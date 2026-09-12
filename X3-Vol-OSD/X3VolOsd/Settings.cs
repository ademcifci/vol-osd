using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace X3VolOsd
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

        /// <summary>Playback endpoint whose volume changes come from the X3 hardware knob. The OSD
        /// only appears when this card's volume moves. Empty = try auto-detecting the X3 at startup.</summary>
        public string KnobDeviceId { get; set; } = "";

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

        public static string FilePath => AppPaths.SettingsFile;

        public static AppSettings Load()
        {
            AppPaths.MigrateLegacyDataIfNeeded();

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
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
    }
}
