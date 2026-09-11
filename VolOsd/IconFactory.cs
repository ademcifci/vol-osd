using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Media.Imaging;

namespace VolOsd
{
    /// <summary>Draws the app icon in-process so the app ships without an external .ico asset.</summary>
    public static class IconFactory
    {
        private const int DesignSize = 32;

        public static Icon CreateTrayIcon() => CreateIconFromPng(DrawBitmap(DesignSize));

        public static BitmapSource CreateWindowIconSource()
        {
            using var bitmap = DrawBitmap(DesignSize);
            using var pngStream = new MemoryStream();
            bitmap.Save(pngStream, ImageFormat.Png);
            pngStream.Seek(0, SeekOrigin.Begin);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = pngStream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        /// <summary>
        /// Writes a proper multi-resolution .ico so the compiled exe itself - not just the tray icon
        /// drawn at runtime - shows the app icon in Explorer, the taskbar, Alt-Tab, and shortcuts. Not
        /// called at runtime; this is how app.ico (referenced by VolOsd.csproj's ApplicationIcon and
        /// the installer's SetupIconFile) was generated. Re-run it if the icon design ever changes.
        ///
        /// PNG-compressed frames are only reliably decoded by System.Drawing/shell icon loaders at
        /// 256x256, where the format effectively requires them; below that, several common code paths
        /// (System.Drawing.Icon's loader among them) misread the PNG bytes as raw pixel data and
        /// render noise instead of the image. Smaller sizes use classic uncompressed 32bpp BMP+AND-mask
        /// data, which every consumer understands.
        /// </summary>
        public static void SaveMultiResolutionIco(string path, int[] sizes)
        {
            var images = new (int Size, byte[] Data)[sizes.Length];
            for (int i = 0; i < sizes.Length; i++)
            {
                using var bitmap = DrawBitmap(sizes[i]);
                images[i] = (sizes[i], sizes[i] >= 256 ? EncodePng(bitmap) : EncodeBmpDib(bitmap));
            }

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fs);

            writer.Write((short)0);              // reserved
            writer.Write((short)1);              // type: icon
            writer.Write((short)images.Length);  // image count

            int offset = 6 + 16 * images.Length; // header + one directory entry per image
            foreach (var (size, data) in images)
            {
                writer.Write((byte)(size >= 256 ? 0 : size));  // 0 means 256 per the ICO format
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);   // color palette
                writer.Write((byte)0);   // reserved
                writer.Write((short)1);  // color planes
                writer.Write((short)32); // bits per pixel
                writer.Write(data.Length);
                writer.Write(offset);
                offset += data.Length;
            }

            foreach (var (_, data) in images)
                writer.Write(data);
        }

        private static byte[] EncodePng(Bitmap bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }

        /// <summary>Uncompressed 32bpp BITMAPINFOHEADER + XOR (BGRA, bottom-up) + AND mask, the classic
        /// small-icon encoding every Windows icon consumer supports. The AND mask is left all-zero
        /// (nothing legacy-masked) since the real transparency comes from the 32bpp alpha channel.</summary>
        private static byte[] EncodeBmpDib(Bitmap bitmap)
        {
            int w = bitmap.Width, h = bitmap.Height;
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write(40);            // biSize (BITMAPINFOHEADER)
            bw.Write(w);             // biWidth
            bw.Write(h * 2);         // biHeight - XOR and AND masks stacked, per the ICO format
            bw.Write((short)1);      // biPlanes
            bw.Write((short)32);     // biBitCount
            bw.Write(0);             // biCompression = BI_RGB
            bw.Write(w * h * 4);     // biSizeImage
            bw.Write(0); bw.Write(0);
            bw.Write(0); bw.Write(0);

            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = 0; x < w; x++)
                {
                    var c = bitmap.GetPixel(x, y);
                    bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                }
            }

            int maskRowBytes = ((w + 31) / 32) * 4;
            var zeroRow = new byte[maskRowBytes];
            for (int y = 0; y < h; y++)
                bw.Write(zeroRow);

            return ms.ToArray();
        }

        private static Bitmap DrawBitmap(int size)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(Color.FromArgb(255, 32, 120, 220));
            g.FillEllipse(bg, 0, 0, size, size);

            // Scaled from the original 15px-at-32px design ratio.
            float fontSize = Math.Max(size * (15f / DesignSize), 6f);
            using var font = new Font("Segoe MDL2 Assets", fontSize, GraphicsUnit.Pixel);
            using var fg = new SolidBrush(Color.White);
            // \u escape, not the raw glyph character: a bare Private-Use-Area character has no
            // visible representation, so it can (and did) get silently dropped when this file was
            // rewritten - a full round of edits produced an empty string with no visible sign of it.
            // Confirmed to happen more than once during development. This escape form is verified
            // (by raw byte inspection) to survive edits that the raw character does not.
            var glyph = "\uE995"; // Volume3
            var textSize = g.MeasureString(glyph, font);
            var x = (size - textSize.Width) / 2f;
            var y = (size - textSize.Height) / 2f;
            g.DrawString(glyph, font, fg, x, y);

            return bitmap;
        }

        private static Icon CreateIconFromPng(Bitmap bitmap)
        {
            using (bitmap)
            {
                using var pngStream = new MemoryStream();
                bitmap.Save(pngStream, ImageFormat.Png);
                var pngBytes = pngStream.ToArray();

                using var icoStream = new MemoryStream();
                using var writer = new BinaryWriter(icoStream);

                writer.Write((short)0);   // reserved
                writer.Write((short)1);   // type: icon
                writer.Write((short)1);   // image count

                writer.Write((byte)bitmap.Width);
                writer.Write((byte)bitmap.Height);
                writer.Write((byte)0);    // color palette
                writer.Write((byte)0);    // reserved
                writer.Write((short)1);   // color planes
                writer.Write((short)32);  // bits per pixel
                writer.Write(pngBytes.Length);
                writer.Write(6 + 16);     // offset: 6-byte header + 16-byte dir entry

                writer.Write(pngBytes);
                writer.Flush();
                icoStream.Seek(0, SeekOrigin.Begin);
                return new Icon(icoStream);
            }
        }
    }
}
