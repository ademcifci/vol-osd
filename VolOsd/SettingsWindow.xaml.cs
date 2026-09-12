using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace VolOsd
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly Action _onSaved;
        private readonly Action _onPreview;

        private Color _darkBackground, _darkForeground, _darkBar;
        private Color _lightBackground, _lightForeground, _lightBar;
        private bool _darkBarFollowsAccent, _lightBarFollowsAccent;
        private bool _suppressAccentSync;

        private bool EditingDark => ColorVariantCombo.SelectedIndex == 0;

        public SettingsWindow(AppSettings settings, IReadOnlyList<RenderDevice> devices, Action onSaved, Action onPreview)
        {
            InitializeComponent();
            Icon = IconFactory.CreateWindowIconSource();
            ApplyWindowTheme();
            _settings = settings;
            _onSaved = onSaved;
            _onPreview = onPreview;

            PopulateKnobDevices(devices);

            PositionCombo.SelectedIndex = (int)_settings.Position;
            ThemeCombo.SelectedIndex = (int)_settings.Theme;
            ScaleSlider.Value = _settings.Scale;
            DurationSlider.Value = _settings.DisplayDurationMs / 1000.0;
            StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;

            _darkBackground = ThemeHelper.ParseColor(_settings.DarkBackgroundColorHex, Color.FromRgb(0x1E, 0x1E, 0x1E));
            _darkForeground = ThemeHelper.ParseColor(_settings.DarkForegroundColorHex, Colors.White);
            _lightBackground = ThemeHelper.ParseColor(_settings.LightBackgroundColorHex, Color.FromRgb(0xE4, 0xE4, 0xE6));
            _lightForeground = ThemeHelper.ParseColor(_settings.LightForegroundColorHex, Color.FromRgb(0x1A, 0x1A, 0x1A));

            _darkBarFollowsAccent = ThemeHelper.IsSystemAccentToken(_settings.DarkBarColorHex);
            _lightBarFollowsAccent = ThemeHelper.IsSystemAccentToken(_settings.LightBarColorHex);
            _darkBar = ThemeHelper.ResolveColor(_settings.DarkBarColorHex, Colors.White);
            _lightBar = ThemeHelper.ResolveColor(_settings.LightBarColorHex, Color.FromRgb(0x1A, 0x1A, 0x1A));

            // Default the color editor to whichever variant is currently in effect.
            bool initialDark = _settings.Theme switch
            {
                OsdTheme.Dark => true,
                OsdTheme.Light => false,
                _ => ThemeHelper.IsSystemDarkTheme()
            };
            ColorVariantCombo.SelectedIndex = initialDark ? 0 : 1;
        }

        private void PopulateKnobDevices(IReadOnlyList<RenderDevice> devices)
        {
            PopulateDeviceCombo(KnobDeviceCombo, devices, _settings.KnobDeviceId);
        }

        private static void PopulateDeviceCombo(ComboBox combo, IReadOnlyList<RenderDevice> devices, string saved)
        {
            combo.Items.Add(new ComboBoxItem { Content = "None", Tag = "" });

            foreach (var device in devices)
                combo.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Id });

            if (!string.IsNullOrEmpty(saved) && !devices.Any(d => d.Id == saved))
            {
                // Keep an unplugged selection rather than silently clearing it on save.
                combo.Items.Add(new ComboBoxItem { Content = "(device not connected)", Tag = saved });
            }

            combo.SelectedIndex = 0;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && (string)item.Tag == saved)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
        }

        private void ColorVariantCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackgroundSwatch == null) return; // fires during InitializeComponent, before elements exist
            RefreshColorSwatches();
        }

        private void RefreshColorSwatches()
        {
            bool dark = EditingDark;
            BackgroundSwatch.Background = new SolidColorBrush(dark ? _darkBackground : _lightBackground);
            ForegroundSwatch.Background = new SolidColorBrush(dark ? _darkForeground : _lightForeground);

            bool followsAccent = dark ? _darkBarFollowsAccent : _lightBarFollowsAccent;
            var bar = followsAccent ? ThemeHelper.GetSystemAccentColor() : (dark ? _darkBar : _lightBar);
            BarSwatch.Background = new SolidColorBrush(bar);

            _suppressAccentSync = true;
            BarAccentCheck.IsChecked = followsAccent;
            _suppressAccentSync = false;
            BarChooseButton.IsEnabled = !followsAccent;
        }

        private void BarAccent_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressAccentSync || BarSwatch == null) return;

            bool followsAccent = BarAccentCheck.IsChecked == true;
            if (EditingDark) _darkBarFollowsAccent = followsAccent;
            else _lightBarFollowsAccent = followsAccent;

            RefreshColorSwatches();
        }

        private static void ChooseColor(Color current, Action<Color> onPicked)
        {
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
                FullOpen = true
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var c = dialog.Color;
                onPicked(Color.FromRgb(c.R, c.G, c.B));
            }
        }

        private void ChooseBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            ChooseColor(EditingDark ? _darkBackground : _lightBackground, c =>
            {
                if (EditingDark) _darkBackground = c; else _lightBackground = c;
                RefreshColorSwatches();
            });
        }

        private void ChooseForegroundColor_Click(object sender, RoutedEventArgs e)
        {
            ChooseColor(EditingDark ? _darkForeground : _lightForeground, c =>
            {
                if (EditingDark) _darkForeground = c; else _lightForeground = c;
                RefreshColorSwatches();
            });
        }

        private void ChooseBarColor_Click(object sender, RoutedEventArgs e)
        {
            ChooseColor(EditingDark ? _darkBar : _lightBar, c =>
            {
                // Picking an explicit color stops it following the accent.
                if (EditingDark) { _darkBar = c; _darkBarFollowsAccent = false; }
                else { _lightBar = c; _lightBarFollowsAccent = false; }
                RefreshColorSwatches();
            });
        }

        private void ApplyWindowTheme()
        {
            bool dark = ThemeHelper.IsSystemDarkTheme();

            if (dark)
            {
                // Matches the Application-level defaults in App.xaml; nothing to override.
                return;
            }

            Resources["WindowBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
            Resources["WindowForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            Resources["SubtleForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
            Resources["CardBackgroundBrush"] = new SolidColorBrush(Colors.White);
            Resources["ButtonBackgroundBrush"] = new SolidColorBrush(Colors.White);
            Resources["ButtonHoverBrush"] = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xED));
            Resources["ButtonPressedBrush"] = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
            Resources["ButtonBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
            Resources["ControlBackgroundBrush"] = new SolidColorBrush(Colors.White);
            Resources["ControlBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
            Resources["ControlHoverBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
            Resources["TrackBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8));
            Resources["PopupBackgroundBrush"] = new SolidColorBrush(Colors.White);
            Resources["ItemHoverBrush"] = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            Resources["SeparatorBrush"] = new SolidColorBrush(Color.FromRgb(0xDE, 0xDE, 0xDE));
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            DarkTitleBar.Apply(hwnd, ThemeHelper.IsSystemDarkTheme());
        }

        private void ApplyToSettings()
        {
            _settings.Position = (OsdPosition)PositionCombo.SelectedIndex;
            _settings.Theme = (OsdTheme)ThemeCombo.SelectedIndex;
            _settings.Scale = ScaleSlider.Value;
            _settings.DisplayDurationMs = (int)(DurationSlider.Value * 1000);
            _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
            _settings.KnobDeviceId = (KnobDeviceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

            _settings.DarkBackgroundColorHex = ThemeHelper.ToHex(_darkBackground);
            _settings.DarkForegroundColorHex = ThemeHelper.ToHex(_darkForeground);
            _settings.DarkBarColorHex = _darkBarFollowsAccent ? ThemeHelper.SystemAccentToken : ThemeHelper.ToHex(_darkBar);
            _settings.LightBackgroundColorHex = ThemeHelper.ToHex(_lightBackground);
            _settings.LightForegroundColorHex = ThemeHelper.ToHex(_lightForeground);
            _settings.LightBarColorHex = _lightBarFollowsAccent ? ThemeHelper.SystemAccentToken : ThemeHelper.ToHex(_lightBar);
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyToSettings();
            _onPreview();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyToSettings();
            _settings.Save();
            StartupHelper.SetEnabled(_settings.StartWithWindows);
            _onSaved();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
