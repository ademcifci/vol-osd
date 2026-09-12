using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace VolOsd
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = "VolOsd-SingleInstance-9F2E4E1E-6B1B-4C7E-9C74-8B6E9D2B2B0B";

        private Mutex? _mutex;
        private AudioMonitor? _audioMonitor;
        private OsdWindow? _osdWindow;
        private TrayIconManager? _trayIcon;
        private SettingsWindow? _settingsWindow;
        private AppSettings _settings = new();
        private KnobRelay? _knobRelay;
        private float _lastVolume;
        private bool _lastMuted;

        // The monitor reports every device, so one physical change arrives several times (a card's
        // endpoints move together, and an enhancer stacked on top mirrors them). Collapse those for
        // display, or the OSD redraws repeatedly and flickers between the values each reports.
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

                _osdWindow = new OsdWindow(_settings);

                _audioMonitor = new AudioMonitor();
                _audioMonitor.VolumeChanged += OnVolumeChanged;

                _knobRelay = new KnobRelay(_audioMonitor, _settings);
                _knobRelay.DefaultVolumeRelayed += (volume, muted) => ShowOsd(volume, muted);
                _knobRelay.DisabledForSafety += OnKnobRelayDisabledForSafety;

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

        private void ApplySystemAccent()
        {
            // Reflects the user's actual Windows accent (Settings > Personalization > Colors) in our
            // own UI - the Settings window's Save button, sliders and toggle - instead of a hardcoded blue.
            var accent = ThemeHelper.GetSystemAccentColor();
            Resources["AccentBrush"] = new SolidColorBrush(accent);
            Resources["AccentHoverBrush"] = new SolidColorBrush(ThemeHelper.Lighten(accent, 0.15));
            Resources["AccentPressedBrush"] = new SolidColorBrush(ThemeHelper.Darken(accent, 0.2));
        }

        /// <summary>
        /// Windows raises this when the user switches light/dark mode or changes their accent color,
        /// so "Match Windows" keeps matching Windows without needing a restart.
        /// </summary>
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
            // When the knob relay is driving, it decides what the OSD shows - the knob card's own
            // levels are meaningless in that case.
            if (_knobRelay?.ShouldSuppressOsd(change.DeviceId) == true)
                return;

            ShowOsd(change.Volume, change.Muted);
        }

        /// <summary>
        /// Single entry point for displaying the OSD, so the relay and ordinary notifications share
        /// one coalescing window - a relayed change also comes back as a real notification from the
        /// default device moments later, and without this both would redraw.
        /// </summary>
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

            // Fire-and-forget: this runs on an audio COM callback thread, which must not be blocked
            // waiting on the UI thread. BeginInvoke is also safe if the dispatcher is already shutting
            // down, where Invoke would throw.
            Dispatcher.BeginInvoke(() => _osdWindow?.Show(volume, muted));
        }

        /// <summary>
        /// The knob relay's rate limiter tripped, which means it saw volume moving faster than any
        /// real knob turn could produce and disabled itself for the rest of this session. This is a
        /// safety backstop, not routine behaviour - the user needs to know it happened.
        /// </summary>
        private void OnKnobRelayDisabledForSafety()
        {
            Dispatcher.BeginInvoke(() => _trayIcon?.ShowNotification(
                "Vol OSD",
                "The hardware knob feature detected unusually fast volume changes and turned itself off for this session. Your volume has not been changed further. Restart the app to try again, or check the diagnostics report if it keeps happening."));
        }

        private void SaveDiagnosticsReport()
        {
            var path = DiagnosticsReport.SaveWithDialog(_audioMonitor, _settings);
            if (path == null) return;

            System.Windows.Forms.MessageBox.Show(
                $"Saved to:\n{path}",
                "Vol OSD", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
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
            // SystemEvents holds a static reference to its handlers; leaving this attached would keep
            // the App object alive past shutdown.
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            _knobRelay?.Dispose();
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
