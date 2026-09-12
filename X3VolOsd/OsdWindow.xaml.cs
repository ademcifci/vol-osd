using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace X3VolOsd
{
    public partial class OsdWindow : Window
    {
        // \u escapes, not the raw glyph characters: a bare Private-Use-Area character has no visible
        // representation in a source file, so a full rewrite of this file could silently drop it with
        // nothing to show it happened - confirmed elsewhere in this codebase (IconFactory.cs) when
        // exactly that occurred during an unrelated edit.
        private const string MuteGlyph = "\uE74F";
        private const string Volume0Glyph = "\uE992";
        private const string Volume1Glyph = "\uE993";
        private const string Volume2Glyph = "\uE994";
        private const string Volume3Glyph = "\uE995";

        private readonly DispatcherTimer _hideTimer = new();
        private readonly AppSettings _settings;
        private const double BarWidth = 200;
        private bool _fadingOut;
        private bool _barFollowsAccent;
        private Color _appliedBarColor;

        public OsdWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); FadeOut(); };
            ApplyTheme();
            ApplyScale();
        }

        public void ApplySettings()
        {
            ApplyTheme();
            ApplyScale();
        }

        private void ApplyScale()
        {
            var scale = Math.Clamp(_settings.Scale, 0.5, 2.0);
            RootBorder.LayoutTransform = new ScaleTransform(scale, scale);
        }

        private void ApplyTheme()
        {
            bool dark = _settings.Theme switch
            {
                OsdTheme.Dark => true,
                OsdTheme.Light => false,
                _ => ThemeHelper.IsSystemDarkTheme()
            };

            var defaultBg = dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Color.FromRgb(0xE4, 0xE4, 0xE6);
            var defaultFg = dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A);

            var bg = ThemeHelper.ParseColor(dark ? _settings.DarkBackgroundColorHex : _settings.LightBackgroundColorHex, defaultBg);
            var fg = ThemeHelper.ParseColor(dark ? _settings.DarkForegroundColorHex : _settings.LightForegroundColorHex, defaultFg);

            var barSetting = dark ? _settings.DarkBarColorHex : _settings.LightBarColorHex;
            _barFollowsAccent = ThemeHelper.IsSystemAccentToken(barSetting);
            var bar = ThemeHelper.ResolveColor(barSetting, ThemeHelper.GetSystemAccentColor());
            _appliedBarColor = bar;

            // A near-white fill alone reads as washed-out against light desktops/wallpapers;
            // a firmer background plus a visible border and shadow keep it legible in light mode.
            byte bgAlpha = dark ? (byte)0xEC : (byte)0xF5;
            byte borderAlpha = dark ? (byte)0x40 : (byte)0x50;
            byte trackAlpha = dark ? (byte)0x33 : (byte)0x28;

            RootBorder.Background = new SolidColorBrush(Color.FromArgb(bgAlpha, bg.R, bg.G, bg.B));
            RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(borderAlpha, fg.R, fg.G, fg.B));
            TrackBar.Background = new SolidColorBrush(Color.FromArgb(trackAlpha, fg.R, fg.G, fg.B));

            var fgBrush = new SolidColorBrush(fg);
            IconText.Foreground = fgBrush;
            PercentText.Foreground = fgBrush;
            FillBar.Background = new SolidColorBrush(bar);
        }

        public void Show(float volume, bool muted)
        {
            var percent = Math.Clamp(volume, 0f, 1f);

            IconText.Text = muted || percent <= 0.001
                ? (muted ? MuteGlyph : Volume0Glyph)
                : percent < 0.34 ? Volume1Glyph
                : percent < 0.67 ? Volume2Glyph
                : Volume3Glyph;

            FillBar.Width = muted ? 0 : BarWidth * percent;
            PercentText.Text = muted ? "Muted" : $"{Math.Round(percent * 100):0}%";

            RefreshAccentIfFollowing();
            _fadingOut = false;

            if (!IsVisible)
            {
                // Show before positioning: until the window is realised its ActualWidth/Height are 0,
                // which would place the first OSD half a screen off. It is still fully transparent
                // here (opacity 0 until FadeIn), so repositioning now is not visible.
                base.Show();
                PositionWindow();
                FadeIn();
            }
            else
            {
                PositionWindow();
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
            }

            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(300, _settings.DisplayDurationMs));
            _hideTimer.Start();
        }

        /// <summary>
        /// Re-reads the live accent right before showing, so the bar tracks an accent change even if
        /// the system notification for it was missed. Only allocates when the color actually changed.
        /// </summary>
        private void RefreshAccentIfFollowing()
        {
            if (!_barFollowsAccent) return;

            var accent = ThemeHelper.GetSystemAccentColor();
            if (accent == _appliedBarColor) return;

            _appliedBarColor = accent;
            FillBar.Background = new SolidColorBrush(accent);
        }

        private void PositionWindow()
        {
            UpdateLayout();

            double width = ActualWidth;
            double height = ActualHeight;
            var area = SystemParameters.WorkArea;
            const double margin = 24;

            double left, top;
            switch (_settings.Position)
            {
                case OsdPosition.TopRight:
                    left = area.Right - width - margin;
                    top = area.Top + margin;
                    break;
                case OsdPosition.BottomCenter:
                    left = area.Left + (area.Width - width) / 2;
                    top = area.Bottom - height - margin;
                    break;
                case OsdPosition.BottomRight:
                    left = area.Right - width - margin;
                    top = area.Bottom - height - margin;
                    break;
                default: // TopCenter
                    left = area.Left + (area.Width - width) / 2;
                    top = area.Top + margin;
                    break;
            }

            Left = left;
            Top = top;
        }

        private void FadeIn()
        {
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            BeginAnimation(OpacityProperty, anim);
        }

        private void FadeOut()
        {
            _fadingOut = true;
            var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            // A volume change during the fade re-shows the window and clears the flag; without this
            // guard the in-flight animation's Completed could then hide it again mid-use.
            anim.Completed += (_, _) => { if (_fadingOut) Hide(); };
            BeginAnimation(OpacityProperty, anim);
        }
    }
}
