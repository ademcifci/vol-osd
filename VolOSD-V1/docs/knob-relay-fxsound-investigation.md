# Knob relay + FxSound investigation

**Status as of 2026-09-12: fixed for FxSound specifically; one loose end never fully explained (see [Open items](#open-items)).**

This documents a debugging session covering three related but distinct bugs in
`KnobRelay` (the "hardware knob controls the default device" feature), found by
working through real diagnostics reports rather than guessing. Kept here so the
reasoning and the dead ends aren't lost.

## The original complaint

> "There still seems to be scenarios where you turn the knob, and it turns the
> volume up too much. I thought we fixed that, how is it still possible?"

The earlier fix being referred to (documented in `KnobRelay.cs`'s class remarks
and in the README) was: never write to the knob's own device, because doing so
had leaked through an undetectable FxSound mirror into the real output. That
fix was correct and stayed in place. It didn't fully solve the complaint above,
because it addressed a different failure mode than the ones found here.

## Setup

- Sound Blaster X3, exposing (at least) two endpoints: `Speakers (Sound Blaster X3)`
  and `SPDIF Out (Sound Blaster X3)`.
- FxSound (audio enhancer) normally active, its own virtual device
  (`FxSound Speakers`) set as the Windows default.
- Knob device configured in VolOsd as `SPDIF Out (Sound Blaster X3)`.
- A Jabra EVOLVE 20 SE headset as an alternate, unrelated output.

## Finding 1: the safety net's ceiling was too loose

Diagnostics showed the rate limiter tripping only after real jumps like
`moved 52 points in under a second`. The trip logic itself worked (it stopped
things), but by the time it fired, a real ~50-point jump had already happened -
audibly "too much" even though the backstop technically did its job.

**Fix** (`KnobRelay.cs`):
- `BudgetPerSecond` tightened from `0.5f` (50 pts/sec) to `0.2f` (20 pts/sec).
- Added a second, longer-window budget: `LongBudgetPerWindow = 0.3f` over
  `LongBudgetWindowTicks` (5 seconds). The 1-second budget structurally can't
  catch a slow sustained drift where no single second carries much - this
  closes that gap. See `_appliedInLongWindow` / `_longBudgetWindowStartTicks`.
- Added logging for successful relay writes (`KnobRelay: relayed ...`) - there
  previously was none, only rejections and trips, which made later diagnostics
  much harder to read.
- Added an injectable clock (`Func<long> _now`, internal constructor +
  `InternalsVisibleTo`) so the long-window behavior could be tested without
  real multi-second sleeps.

Tests: `RapidMovement_CapsActualRelayedMovementWellBelowOldCeiling`,
`SustainedDrift_AcrossSeveralSeconds_TripsTheLongerBudget` in `KnobRelayTests.cs`.

## Finding 2: FxSound's own auto-leveling was being read as knob turns

Even with the tightened budget, diagnostics kept showing a near-perfectly
regular ~750ms cadence of `relayed +4.0 to default` events, climbing the
default from ~2% to 60%+ over several seconds - far too regular to be a hand on
a knob, and it kept going even when nothing was being touched.

**Confirmed by isolation test**: with FxSound fully disabled, and a genuinely
unrelated default device (Jabra) selected, a multi-minute test run produced a
completely clean log - no implausible-step floods, no phantom drift, no trips.
With FxSound re-enabled, the pattern came back. This isolated FxSound (not the
knob, not VolOsd's own logic) as the source of the phantom movement.

**Root cause, confirmed by reading FxSound's own source**
(`github.com/fxsound2/fxsound-app`, GPL-3, specifically
`audiopassthru/src/sndDevices/sndDevicesVolCallbacks.cpp`): FxSound registers
`IAudioEndpointVolumeCallback` on both its own internal "capture" device (the
DFX/virtual device Windows shows as `FxSound Speakers`) and the real "playback"
device it's actually routed to, and keeps their volumes bidirectionally
synced - any change on one gets written to the other, guarded only against
FxSound's own writes (via a GUID it tags its own calls with, which we don't
have and can't reuse). Something inside FxSound's own processing was
programmatically nudging its internal volume, and that sync mechanism wrote
those nudges straight onto the *real* hardware endpoint - which happened to be
the exact endpoint configured as the knob. VolOsd had no way to distinguish
that from a real physical turn.

We suspected FxSound's "Dynamic Boost" feature specifically (it describes
automatically lifting quiet audio, a plausible source of continuous gain
changes) but **this was never confirmed in isolation** - the test that ruled
FxSound in was FxSound fully off, not Dynamic Boost off with everything else
on. If phantom movement ever returns with FxSound active, that's the next
thing to isolate (see [Open items](#open-items)).

**No code fix for this part** - it's not something VolOsd can distinguish from
a real turn at the source. The mitigation is Finding 1's tightened safety net,
plus Finding 3's stand-down logic correctly recognizing when FxSound is
genuinely routed through the knob's own card (in which case relaying was never
needed anyway - see below).

## Finding 3: the knob's own hardware floor, not a bug

Separately, the user observed: turning the knob up worked, but turning it back
down seemed to stop at wherever it started, never going lower.

**Cause**: the physical knob's own device (`SPDIF Out`) happened to be resting
very close to its own 0% floor. `KnobRelay` deliberately never writes to the
knob's own device (that's Finding 0, the original fix), so once the knob's own
volume hits 0%, it has nothing further to report moving *toward* - it "goes
dead" until turned back up first. This is documented, intentional behavior, not
a regression. No code change; explained the mechanism and suggested resting the
physical knob mid-range during idle periods as a practical workaround.

## Finding 4: FxSound's default is not a fixed device underneath

Initial attempt at automating "don't relay when FxSound is secretly the knob's
card" was a static setting (`KnobPassthroughDeviceId`): declare once that
`FxSound Speakers` really means the X3, and stand down whenever that's the
current default.

**This was wrong.** The user pointed out that FxSound automatically follows
whichever real device is actually active - when they switch to the Jabra,
FxSound starts rendering to the Jabra instead of the X3, while still presenting
the exact same `FxSound Speakers` device to Windows. A static "X = Y" mapping
can't represent a relationship that changes on its own, and would have wrongly
suppressed the knob the moment FxSound moved to a device the knob has no
relationship to.

**Real fix, again found by reading FxSound's source**
(`sndDevicesImplementDeviceRules.cpp` + `sndDevicesReg.cpp`): FxSound always
forces the Windows default to its own fixed virtual device and separately
auto-selects the real target using a rule chain (user selection, newly-plugged
device, most-recently-used, prior default, ...). That real target is invisible
to Windows - but FxSound *does* persist it, in its own registry state, as part
of its own device-selection bookkeeping:

```
HKCU\SOFTWARE\DFX\13\23\devices\most_recent_playback   (default value = device id)
```

Confirmed present and populated with a real, valid device id on the user's own
machine (see the PowerShell checks run during this session). `13` and `23` are
constants baked into FxSound's own source (`(int)DFX_VERSION` and
`DFXP_VENDOR_CODE_UNIVERSAL`) - not documented, not guaranteed stable across
FxSound versions.

**Fix** (`IAudioDeviceSource.cs`, `AudioMonitor.cs`, `KnobRelay.cs`):
- `IAudioDeviceSource.FxSoundRealPlaybackDeviceId` - reads that registry value
  live, wrapped in try/catch, returns `null` on any failure (missing key,
  future FxSound version with a different structure, etc.) rather than ever
  throwing.
- `KnobRelay.DefaultIsKnobPath` now checks, in order: (1) literal card-group
  membership as before, (2) the static `KnobPassthroughDeviceId` fallback
  setting, (3) - only when the *current* default's name looks like an FxSound
  device - the live registry value. That guard matters: the registry value is
  a snapshot from whenever FxSound last ran its selection logic, not
  continuously updated, so trusting it when the current default isn't even an
  FxSound device could wrongly stand the relay down using stale data.
- `KnobPassthroughDeviceId` (the static setting from the wrong first attempt)
  was kept, not removed - it's a reasonable fallback for other enhancers that
  don't expose anything like FxSound's registry state, and it's harmless when
  unset.

Tests: `FxSoundRegistryHint_RealDeviceIsKnobCard_StandsDown_NoWrite`,
`FxSoundRegistryHint_UpdatesLive_AsFxSoundsRealTargetChanges_NoSettingsInvolved`,
`FxSoundRegistryHint_Ignored_WhenCurrentDefaultIsNotAnFxSoundDevice` in
`KnobRelayTests.cs`.

## Current status

- Safety net tightened and given a second, longer-window check (Finding 1).
- Relay writes are now logged, closing a diagnostics blind spot.
- FxSound's phantom volume nudges are understood and root-caused, though not
  preventable at the source - the tightened safety net is the mitigation.
- The knob-floor behavior (Finding 3) is confirmed working-as-intended, not a
  bug.
- FxSound's real target is now detected live via its own registry state, with
  a static-declaration fallback for other software (Finding 4). This should
  make the relay correctly stand down/re-engage automatically as the user
  switches outputs through FxSound, with no manual toggling.

## Open items

- **Unexplained log anomaly**: two early diagnostics reports showed multiple
  `SAFETY TRIP ... Disabled for the rest of this session` entries within the
  same continuous app run (no intervening `Startup begin`), which is
  logically impossible given `_disabledForSafety` has no reset path and
  `KnobRelay` is constructed exactly once per process. Investigated
  extensively (stale build, duplicate process via Task Manager) without a
  confirmed explanation. Task Manager showed only one process both times it
  was checked, ruling out a simple duplicate-launch explanation. This pattern
  did not recur in later, single-instance-confirmed clean test runs, so it's
  parked rather than resolved - worth another look if it reappears, ideally
  with the exact sequence of user actions leading up to it.
- **Dynamic Boost not isolated**: FxSound was ruled in as a category (fully
  off vs. fully on), but the specific feature responsible (suspected: Dynamic
  Boost) was never tested in isolation. If phantom relay activity ever returns
  while FxSound is active, try disabling just Dynamic Boost before assuming
  the registry-based fix has a gap.
- **FxSound registry path is unversioned/undocumented**: `SOFTWARE\DFX\13\23\...`
  reflects FxSound's own internal version/vendor constants at the time of this
  investigation (source read from the `main` branch of
  `github.com/fxsound2/fxsound-app`). A future FxSound release could change
  this path; the code fails silently (returns `null`, falls back to prior
  behavior) rather than breaking, but that also means a future break here
  would be silent - if the "stand down while on FxSound" behavior stops
  working after an FxSound update, check this first.
