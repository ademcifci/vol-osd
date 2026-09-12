using System.Text.Json;
using VolOsd;

namespace VolOsd.Tests
{
    /// <summary>
    /// Deliberately does not call AppSettings.Save()/Load() anywhere in this file: those read and
    /// write the real per-user settings file at %AppData%\VolOsd\settings.json, and a test must never
    /// touch a real user's actual configuration as a side effect of running. Everything here goes
    /// through AppSettings.JsonOptions directly, which is the exact same serialization contract
    /// Save/Load use internally - what's actually being protected against regressing.
    /// </summary>
    public class SettingsTests
    {
        [Fact]
        public void RoundTrip_PreservesAllFields()
        {
            var original = new AppSettings
            {
                Position = OsdPosition.BottomRight,
                Scale = 1.4,
                DisplayDurationMs = 2200,
                Theme = OsdTheme.Light,
                StartWithWindows = true,
                DarkBackgroundColorHex = "#111111",
                DarkForegroundColorHex = "#EEEEEE",
                DarkBarColorHex = "accent",
                KnobDeviceId = "{some-device-id}",
                KnobPassthroughDeviceId = "{some-other-device-id}",
                LightBackgroundColorHex = "#F0F0F0",
                LightForegroundColorHex = "#101010",
                LightBarColorHex = "#00FF00"
            };

            var json = JsonSerializer.Serialize(original, AppSettings.JsonOptions);
            var restored = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions);

            Assert.NotNull(restored);
            Assert.Equal(original.Position, restored!.Position);
            Assert.Equal(original.Scale, restored.Scale);
            Assert.Equal(original.DisplayDurationMs, restored.DisplayDurationMs);
            Assert.Equal(original.Theme, restored.Theme);
            Assert.Equal(original.StartWithWindows, restored.StartWithWindows);
            Assert.Equal(original.DarkBackgroundColorHex, restored.DarkBackgroundColorHex);
            Assert.Equal(original.DarkBarColorHex, restored.DarkBarColorHex);
            Assert.Equal(original.KnobDeviceId, restored.KnobDeviceId);
            Assert.Equal(original.KnobPassthroughDeviceId, restored.KnobPassthroughDeviceId);
            Assert.Equal(original.LightBarColorHex, restored.LightBarColorHex);
        }

        // The actual historical bug: enums serialize as plain integers by default in System.Text.Json.
        // A hand-edited (or older-version-written) settings file using a readable enum name like
        // "Light" failed to parse at all, and the whole file silently fell back to defaults - not just
        // that one field. This pins the human-readable serialization explicitly, so it fails loudly
        // if the JsonStringEnumConverter is ever dropped.
        [Fact]
        public void Enums_SerializeAsReadableStrings_NotIntegers()
        {
            var settings = new AppSettings { Theme = OsdTheme.Light, Position = OsdPosition.TopRight };
            var json = JsonSerializer.Serialize(settings, AppSettings.JsonOptions);

            Assert.Contains("\"Theme\": \"Light\"", json);
            Assert.Contains("\"Position\": \"TopRight\"", json);
        }

        [Fact]
        public void HandWrittenEnumName_DeserializesCorrectly()
        {
            // Simulates a user or an older version hand-editing the file with the readable name.
            var json = "{ \"Theme\": \"Dark\", \"Position\": \"BottomCenter\" }";
            var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions);

            Assert.NotNull(settings);
            Assert.Equal(OsdTheme.Dark, settings!.Theme);
            Assert.Equal(OsdPosition.BottomCenter, settings.Position);
        }

        [Fact]
        public void DefaultConstructor_ProducesSensibleDefaults()
        {
            var settings = new AppSettings();

            Assert.Equal(OsdPosition.TopCenter, settings.Position);
            Assert.Equal(OsdTheme.Auto, settings.Theme);
            Assert.False(settings.StartWithWindows);
            Assert.Equal("", settings.KnobDeviceId);
            Assert.Equal("", settings.KnobPassthroughDeviceId);
            Assert.True(ThemeHelper.IsSystemAccentToken(settings.DarkBarColorHex));
            Assert.True(ThemeHelper.IsSystemAccentToken(settings.LightBarColorHex));
        }
    }
}
