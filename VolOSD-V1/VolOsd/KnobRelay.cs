using System;
using System.Collections.Generic;
using System.Linq;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("VolOsd.Tests")]

namespace VolOsd
{
    /// <summary>
    /// Makes a hardware volume knob control the current default device instead of only its own.
    ///
    /// A knob on a sound card moves that card's endpoint volume and nothing else. If the card isn't
    /// the device you're listening through, the knob is inert. This watches the knob's endpoint and
    /// applies its movement (the delta, not the absolute value) to the default device instead.
    ///
    /// It stands down automatically when the knob's device IS in the audio path (directly, or via an
    /// enhancer that mirrors it), because there the knob already works and relaying would form a
    /// feedback loop.
    ///
    /// This class deliberately never writes to the knob's own device - only reads it. An earlier
    /// version did write to it (parking it mid-range so the knob would not go dead at 0%/100%, then
    /// restoring it afterward), which seemed safely contained to a device nothing was listening
    /// through. It was not: third-party audio-enhancement software (this was found with FxSound) can
    /// mirror whatever device it is currently routed to in software, a relationship with no discoverable
    /// signal - no shared name, no registry entry, nothing this class can query. Writing to the knob
    /// device leaked straight through that mirror into the actual audible default, exactly like the
    /// hardware-sibling-endpoint problem this class already guards against, but through a path with no
    /// way to detect it in advance. The fix is not a smarter detector; it is to never write to the knob
    /// device at all, so there is nothing to leak. The cost: the knob goes dead at 0%/100% until turned
    /// back the other way, same as it would with no software involved.
    /// </summary>
    public sealed class KnobRelay : IDisposable
    {
        private readonly IAudioDeviceSource _monitor;
        private readonly AppSettings _settings;

        private readonly object _lock = new();
        private readonly object _groupLock = new();
        private readonly Dictionary<string, (float Value, long ExpiryTicks)> _ourWrites = new();
        private readonly Dictionary<string, long> _echoSuppressUntilTicks = new();
        private readonly Dictionary<string, List<string>> _groupCache = new();

        private float _lastKnobValue = -1f;
        private bool _disabledForSafety;

        // Rate limiting: the failure mode of this feature is volume running away, which is painful
        // and potentially harmful on headphones, so this trips well below what a human could produce
        // by hand and, when it does, turns the feature off for the rest of the session rather than
        // risk repeating whatever caused it.
        private long _budgetWindowStartTicks;
        private float _appliedInWindow;

        // A second, longer-running budget alongside the one above. The short one only ever sees a fast
        // burst - it resets every second, so a steady drift of a few points every several hundred
        // milliseconds (real incident: FxSound's own auto-leveling nudging the X3's real hardware volume
        // on roughly a 0.75s cadence, picked up and relayed as if each nudge were a fresh knob turn) never
        // trips it, since no single 1-second window ever holds much. That drift ran the default from 2%
        // to 66% over about nine seconds - "the volume keeps increasing even after you've stopped turning
        // it," because it was never the knob doing it. This catches sustained movement the short window
        // is structurally blind to, without needing to identify the source.
        private long _longBudgetWindowStartTicks;
        private float _appliedInLongWindow;

        private readonly Func<long> _now;

        private static readonly long WriteSuppressionTicks = TimeSpan.FromMilliseconds(400).Ticks;
        private static readonly long EchoSuppressionTicks = TimeSpan.FromMilliseconds(600).Ticks;
        private const float MaxSingleStep = 0.15f;   // bigger than this isn't a knob detent

        // 50 points/sec was the original ceiling here, chosen as "well above real use, well below a
        // runaway" - but a diagnostics report showed a real session hitting exactly that: "moved 52
        // points in under a second" before tripping, with the default device (FxSound) mirroring the
        // knob's own card at the time. The trip did its job - it stopped an unbounded runaway - but by
        // then a real, audible ~50-point jump had already happened, which is exactly the "turns the
        // volume up too much" this feature exists to prevent. The ceiling itself was the gap: it correctly
        // bounds worst-case damage, but the bound was too generous. Tightened to 20 points/sec, still well
        // above what a deliberate volume nudge needs, to cap that worst case much lower.
        private const float BudgetPerSecond = 0.2f;

        private static readonly long LongBudgetWindowTicks = TimeSpan.FromSeconds(5).Ticks;
        private const float LongBudgetPerWindow = 0.3f; // 30 points over 5 seconds - generous for a deliberate big adjustment, tight against a sustained drift

        /// <summary>Raised when the relay moved the default device, so the OSD can show that
        /// device's level rather than the knob's meaningless one.</summary>
        public event Action<float, bool>? DefaultVolumeRelayed;

        /// <summary>Raised once if the rate limiter trips. The feature is off for the rest of the
        /// session at that point; this exists purely so the app can tell the user why.</summary>
        public event Action? DisabledForSafety;

        public KnobRelay(IAudioDeviceSource monitor, AppSettings settings)
            : this(monitor, settings, () => DateTime.UtcNow.Ticks)
        {
        }

        /// <summary>Test-only seam: the long budget window (see <see cref="LongBudgetWindowTicks"/>) is
        /// several real seconds wide, and a test proving it trips needs to simulate that elapsed time
        /// without an actual multi-second sleep.</summary>
        internal KnobRelay(IAudioDeviceSource monitor, AppSettings settings, Func<long> nowTicks)
        {
            _monitor = monitor;
            _settings = settings;
            _now = nowTicks;
            CaptureBaseline();
            _monitor.VolumeChanged += OnVolumeChanged;
        }

        /// <summary>
        /// Seeds the reference point the first real delta is measured against. Without this, the first
        /// event after startup would be compared against nothing and could compute a bogus jump.
        /// </summary>
        public void CaptureBaseline()
        {
            if (!Enabled) return;
            if (!_monitor.TryGetVolume(_settings.KnobDeviceId, out float current)) return;

            lock (_lock) { _lastKnobValue = current; }
            Diagnostics.Log($"KnobRelay: baseline for knob device is {current * 100:0.0}%");
        }

        private bool Enabled => !_disabledForSafety && !string.IsNullOrEmpty(_settings.KnobDeviceId);

        private void OnVolumeChanged(VolumeChange change)
        {
            if (!Enabled) return;
            if (change.DeviceId != _settings.KnobDeviceId) return;

            if (WasOurWrite(change.DeviceId, change.Volume))
                return;

            long nowTicks = _now();

            // A card can expose several endpoints the driver keeps in sync (the X3 has "Speakers" and
            // "SPDIF Out"). If the DEFAULT device is any endpoint of the knob's own card - not
            // necessarily this exact one - the knob is already audible via native hardware: turning it
            // is changing the default's volume directly, in parallel with this event. Relaying a delta
            // on top of that races the hardware and double-applies the movement. Membership in the
            // card group (not raw ID equality against a single endpoint) is what actually answers
            // "is this card in the path", so it must be checked here too, not only for the OSD.
            if (DefaultIsKnobPath(_monitor.DefaultDeviceId))
            {
                lock (_lock) { _lastKnobValue = change.Volume; }
                return;
            }

            HandleKnobMoved(change.Volume, nowTicks);
        }

        private void HandleKnobMoved(float value, long nowTicks)
        {
            float delta;
            string defaultId = _monitor.DefaultDeviceId;

            lock (_lock)
            {
                if (_lastKnobValue < 0)
                {
                    _lastKnobValue = value;
                    return;
                }

                delta = value - _lastKnobValue;

                if (Math.Abs(delta) < 0.0005f)
                {
                    _lastKnobValue = value;
                    return;
                }

                if (Math.Abs(delta) > MaxSingleStep)
                {
                    // Deliberately does NOT update _lastKnobValue: an implausible jump becoming the
                    // new reference point would mean the next genuine turn computes its delta from a
                    // value the physical knob never actually passed through - most likely a stray
                    // notification (an echo of some other write) rather than a real turn, and the one
                    // case that matters most is an echo of this class's own relay write bouncing back
                    // via an undetected software mirror (see class remarks). Keeping the old reference
                    // means a wrongly-accepted echo self-corrects on the very next real turn instead of
                    // permanently skewing every future delta.
                    Diagnostics.Log($"KnobRelay: ignoring implausible step {delta * 100:0.0} points, keeping prior reference point");
                    return;
                }

                _lastKnobValue = value;

                // Token bucket over a rolling second. Tripping this disables the feature outright
                // (see TripSafety) rather than pausing - a pause would let the same fault repeat.
                if (nowTicks - _budgetWindowStartTicks > TimeSpan.TicksPerSecond)
                {
                    _budgetWindowStartTicks = nowTicks;
                    _appliedInWindow = 0f;
                }
                _appliedInWindow += Math.Abs(delta);
                if (_appliedInWindow > BudgetPerSecond)
                {
                    TripSafety($"moved {_appliedInWindow * 100:0} points in under a second");
                    return;
                }

                // See the field comment: this is the same token-bucket shape as above, just wider, to
                // catch a sustained drift that never trips the 1-second bucket because no single second
                // of it carries much.
                if (nowTicks - _longBudgetWindowStartTicks > LongBudgetWindowTicks)
                {
                    _longBudgetWindowStartTicks = nowTicks;
                    _appliedInLongWindow = 0f;
                }
                _appliedInLongWindow += Math.Abs(delta);
                if (_appliedInLongWindow > LongBudgetPerWindow)
                {
                    TripSafety($"moved {_appliedInLongWindow * 100:0} points over a sustained few seconds");
                    return;
                }
            }

            if (string.IsNullOrEmpty(defaultId)) return;
            if (!_monitor.TryGetVolume(defaultId, out float currentDefault)) return;

            float target = Math.Clamp(currentDefault + delta, 0f, 1f);
            if (Math.Abs(target - currentDefault) > 0.0001f)
            {
                RecordOurWrite(defaultId, target);

                // Also record it under the knob device's own id. If the default happens to be an
                // undetected software mirror of the knob (the FxSound-shaped failure mode from the
                // class remarks, just with the mirror direction reversed), this write can bounce back
                // as a notification on the knob device itself, indistinguishable from a real turn
                // except by value. WasOurWrite's tight match only fires on an actual echo at this
                // exact value, so a genuine subsequent turn - which lands near the knob's own prior
                // value, not this one - is unaffected.
                //
                // This can't be widened into the same unconditional time-window suppression used below
                // for sibling endpoints: unlike the default's siblings, the knob device is exactly where
                // legitimate follow-up turns keep arriving, so blanket-suppressing it after every write
                // was tried and breaks continuous turning outright (confirmed by
                // MultipleSmallTurns_TrackCumulatively and the mirror-echo regression test both failing
                // when that was attempted) - it must stay a value match, not a time window.
                RecordOurWrite(_settings.KnobDeviceId, target);

                if (_monitor.TrySetVolume(defaultId, target))
                {
                    // Belt-and-suspenders beyond the group-membership check above: if the device we
                    // just wrote has sibling endpoints, hardware sync can echo this write back as a
                    // notification on them a moment later, which would otherwise look exactly like a
                    // fresh knob turn and drive another write - a closed loop. This suppresses that
                    // regardless of the echoed value, since per-endpoint dB rounding means it will not
                    // always match the tight value-based check in WasOurWrite.
                    SuppressEcho(GetCardGroup(defaultId), nowTicks);
                    Diagnostics.Log($"KnobRelay: relayed {delta * 100:+0.0;-0.0} to default, now {target * 100:0.0}%");
                    DefaultVolumeRelayed?.Invoke(target, false);
                }
            }
            else
            {
                // Already at an end stop - still surface it so the OSD reflects reality.
                DefaultVolumeRelayed?.Invoke(currentDefault, false);
            }
        }

        /// <summary>Disables the relay for the rest of the process lifetime. Deliberately not
        /// recoverable without a restart: a timed pause could just repeat whatever tripped it.</summary>
        private void TripSafety(string reason)
        {
            _disabledForSafety = true;
            Diagnostics.Log($"KnobRelay: SAFETY TRIP - {reason}. Disabled for the rest of this session.");
            DisabledForSafety?.Invoke();
        }

        private List<string> GetKnobDeviceGroup() => GetCardGroup(_settings.KnobDeviceId);

        /// <summary>Endpoints belonging to the same physical card as <paramref name="deviceId"/>,
        /// matched on the device name the driver puts in parentheses - e.g. both
        /// "Speakers (Sound Blaster X3)" and "SPDIF Out (Sound Blaster X3)" resolve to the same group.
        /// This only catches hardware siblings; it has no way to see a software mirror like FxSound's
        /// (see the class remarks) - which is exactly why writing to the knob device is avoided instead
        /// of trying to extend this detection to cover that case too.</summary>
        private List<string> GetCardGroup(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return new List<string>();

            lock (_groupLock)
            {
                if (_groupCache.TryGetValue(deviceId, out var cached))
                    return cached;

                var devices = _monitor.GetRenderDevices();
                var target = devices.FirstOrDefault(d => d.Id == deviceId);

                List<string> group;
                if (target.Id == null)
                {
                    group = new List<string> { deviceId };
                }
                else
                {
                    var card = CardName(target.Name);
                    group = string.IsNullOrEmpty(card)
                        ? new List<string> { deviceId }
                        : devices.Where(d => CardName(d.Name) == card).Select(d => d.Id).ToList();
                }

                _groupCache[deviceId] = group;
                return group;
            }
        }

        /// <summary>
        /// True when this device's level is meaningless to display: it belongs to the knob's card,
        /// and that card isn't what you're listening through.
        /// </summary>
        public bool ShouldSuppressOsd(string deviceId)
        {
            if (!Enabled) return false;

            var group = GetKnobDeviceGroup();
            if (!group.Contains(deviceId)) return false;

            // If the card IS the output - directly, or via a declared passthrough enhancer - its
            // numbers are the real ones - show them.
            return !DefaultIsKnobPath(_monitor.DefaultDeviceId);
        }

        /// <summary>
        /// True when the current default is, functionally, the knob's own card - either directly/via a
        /// hardware sibling endpoint (detectable), or because it's secretly what an enhancer is really
        /// rendering to underneath a fixed virtual device. Either way the knob already reaches the
        /// listener natively, so relaying would double it and its own reading is the real number, not a
        /// meaningless one.
        /// </summary>
        private bool DefaultIsKnobPath(string defaultDeviceId)
        {
            var knobGroup = GetKnobDeviceGroup();
            if (knobGroup.Contains(defaultDeviceId)) return true;

            if (!string.IsNullOrEmpty(_settings.KnobPassthroughDeviceId) && defaultDeviceId == _settings.KnobPassthroughDeviceId)
                return true;

            // FxSound auto-follows whichever real device was last active and gives Windows no way to
            // see that - but it does record that real device in its own registry state (see
            // IAudioDeviceSource.FxSoundRealPlaybackDeviceId). That's a live, self-correcting signal,
            // better than the static id above, but only trustworthy when the CURRENT default actually
            // looks like an FxSound device - otherwise a stale value from a past FxSound session could
            // wrongly stand this down while listening through something FxSound has nothing to do with.
            if (IsLikelyFxSoundDevice(defaultDeviceId))
            {
                var realId = _monitor.FxSoundRealPlaybackDeviceId;
                if (!string.IsNullOrEmpty(realId) && knobGroup.Contains(realId))
                    return true;
            }

            return false;
        }

        private bool IsLikelyFxSoundDevice(string deviceId) =>
            _monitor.GetRenderDevices().Any(d => d.Id == deviceId && d.Name.Contains("FxSound", StringComparison.OrdinalIgnoreCase));

        private static string CardName(string friendlyName)
        {
            int open = friendlyName.LastIndexOf('(');
            int close = friendlyName.LastIndexOf(')');
            return open >= 0 && close > open ? friendlyName[(open + 1)..close] : "";
        }

        private void RecordOurWrite(string deviceId, float value)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            lock (_lock)
                _ourWrites[deviceId] = (value, _now() + WriteSuppressionTicks);
        }

        /// <summary>Unconditionally ignores any change reported on these devices for a short window,
        /// regardless of what value they report. Used after a write to a device that might have
        /// sibling endpoints, where hardware sync can echo the write back at a slightly different
        /// (per-endpoint dB-rounded) value than the tight match in <see cref="WasOurWrite"/> expects.</summary>
        private void SuppressEcho(IEnumerable<string> deviceIds, long nowTicks)
        {
            long until = nowTicks + EchoSuppressionTicks;
            lock (_lock)
            {
                foreach (var id in deviceIds)
                    _echoSuppressUntilTicks[id] = until;
            }
        }

        /// <summary>Our own writes come back as notifications; acting on them is what would create a
        /// feedback loop, so they are consumed here.</summary>
        private bool WasOurWrite(string deviceId, float value)
        {
            lock (_lock)
            {
                if (_echoSuppressUntilTicks.TryGetValue(deviceId, out var until))
                {
                    if (_now() <= until) return true;
                    _echoSuppressUntilTicks.Remove(deviceId);
                }

                if (!_ourWrites.TryGetValue(deviceId, out var pending)) return false;
                if (_now() > pending.ExpiryTicks)
                {
                    _ourWrites.Remove(deviceId);
                    return false;
                }
                // Tight: the recorded value is what the driver actually stored (read back after
                // writing), not what we asked for. A loose match here would swallow real knob steps,
                // which are only ~2 points apart.
                return Math.Abs(value - pending.Value) < 0.005f;
            }
        }

        public void Dispose() => _monitor.VolumeChanged -= OnVolumeChanged;
    }
}
