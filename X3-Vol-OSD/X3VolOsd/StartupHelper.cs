using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace X3VolOsd
{
    public static class StartupHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "X3VolOsd";
        private const string LegacyValueName = "VolOsd";

        public static void SetEnabled(bool enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;

            if (enabled)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(ValueName, $"\"{exePath}\"");
                    key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
                }
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            }
        }

        public static bool IsEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) != null;
        }

        /// <summary>
        /// Repoints an existing autostart entry at wherever the app is now. The portable build gets
        /// moved around, and the entry records an absolute path - without this it would keep pointing
        /// at the old location and silently stop launching at login.
        /// </summary>
        public static void RepairPathIfEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key == null) return;

                MigrateLegacyAutostartEntry(key);

                if (key.GetValue(ValueName) is not string existing) return;

                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                var expected = $"\"{exePath}\"";
                if (string.Equals(existing, expected, StringComparison.OrdinalIgnoreCase)) return;

                key.SetValue(ValueName, expected);
                Diagnostics.Log($"Autostart path updated: {existing} -> {expected}");
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"Autostart path repair failed: {ex.Message}");
            }
        }

        private static void MigrateLegacyAutostartEntry(RegistryKey key)
        {
            if (key.GetValue(ValueName) != null) return;
            if (key.GetValue(LegacyValueName) is not string legacy) return;

            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;

            key.SetValue(ValueName, $"\"{exePath}\"");
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            Diagnostics.Log($"Migrated autostart entry from {LegacyValueName} to {ValueName} (was: {legacy})");
        }
    }
}
