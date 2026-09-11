using VolOsd;

namespace VolOsd.Tests
{
    /// <summary>
    /// KnobRelay is the highest-risk class in this app: it writes system volume in response to
    /// hardware events with no confirmation step, and every real bug found in it during development
    /// was a pure logic error - reachable and provable here without touching real audio hardware.
    /// Each test below is anchored to a specific incident; the comment on each names it.
    /// </summary>
    public class KnobRelayTests
    {
        private const string X3Speakers = "x3-speakers";
        private const string X3Spdif = "x3-spdif";
        private const string Jabra = "jabra";
        private const string FxSound = "fxsound-speakers";

        private static FakeAudioDeviceSource MakeSource()
        {
            var source = new FakeAudioDeviceSource();
            source.AddDevice(X3Speakers, "Speakers (Sound Blaster X3)", 0.08f);
            source.AddDevice(X3Spdif, "SPDIF Out (Sound Blaster X3)", 0.08f);
            source.AddDevice(Jabra, "Speakers (Jabra EVOLVE 20 SE)", 0.50f);
            source.AddDevice(FxSound, "FxSound Speakers (FxSound Audio Enhancer)", 0.08f);
            return source;
        }

        private static AppSettings MakeSettings(string knobDeviceId) =>
            new() { KnobDeviceId = knobDeviceId };

        private static AppSettings MakeSettings(string knobDeviceId, string passthroughDeviceId) =>
            new() { KnobDeviceId = knobDeviceId, KnobPassthroughDeviceId = passthroughDeviceId };

        // Incident: default = one X3 endpoint, knob = the sibling endpoint. The knob's card was
        // already audible via native hardware, but the relay only compared exact endpoint IDs and
        // didn't recognise it - it wrote a delta onto the default in parallel with the native change
        // already happening, racing the hardware, which is what produced the audible jumping.
        [Fact]
        public void SiblingEndpointOfKnobAsDefault_StandsDown_NoWrite()
        {
            var source = MakeSource();
            source.DefaultDeviceId = X3Speakers; // sibling of the knob device, not the same id
            var relay = new KnobRelay(source, MakeSettings(X3Spdif));

            source.RaiseVolumeChanged(X3Spdif, 0.10f);
            source.RaiseVolumeChanged(X3Spdif, 0.12f);

            Assert.Empty(source.Writes);
        }

        [Fact]
        public void ExactSameDeviceAsDefault_StandsDown_NoWrite()
        {
            var source = MakeSource();
            source.DefaultDeviceId = X3Spdif;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif));

            source.RaiseVolumeChanged(X3Spdif, 0.10f);
            source.RaiseVolumeChanged(X3Spdif, 0.12f);

            Assert.Empty(source.Writes);
        }

        // The actual intended use case: knob and default are unrelated devices.
        [Fact]
        public void UnrelatedDefault_RelaysDelta()
        {
            var source = MakeSource();
            source.DefaultDeviceId = Jabra; // 0.50 initial
            var relay = new KnobRelay(source, MakeSettings(X3Spdif)); // baseline captured at 0.08

            float? relayed = null;
            relay.DefaultVolumeRelayed += (v, _) => relayed = v;

            source.RaiseVolumeChanged(X3Spdif, 0.10f); // +0.02

            var write = Assert.Single(source.Writes);
            Assert.Equal(Jabra, write.DeviceId);
            Assert.Equal(0.52f, write.Volume, 3);
            Assert.Equal(0.52f, relayed!.Value, 3);
        }

        [Fact]
        public void MultipleSmallTurns_TrackCumulatively()
        {
            var source = MakeSource();
            source.DefaultDeviceId = Jabra;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif));

            source.RaiseVolumeChanged(X3Spdif, 0.10f); // +0.02 -> 0.52
            source.RaiseVolumeChanged(X3Spdif, 0.08f); // -0.02 -> 0.50
            source.RaiseVolumeChanged(X3Spdif, 0.06f); // -0.02 -> 0.48

            Assert.Equal(3, source.Writes.Count);
            Assert.Equal(0.48f, source.Writes[^1].Volume, 3);
        }

        // Regression for the parking-mechanism bug: FxSound (or any audio-enhancement software) can
        // mirror whatever device it is routed to, in software, with no discoverable signal - no shared
        // name, nothing queryable. An earlier version wrote to the knob's own device to keep it off its
        // end stops; that write leaked through an undetectable mirror into the real output, audibly.
        // The fix was not a smarter detector but to never write to the knob device at all. This asserts
        // that invariant directly, across a spread of values including both end stops, so it fails
        // loudly if that mechanism is ever reintroduced.
        [Fact]
        public void NeverWritesToTheKnobDeviceItself()
        {
            var source = MakeSource();
            source.DefaultDeviceId = Jabra;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif));

            foreach (var v in new[] { 0.02f, 0.50f, 0.98f, 0.99f, 0.01f, 0.60f })
                source.RaiseVolumeChanged(X3Spdif, v);

            Assert.DoesNotContain(source.Writes, w => w.DeviceId == X3Spdif);
        }

        [Fact]
        public void ImplausibleSingleStep_IsIgnored_AndDoesNotCorruptTheReferencePoint()
        {
            var source = MakeSource();
            source.DefaultDeviceId = Jabra;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif)); // baseline 0.08

            source.RaiseVolumeChanged(X3Spdif, 0.90f); // +0.82, implausible - ignored
            Assert.Empty(source.Writes);

            // Delta must be measured from the ORIGINAL 0.08 reference, not from the rejected 0.90 -
            // an implausible jump must never become the new baseline (see class remarks on TripSafety
            // and the comment in HandleKnobMoved), or every future delta would be skewed by it.
            source.RaiseVolumeChanged(X3Spdif, 0.10f); // +0.02 from 0.08
            var write = Assert.Single(source.Writes);
            Assert.Equal(0.52f, write.Volume, 3);
        }

        // Regression for the theoretical gap flagged during the test-writing pass: RecordOurWrite used
        // to only key suppression by the device actually written to, so if the DEFAULT happened to be
        // an undetected software mirror of the knob - the FxSound-shaped failure mode from the class
        // remarks, mirror direction reversed - a relay write could bounce back as a notification on the
        // knob device with no suppression at all, and (before the ordering fix above) permanently skew
        // the reference point even after being rejected as implausible.
        [Fact]
        public void UndetectedSoftwareMirror_EchoOfRelayWriteOntoKnobDevice_IsNotTreatedAsARealTurn()
        {
            var source = MakeSource();
            source.DefaultDeviceId = Jabra;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif)); // baseline 0.08

            source.RaiseVolumeChanged(X3Spdif, 0.10f); // +0.02 -> writes 0.52 to Jabra
            var firstWrite = Assert.Single(source.Writes);
            Assert.Equal(0.52f, firstWrite.Volume, 3);

            // The (imagined) mirror bounces that write back as a notification on the knob device itself.
            source.RaiseVolumeChanged(X3Spdif, 0.52f);
            Assert.Single(source.Writes); // the echo must not be relayed again

            // A genuine subsequent turn must compute its delta from the real prior knob position
            // (0.10), not from the echoed 0.52 - proving the echo did not corrupt the reference point.
            source.RaiseVolumeChanged(X3Spdif, 0.12f); // +0.02 from 0.10
            Assert.Equal(2, source.Writes.Count);
            Assert.Equal(0.54f, source.Writes[^1].Volume, 3);
        }

        // The circuit breaker: if volume ever moves faster than a real knob could produce, the feature
        // must turn itself off for the rest of the session - not pause, since a timed pause could just
        // repeat whatever caused it. This is the last line of defence if every check above this one is
        // ever wrong at once.
        [Fact]
        public void RapidMovement_TripsRateLimiter_AndDisablesPermanently()
        {
            var source = MakeSource();
            source.DefaultDeviceId = Jabra;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif));

            bool tripped = false;
            relay.DisabledForSafety += () => tripped = true;

            float value = 0.08f;
            for (int i = 0; i < 40; i++)
            {
                value += 0.02f;
                source.RaiseVolumeChanged(X3Spdif, value);
            }

            Assert.True(tripped, "rate limiter should have tripped well before 40 rapid steps");
            int writesAtTrip = source.Writes.Count;
            Assert.True(writesAtTrip < 40, "should have stopped relaying before exhausting all steps");

            // Disabled means disabled - further plausible-looking turns must not resume relaying.
            source.RaiseVolumeChanged(X3Spdif, value + 0.02f);
            Assert.Equal(writesAtTrip, source.Writes.Count);
        }

        // Incident: a diagnostics report from a real session showed the rate limiter tripping only
        // after "moved 52 points in under a second" - the default device (FxSound) was mirroring the
        // knob's own card (Sound Blaster X3) at the time. The trip fired and stopped things going
        // further, but a real, audible ~50-point jump had already landed by then, which is exactly the
        // "turns the volume up too much" this feature exists to prevent. Eventually tripping isn't
        // enough - the ceiling itself has to be tight. This asserts the actual relayed movement before
        // a trip stays well below that old 50-point ceiling.
        [Fact]
        public void RapidMovement_CapsActualRelayedMovementWellBelowOldCeiling()
        {
            var source = MakeSource();
            source.DefaultDeviceId = Jabra; // 0.50 initial
            var relay = new KnobRelay(source, MakeSettings(X3Spdif));

            float value = 0.08f;
            for (int i = 0; i < 40; i++)
            {
                value += 0.02f;
                source.RaiseVolumeChanged(X3Spdif, value);
            }

            float totalMoved = source.Writes.Count == 0 ? 0f : Math.Abs(source.Writes[^1].Volume - 0.50f);
            Assert.True(totalMoved <= 0.25f,
                $"expected real relayed movement well below the old 50-point ceiling, but it moved {totalMoved * 100:0} points");
        }

        // Incident: a diagnostics report showed the default climbing from 2% to 66% over about nine
        // seconds with nothing looking alarming second-by-second - each step was a plausible ~4-point
        // nudge spaced roughly 0.75s apart. That cadence was FxSound's own auto-leveling touching the
        // X3's real hardware volume, not a human hand, but the relay has no way to tell the difference -
        // it just relayed every one of them. The 1-second budget never saw enough in any single window to
        // trip, so the drift kept going: "the volume keeps increasing even after you've stopped turning
        // it." Uses the internal clock seam to simulate several real seconds elapsing without an actual
        // multi-second sleep.
        [Fact]
        public void SustainedDrift_AcrossSeveralSeconds_TripsTheLongerBudget()
        {
            var source = MakeSource();
            source.DefaultDeviceId = Jabra;
            long ticks = 0;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif), () => ticks);

            bool tripped = false;
            relay.DisabledForSafety += () => tripped = true;

            float value = 0.08f;
            float maxAppliedInAnySecond = 0f;
            long lastSecondStart = 0;
            float appliedThisSecond = 0f;

            for (int i = 0; i < 20 && !tripped; i++)
            {
                ticks += TimeSpan.FromMilliseconds(750).Ticks; // well-spaced - never a fast burst
                if (ticks - lastSecondStart > TimeSpan.TicksPerSecond)
                {
                    lastSecondStart = ticks;
                    appliedThisSecond = 0f;
                }
                appliedThisSecond += 0.07f;
                maxAppliedInAnySecond = Math.Max(maxAppliedInAnySecond, appliedThisSecond);

                value += 0.07f;
                source.RaiseVolumeChanged(X3Spdif, value);
            }

            Assert.True(tripped, "a sustained multi-second drift should trip the longer budget");
            Assert.True(maxAppliedInAnySecond < 0.2f,
                "the short 1-second budget should never have come close to tripping on its own here - this is specifically testing the longer window");
        }

        [Fact]
        public void ShouldSuppressOsd_TrueForKnobCard_WhenNotDefault()
        {
            var source = MakeSource();
            source.DefaultDeviceId = Jabra;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif));

            Assert.True(relay.ShouldSuppressOsd(X3Spdif));
            Assert.True(relay.ShouldSuppressOsd(X3Speakers)); // sibling, same card
            Assert.False(relay.ShouldSuppressOsd(Jabra));
        }

        [Fact]
        public void ShouldSuppressOsd_FalseForKnobCard_WhenItIsTheDefault()
        {
            var source = MakeSource();
            source.DefaultDeviceId = X3Speakers; // sibling of knob, and the active output
            var relay = new KnobRelay(source, MakeSettings(X3Spdif));

            // The card IS the output here - its numbers are real and should reach the OSD.
            Assert.False(relay.ShouldSuppressOsd(X3Speakers));
            Assert.False(relay.ShouldSuppressOsd(X3Spdif));
        }

        [Fact]
        public void EmptyKnobDeviceId_DisablesFeatureEntirely()
        {
            var source = MakeSource();
            source.DefaultDeviceId = Jabra;
            var relay = new KnobRelay(source, MakeSettings(""));

            source.RaiseVolumeChanged(X3Spdif, 0.50f);

            Assert.Empty(source.Writes);
            Assert.False(relay.ShouldSuppressOsd(X3Spdif));
        }

        // Manual fallback case: some enhancer plays through the knob's own card under the hood, but
        // Windows exposes it as a completely unrelated-looking default device (no shared name, no
        // card-group match), and unlike FxSound (below) there's no way to ask it what it's really doing.
        // KnobPassthroughDeviceId lets the user state that mapping once instead of toggling the whole
        // feature by hand every time. With it declared, the relay must stand down exactly as it would
        // if the enhancer's device were literally a member of the knob's card group.
        [Fact]
        public void DeclaredPassthroughDevice_AsDefault_StandsDown_NoWrite()
        {
            var source = MakeSource();
            source.DefaultDeviceId = FxSound;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif, FxSound));

            source.RaiseVolumeChanged(X3Spdif, 0.10f);
            source.RaiseVolumeChanged(X3Spdif, 0.12f);

            Assert.Empty(source.Writes);
        }

        [Fact]
        public void DeclaredPassthroughDevice_AsDefault_OsdShowsTheKnobsOwnRealReading()
        {
            var source = MakeSource();
            source.DefaultDeviceId = FxSound;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif, FxSound));

            // The knob's own number IS the real, audible one here - it must reach the OSD, not be
            // suppressed as "meaningless".
            Assert.False(relay.ShouldSuppressOsd(X3Spdif));
            Assert.False(relay.ShouldSuppressOsd(X3Speakers)); // sibling, same card
        }

        [Fact]
        public void DeclaredPassthroughDevice_RelayResumes_WhenDefaultMovesToSomethingElse()
        {
            var source = MakeSource();
            source.DefaultDeviceId = FxSound;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif, FxSound));

            source.RaiseVolumeChanged(X3Spdif, 0.10f); // stood down - the declared passthrough is default
            Assert.Empty(source.Writes);

            // Switch to an output the knob has no native path to - the relay must engage automatically,
            // with no settings change needed.
            source.DefaultDeviceId = Jabra;
            source.RaiseVolumeChanged(X3Spdif, 0.12f); // +0.02 from the last tracked position

            var write = Assert.Single(source.Writes);
            Assert.Equal(Jabra, write.DeviceId);
            Assert.Equal(0.52f, write.Volume, 3);
            Assert.True(relay.ShouldSuppressOsd(X3Spdif)); // meaningless again now that it's not the path
        }

        [Fact]
        public void UndeclaredPassthroughDevice_StillRelaysAsBefore()
        {
            // No KnobPassthroughDeviceId set - an enhancer default with no declared relationship to the
            // knob's card must be treated exactly as before this feature existed.
            var source = MakeSource();
            source.DefaultDeviceId = FxSound;
            var relay = new KnobRelay(source, MakeSettings(X3Spdif));

            source.RaiseVolumeChanged(X3Spdif, 0.10f); // +0.02

            var write = Assert.Single(source.Writes);
            Assert.Equal(FxSound, write.DeviceId);
            Assert.Equal(0.10f, write.Volume, 3);
        }

        // Discovered by reading FxSound's own open-source repo (github.com/fxsound2/fxsound-app,
        // audiopassthru/src/sndDevices/sndDevicesImplementDeviceRules.cpp): it always presents ONE fixed
        // virtual device to Windows, but auto-follows whichever real device was last actually active
        // underneath (confirmed by the user's own description of the behavior), with no way for Windows
        // - or us - to observe that switch. FxSound does record its real target in its own registry
        // state though (sndDevicesReg.cpp, confirmed present and populated with a real device id on a
        // live machine); reading that live is a self-updating replacement for a user having to
        // redeclare KnobPassthroughDeviceId every time FxSound's real target moves on its own.
        [Fact]
        public void FxSoundRegistryHint_RealDeviceIsKnobCard_StandsDown_NoWrite()
        {
            var source = MakeSource();
            source.DefaultDeviceId = FxSound;
            source.FxSoundRealPlaybackDeviceId = X3Speakers; // FxSound is secretly rendering to the X3
            var relay = new KnobRelay(source, MakeSettings(X3Spdif)); // no static passthrough declared

            source.RaiseVolumeChanged(X3Spdif, 0.10f);

            Assert.Empty(source.Writes);
            Assert.False(relay.ShouldSuppressOsd(X3Spdif)); // real number here - show it
        }

        [Fact]
        public void FxSoundRegistryHint_UpdatesLive_AsFxSoundsRealTargetChanges_NoSettingsInvolved()
        {
            var source = MakeSource();
            source.DefaultDeviceId = FxSound;
            source.FxSoundRealPlaybackDeviceId = X3Speakers; // secretly on the X3 right now
            var relay = new KnobRelay(source, MakeSettings(X3Spdif));

            source.RaiseVolumeChanged(X3Spdif, 0.10f); // stood down
            Assert.Empty(source.Writes);

            // FxSound quietly re-targets itself to the Jabra - nothing in Windows reflects this except
            // FxSound's own registry state updating. No app setting changes; the relay must notice on
            // its own via the live registry read.
            source.FxSoundRealPlaybackDeviceId = Jabra;
            source.RaiseVolumeChanged(X3Spdif, 0.12f); // +0.02 from the last tracked position

            var write = Assert.Single(source.Writes);
            Assert.Equal(FxSound, write.DeviceId); // still relay onto the Windows-visible default
            Assert.Equal(0.10f, write.Volume, 3); // FxSound's own volume (0.08 initial) + 0.02
        }

        // Guard: the registry value is a snapshot from whenever FxSound last ran its device-selection
        // logic, not something updated the instant it changes. If the CURRENT default isn't even an
        // FxSound device, a stale value naming the knob's card must not wrongly stand the relay down -
        // that would silently break the knob for a setup that has nothing to do with FxSound.
        [Fact]
        public void FxSoundRegistryHint_Ignored_WhenCurrentDefaultIsNotAnFxSoundDevice()
        {
            var source = MakeSource();
            source.DefaultDeviceId = Jabra; // genuinely Jabra, no FxSound involved right now
            source.FxSoundRealPlaybackDeviceId = X3Speakers; // stale value from some earlier session
            var relay = new KnobRelay(source, MakeSettings(X3Spdif));

            source.RaiseVolumeChanged(X3Spdif, 0.10f); // +0.02, must relay normally

            var write = Assert.Single(source.Writes);
            Assert.Equal(Jabra, write.DeviceId);
            Assert.Equal(0.52f, write.Volume, 3);
        }
    }
}
