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

        private static FakeAudioDeviceSource MakeSource()
        {
            var source = new FakeAudioDeviceSource();
            source.AddDevice(X3Speakers, "Speakers (Sound Blaster X3)", 0.08f);
            source.AddDevice(X3Spdif, "SPDIF Out (Sound Blaster X3)", 0.08f);
            source.AddDevice(Jabra, "Speakers (Jabra EVOLVE 20 SE)", 0.50f);
            return source;
        }

        private static AppSettings MakeSettings(string knobDeviceId) =>
            new() { KnobDeviceId = knobDeviceId };

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
    }
}
