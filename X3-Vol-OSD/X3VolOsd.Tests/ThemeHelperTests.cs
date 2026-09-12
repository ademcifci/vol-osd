using X3VolOsd;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace X3VolOsd.Tests
{
    public class ThemeHelperTests
    {
        [Fact]
        public void ParseColor_ValidHex_Parses()
        {
            var c = ThemeHelper.ParseColor("#FF8000", Colors.Black);
            Assert.Equal((byte)0xFF, c.R);
            Assert.Equal((byte)0x80, c.G);
            Assert.Equal((byte)0x00, c.B);
        }

        [Fact]
        public void ParseColor_Invalid_ReturnsFallback()
        {
            var c = ThemeHelper.ParseColor("not-a-color", Colors.Lime);
            Assert.Equal(Colors.Lime, c);
        }

        [Fact]
        public void ParseColor_Empty_ReturnsFallback()
        {
            var c = ThemeHelper.ParseColor("", Colors.Lime);
            Assert.Equal(Colors.Lime, c);
        }

        [Fact]
        public void ToHex_RoundTripsThroughParseColor()
        {
            var original = Color.FromRgb(0x12, 0x34, 0x56);
            var hex = ThemeHelper.ToHex(original);
            var parsed = ThemeHelper.ParseColor(hex, Colors.Black);
            Assert.Equal(original, parsed);
        }

        [Theory]
        [InlineData("accent")]
        [InlineData("Accent")]
        [InlineData("  accent  ")]
        public void IsSystemAccentToken_CaseAndWhitespaceInsensitive(string value)
        {
            Assert.True(ThemeHelper.IsSystemAccentToken(value));
        }

        [Theory]
        [InlineData("#FF0000")]
        [InlineData("")]
        [InlineData(null)]
        public void IsSystemAccentToken_FalseForAnythingElse(string? value)
        {
            Assert.False(ThemeHelper.IsSystemAccentToken(value));
        }

        [Fact]
        public void ResolveColor_ExplicitHex_IgnoresAccent()
        {
            // Regardless of whatever the live system accent happens to be, a pinned hex value must
            // resolve to itself, not the accent - this is what makes "pick a specific color" mean
            // what it says instead of silently drifting if the user later changes their Windows accent.
            var resolved = ThemeHelper.ResolveColor("#123456", Colors.Black);
            Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), resolved);
        }

        [Fact]
        public void Lighten_MovesTowardWhite_AndClampsAtWhite()
        {
            var c = Color.FromRgb(100, 100, 100);
            var lighter = ThemeHelper.Lighten(c, 0.5);
            Assert.True(lighter.R > c.R);

            var fullyLightened = ThemeHelper.Lighten(c, 1.0);
            Assert.Equal((byte)255, fullyLightened.R);
        }

        [Fact]
        public void Darken_MovesTowardBlack_AndClampsAtBlack()
        {
            var c = Color.FromRgb(200, 200, 200);
            var darker = ThemeHelper.Darken(c, 0.5);
            Assert.True(darker.R < c.R);

            var fullyDarkened = ThemeHelper.Darken(c, 1.0);
            Assert.Equal((byte)0, fullyDarkened.R);
        }
    }
}
