using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VolOsd
{
    public enum OsdPosition
    {
        TopCenter,
        TopRight,
        BottomCenter,
        BottomRight
    }

    public enum OsdTheme
    {
        Auto,
        Dark,
        Light
    }

    public class AppSettings
    {
        public OsdPosition Position { get; set; } = OsdPosition.TopCenter;
        public double Scale { get; set; } = 1.0;
        public int DisplayDurationMs { get; set; } = 1500;
        public OsdTheme Theme { get; set; } = OsdTheme.Auto;
        public bool StartWithWindows { get; set; } = false;

        public string DarkBackgroundColorHex { get; set; } = "#1E1E1E";
        public string DarkForegroundColorHex { get; set; } = "#FFFFFF";

        // "accent" means follow the live Windows accent color; a hex value pins a specific color.
        public string DarkBarColorHex { get; set; } = ThemeHelper.SystemAccentToken;

        /// <summary>Device id of a hardware volume knob whose movement should be applied to the
        /// current default device instead. Empty = feature off.</summary>
        public string KnobDeviceId { get; set; } = "";

        /// <summary>Optional device id of a virtual/enhancer device (e.g. an FxSound output) that is
        /// known to actually play through the knob's own card under the hood. Windows has no API that
        /// exposes that relationship - the enhancer just presents as an unrelated default device - so
        /// there is nothing to detect it automatically; this lets the user state it once instead of
        /// manually toggling the knob feature on and off every time they switch to and from that
        /// enhancer. When the current default matches this id, the relay treats it exactly like the
        /// knob's card being the literal default: it stands down (the knob already reaches your ears
        /// natively) and the OSD shows the knob's own real reading instead of suppressing it. Empty =
        /// no such device declared.</summary>
        public string KnobPassthroughDeviceId { get; set; } = "";

        public string LightBackgroundColorHex { get; set; } = "#E4E4E6";
        public string LightForegroundColorHex { get; set; } = "#1A1A1A";
        public string LightBarColorHex { get; set; } = ThemeHelper.SystemAccentToken;

        /// <summary>Public so tests can round-trip through the exact same contract Save/Load use,
        /// without going through Save/Load themselves - those touch the real per-user settings file
        /// on disk, which a test must never do.</summary>
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VolOsd", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (settings != null)
                        return settings;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"Settings.Load failed, using defaults: {ex.Message}");
            }

            return new AppSettings();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
    }
}
