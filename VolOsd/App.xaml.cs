using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace VolOsd
{
    public partial class App : Application
    {
        public const string ProductName = "X3 Vol OSD";

        private const string SingleInstanceMutexName = "X3VolOsd-SingleInstance-9F2E4E1E-6B1B-4C7E-9C74-8B6E9D2B2B0B";

        private Mutex? _mutex;
        private AudioMonitor? _audioMonitor;
        private OsdWindow? _osdWindow;
        private TrayIconManager? _trayIcon;
        private SettingsWindow? _settingsWindow;
        private AppSettings _settings = new();
        private float _lastVolume;
        private bool _lastMuted;

        // The X3 driver can notify both Speakers and SPDIF for one detent; collapse those for display.
        private readonly object _osdCoalesceLock = new();
        private float _lastShownVolume = -1f;
        private bool _lastShownMuted;
        private long _lastShownTicks;
        private static readonly long OsdCoalesceTicks = TimeSpan.FromMilliseconds(250).Ticks;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (_, args) =>
            {
                Diagnostics.Log($"UNHANDLED (dispatcher): {args.Exception}");
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                Diagnostics.Log($"UNHANDLED (appdomain): {args.ExceptionObject}");
            };

            Diagnostics.Log("Startup begin");

            try
            {
                _mutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
                if (!createdNew)
                {
                    Diagnostics.Log("Another instance is already running; exiting.");
                    Shutdown();
                    return;
                }

                ApplySystemAccent();
                StartupHelper.RepairPathIfEnabled();
                _settings = AppSettings.Load();

                _audioMonitor = new AudioMonitor();
                EnsureKnobDeviceConfigured();

                _osdWindow = new OsdWindow(_settings);
                _audioMonitor.VolumeChanged += OnVolumeChanged;

                _trayIcon = new TrayIconManager(_settings);
                _trayIcon.SettingsRequested += OpenSettings;
                _trayIcon.DiagnosticsRequested += SaveDiagnosticsReport;
                _trayIcon.ExitRequested += Shutdown;

                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

                Diagnostics.Log("Startup complete");
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"Startup FAILED: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Uses the saved knob endpoint, or auto-detects the Sound Blaster X3 on first run.
        /// </summary>
        private void EnsureKnobDeviceConfigured()
        {
            if (_audioMonitor == null) return;

            var devices = _audioMonitor.GetRenderDevices();
            if (!string.IsNullOrEmpty(_settings.KnobDeviceId))
            {
                var match = devices.FirstOrDefault(d => d.Id == _settings.KnobDeviceId);
                if (!string.IsNullOrEmpty(match.Id))
                    Diagnostics.Log($"Knob device: {match.Name}");
                else
                    Diagnostics.Log("Knob device is configured but not currently connected.");
                return;
            }

            var detected = KnobDeviceHelper.TryAutoDetectKnobDevice(devices);
            if (detected == null)
            {
                Diagnostics.Log("No Sound Blaster X3 knob device found; OSD will not show until one is configured in Settings.");
                return;
            }

            _settings.KnobDeviceId = detected;
            _settings.Save();
            var detectedName = devices.First(d => d.Id == detected).Name;
            Diagnostics.Log($"Auto-detected knob device: {detectedName}");
        }

        private void ApplySystemAccent()
        {
            var accent = ThemeHelper.GetSystemAccentColor();
            Resources["AccentBrush"] = new SolidColorBrush(accent);
            Resources["AccentHoverBrush"] = new SolidColorBrush(ThemeHelper.Lighten(accent, 0.15));
            Resources["AccentPressedBrush"] = new SolidColorBrush(ThemeHelper.Darken(accent, 0.2));
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                ApplySystemAccent();
                _osdWindow?.ApplySettings();
            });
        }

        private void OnVolumeChanged(VolumeChange change)
        {
            if (_audioMonitor == null) return;

            if (!KnobDeviceHelper.IsKnobSource(change.DeviceId, _settings.KnobDeviceId, _audioMonitor.GetRenderDevices()))
                return;

            ShowOsd(change.Volume, change.Muted);
        }

        private void ShowOsd(float volume, bool muted)
        {
            lock (_osdCoalesceLock)
            {
                long now = DateTime.UtcNow.Ticks;
                bool sameValue = Math.Abs(volume - _lastShownVolume) < 0.0005f && muted == _lastShownMuted;
                if (sameValue && now - _lastShownTicks < OsdCoalesceTicks)
                    return;

                _lastShownVolume = volume;
                _lastShownMuted = muted;
                _lastShownTicks = now;
            }

            _lastVolume = volume;
            _lastMuted = muted;

            Dispatcher.BeginInvoke(() => _osdWindow?.Show(volume, muted));
        }

        private void SaveDiagnosticsReport()
        {
            var path = DiagnosticsReport.SaveWithDialog(_audioMonitor, _settings);
            if (path == null) return;

            System.Windows.Forms.MessageBox.Show(
                $"Saved to:\n{path}",
                ProductName, System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
        }

        private void OpenSettings()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow(
                _settings,
                _audioMonitor?.GetRenderDevices() ?? new List<RenderDevice>(),
                onSaved: () => _osdWindow?.ApplySettings(),
                onPreview: () =>
                {
                    _osdWindow?.ApplySettings();
                    _osdWindow?.Show(_lastVolume, _lastMuted);
                });

            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            _trayIcon?.Dispose();
            _audioMonitor?.Dispose();
            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned, e.g. second instance */ }
                _mutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}
