using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace X3VolOsd
{
    /// <summary>
    /// Bundles everything useful for a bug report into one plain-text file: app/OS/runtime versions,
    /// current audio device state, the active settings, and the debug log. Nothing here is sensitive -
    /// device names and volume levels, no personal data - so it's safe to hand to whoever's helping.
    /// </summary>
    public static class DiagnosticsReport
    {
        public static string Generate(AudioMonitor? monitor, AppSettings settings)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"{App.ProductName} diagnostics report");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine();

            sb.AppendLine("--- Version ---");
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            sb.AppendLine($"App version: {version}");
            sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            sb.AppendLine($".NET runtime: {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine();

            sb.AppendLine("--- Audio devices (current) ---");
            if (monitor == null)
            {
                sb.AppendLine("(audio monitor not available)");
            }
            else
            {
                var defaultId = monitor.DefaultDeviceId;
                foreach (var device in monitor.GetRenderDevices())
                {
                    var flag = device.Id == defaultId ? "  [DEFAULT]" : "";
                    var knobFlag = device.Id == settings.KnobDeviceId ? "  [KNOB]" : "";
                    if (monitor.TryGetVolume(device.Id, out float volume))
                        sb.AppendLine($"  {device.Name,-48} {volume * 100,6:0.0}%{flag}{knobFlag}");
                    else
                        sb.AppendLine($"  {device.Name,-48} (unreadable){flag}{knobFlag}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("--- Settings ---");
            try
            {
                sb.AppendLine(File.Exists(AppSettings.FilePath)
                    ? File.ReadAllText(AppSettings.FilePath)
                    : "(no settings file - running on defaults)");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(could not read settings file: {ex.Message})");
            }
            sb.AppendLine();

            sb.AppendLine("--- Log ---");
            try
            {
                sb.AppendLine(File.Exists(Diagnostics.LogPath)
                    ? File.ReadAllText(Diagnostics.LogPath)
                    : "(no log file yet)");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(could not read log file: {ex.Message})");
            }

            return sb.ToString();
        }

        /// <summary>Writes the report to a user-chosen location via a save dialog. Returns the path
        /// saved to, or null if the user cancelled or the save failed.</summary>
        public static string? SaveWithDialog(AudioMonitor? monitor, AppSettings settings)
        {
            var report = Generate(monitor, settings);

            using var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Title = "Save Diagnostics Report",
                Filter = "Text file (*.txt)|*.txt",
                FileName = $"X3VolOsd-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return null;

            try
            {
                File.WriteAllText(dialog.FileName, report);
                return dialog.FileName;
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"Failed to save diagnostics report: {ex.Message}");
                System.Windows.Forms.MessageBox.Show(
                    $"Couldn't save the report:\n{ex.Message}",
                    App.ProductName, System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return null;
            }
        }
    }
}
