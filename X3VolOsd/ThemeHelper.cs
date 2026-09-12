using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace X3VolOsd
{
    public static class ThemeHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetColorizationColor(out uint colorizationColor, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);

        public static bool IsSystemDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int i)
                    return i == 0;
            }
            catch
            {
                // Fall through to default.
            }
            return true;
        }

        /// <summary>The user's current Windows accent color (Settings > Personalization > Colors),
        /// via the documented DWM colorization API. Falls back to Windows' own default blue.</summary>
        public static Color GetSystemAccentColor()
        {
            try
            {
                if (DwmGetColorizationColor(out uint argb, out _) == 0) // S_OK
                {
                    byte r = (byte)(argb >> 16);
                    byte g = (byte)(argb >> 8);
                    byte b = (byte)argb;
                    return Color.FromRgb(r, g, b);
                }
            }
            catch
            {
                // Fall through to default.
            }
            return Color.FromRgb(0x00, 0x78, 0xD4);
        }

        public static Color Lighten(Color c, double amount)
        {
            byte L(byte channel) => (byte)(channel + (255 - channel) * amount);
            return Color.FromRgb(L(c.R), L(c.G), L(c.B));
        }

        public static Color Darken(Color c, double amount)
        {
            byte D(byte channel) => (byte)(channel * (1 - amount));
            return Color.FromRgb(D(c.R), D(c.G), D(c.B));
        }

        /// <summary>Stored in place of a hex value to mean "follow the Windows accent color,
        /// whatever it currently is" rather than pinning the color it happened to be.</summary>
        public const string SystemAccentToken = "accent";

        public static bool IsSystemAccentToken(string? value) =>
            string.Equals(value?.Trim(), SystemAccentToken, StringComparison.OrdinalIgnoreCase);

        /// <summary>Parses a stored color setting, resolving the accent token to the live accent.</summary>
        public static Color ResolveColor(string? value, Color fallback) =>
            IsSystemAccentToken(value) ? GetSystemAccentColor() : ParseColor(value ?? string.Empty, fallback);

        public static Color ParseColor(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return fallback; }
        }

        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
