using System;
using System.IO;

namespace X3VolOsd
{
    /// <summary>Per-user data locations. Migrates once from the old VolOsd app folder if present.</summary>
    internal static class AppPaths
    {
        private const string LegacyFolderName = "VolOsd";
        public const string FolderName = "X3VolOsd";

        public static string DataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

        private static string LegacyDataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyFolderName);

        public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
        public static string LogFile => Path.Combine(DataDirectory, "debug.log");

        private static bool _migrationAttempted;

        public static void MigrateLegacyDataIfNeeded()
        {
            if (_migrationAttempted) return;
            _migrationAttempted = true;

            try
            {
                if (File.Exists(SettingsFile)) return;
                if (!Directory.Exists(LegacyDataDirectory)) return;

                Directory.CreateDirectory(DataDirectory);

                foreach (var file in Directory.GetFiles(LegacyDataDirectory))
                {
                    var dest = Path.Combine(DataDirectory, Path.GetFileName(file));
                    if (!File.Exists(dest))
                        File.Copy(file, dest);
                }

            }
            catch
            {
                // Migration is best-effort; Load() falls back to defaults if settings are still missing.
            }
        }
    }
}
