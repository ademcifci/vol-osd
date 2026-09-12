using X3VolOsd;

namespace X3VolOsd.Tests
{
    public class KnobDeviceHelperTests
    {
        private const string X3Speakers = "x3-speakers";
        private const string X3Spdif = "x3-spdif";
        private const string Jabra = "jabra";

        private static List<RenderDevice> MakeDevices() =>
        [
            new(X3Speakers, "Speakers (Sound Blaster X3)"),
            new(X3Spdif, "SPDIF Out (Sound Blaster X3)"),
            new(Jabra, "Speakers (Jabra EVOLVE 20 SE)"),
        ];

        [Fact]
        public void IsKnobSource_ExactMatch_ReturnsTrue()
        {
            var devices = MakeDevices();
            Assert.True(KnobDeviceHelper.IsKnobSource(X3Spdif, X3Spdif, devices));
        }

        [Fact]
        public void IsKnobSource_SiblingOnSameCard_ReturnsTrue()
        {
            var devices = MakeDevices();
            Assert.True(KnobDeviceHelper.IsKnobSource(X3Speakers, X3Spdif, devices));
        }

        [Fact]
        public void IsKnobSource_UnrelatedDevice_ReturnsFalse()
        {
            var devices = MakeDevices();
            Assert.False(KnobDeviceHelper.IsKnobSource(Jabra, X3Spdif, devices));
        }

        [Fact]
        public void IsKnobSource_EmptyKnobId_ReturnsFalse()
        {
            var devices = MakeDevices();
            Assert.False(KnobDeviceHelper.IsKnobSource(X3Spdif, "", devices));
        }

        [Fact]
        public void TryAutoDetectKnobDevice_PrefersSpdif()
        {
            var devices = MakeDevices();
            Assert.Equal(X3Spdif, KnobDeviceHelper.TryAutoDetectKnobDevice(devices));
        }

        [Fact]
        public void TryAutoDetectKnobDevice_NoX3_ReturnsNull()
        {
            Assert.Null(KnobDeviceHelper.TryAutoDetectKnobDevice([new(Jabra, "Speakers (Jabra)")]));
        }
    }
}
