using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using X3VolOsd;

namespace X3VolOsd.Tests
{
    /// <summary>
    /// Regression tests for two real, hard-to-notice bugs in the generated app icon: a
    /// Private-Use-Area glyph character that got silently dropped to an empty string by a file
    /// rewrite (produced a blank blue circle, invisible in a code review since the character has no
    /// visible glyph in a text editor either), and small ICO frames decoding as noise because they
    /// were PNG-compressed (System.Drawing's loader only reliably reads PNG frames at 256x256).
    /// Both bugs were only caught by looking at actual rendered pixels, which is what these do.
    /// </summary>
    public class IconFactoryTests
    {
        private static bool IsWhiteish(Color c) =>
            c.A > 200 && c.R > 200 && c.G > 200 && c.B > 200;

        private static bool IsBackgroundBlue(Color c) =>
            c.A > 200 && Math.Abs(c.R - 32) < 40 && Math.Abs(c.G - 120) < 40 && Math.Abs(c.B - 220) < 40;

        private static int CountPixels(Bitmap bmp, Func<Color, bool> predicate)
        {
            int count = 0;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if (predicate(bmp.GetPixel(x, y)))
                        count++;
            return count;
        }

        // Direct regression test for the silently-empty-glyph bug: a blank icon is a blue circle
        // with zero white pixels anywhere. This would have caught it immediately, without needing a
        // human to notice a subtly wrong-looking tray icon.
        [Fact]
        public void CreateTrayIcon_RendersVisibleGlyph_NotJustBackground()
        {
            using var icon = IconFactory.CreateTrayIcon();
            using var bitmap = icon.ToBitmap();

            Assert.True(CountPixels(bitmap, IsWhiteish) > 0,
                "no white-ish pixels found - the glyph did not render (this is exactly how the empty-glyph bug looked)");
            Assert.True(CountPixels(bitmap, IsBackgroundBlue) > 0,
                "no background-colored pixels found - the background circle did not render");
        }

        [Fact]
        public void CreateWindowIconSource_ProducesNonNullFrozenImage()
        {
            var source = IconFactory.CreateWindowIconSource();
            Assert.NotNull(source);
            Assert.True(source.IsFrozen);
            Assert.True(source.PixelWidth > 0);
            Assert.True(source.PixelHeight > 0);
        }

        [Fact]
        public void SaveMultiResolutionIco_WritesCorrectImageCountAndDeclaredSizes()
        {
            var sizes = new[] { 16, 24, 32, 48, 256 };
            var path = Path.Combine(Path.GetTempPath(), $"volosd-icotest-{Guid.NewGuid():N}.ico");
            try
            {
                IconFactory.SaveMultiResolutionIco(path, sizes);
                var bytes = File.ReadAllBytes(path);

                short imageCount = BitConverter.ToInt16(bytes, 4);
                Assert.Equal(sizes.Length, imageCount);

                for (int i = 0; i < sizes.Length; i++)
                {
                    int entryOffset = 6 + 16 * i;
                    byte declaredWidth = bytes[entryOffset];
                    byte declaredHeight = bytes[entryOffset + 1];
                    byte expected = (byte)(sizes[i] >= 256 ? 0 : sizes[i]); // 0 means 256 per the ICO format
                    Assert.Equal(expected, declaredWidth);
                    Assert.Equal(expected, declaredHeight);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        // Regression test for the PNG-in-small-ICO-frame noise bug: every non-256 frame must be the
        // uncompressed BMP+AND-mask encoding, never PNG. A PNG frame starts with the fixed 8-byte
        // signature \x89PNG\r\n\x1a\n; this checks the raw bytes of each small frame don't start
        // with it, rather than relying on a loader to (mis)decode it.
        [Fact]
        public void SaveMultiResolutionIco_SmallFrames_AreNotPngEncoded()
        {
            byte[] pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            var sizes = new[] { 16, 24, 32, 48, 256 };
            var path = Path.Combine(Path.GetTempPath(), $"volosd-icotest-{Guid.NewGuid():N}.ico");
            try
            {
                IconFactory.SaveMultiResolutionIco(path, sizes);
                var bytes = File.ReadAllBytes(path);

                for (int i = 0; i < sizes.Length; i++)
                {
                    int entryOffset = 6 + 16 * i;
                    int dataSize = BitConverter.ToInt32(bytes, entryOffset + 8);
                    int dataOffset = BitConverter.ToInt32(bytes, entryOffset + 12);
                    var frame = bytes.AsSpan(dataOffset, Math.Min(8, dataSize));

                    bool isPng = frame.SequenceEqual(pngSignature);
                    if (sizes[i] >= 256)
                        Assert.True(isPng, "the 256px frame must be PNG-encoded");
                    else
                        Assert.False(isPng, $"the {sizes[i]}px frame must not be PNG-encoded - small PNG frames decode as noise in common icon loaders");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void SaveMultiResolutionIco_256Frame_DecodesWithVisibleGlyph()
        {
            var sizes = new[] { 16, 32, 256 };
            var path = Path.Combine(Path.GetTempPath(), $"volosd-icotest-{Guid.NewGuid():N}.ico");
            try
            {
                IconFactory.SaveMultiResolutionIco(path, sizes);

                using var icon = new Icon(path, new Size(256, 256));
                using var bitmap = icon.ToBitmap();

                Assert.True(CountPixels(bitmap, IsWhiteish) > 0,
                    "no white-ish pixels found in the 256px frame - the glyph did not render");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
