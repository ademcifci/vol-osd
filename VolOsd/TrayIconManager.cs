using System;
using System.Windows.Forms;

namespace VolOsd
{
    public sealed class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly AppSettings _settings;
        private readonly ToolStripMenuItem _startWithWindowsItem;

        public event Action? SettingsRequested;
        public event Action? DiagnosticsRequested;
        public event Action? ExitRequested;

        public TrayIconManager(AppSettings settings)
        {
            _settings = settings;

            _startWithWindowsItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = _settings.StartWithWindows };
            _startWithWindowsItem.Click += (_, _) =>
            {
                _settings.StartWithWindows = _startWithWindowsItem.Checked;
                _settings.Save();
                StartupHelper.SetEnabled(_settings.StartWithWindows);
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Settings...", null, (_, _) => SettingsRequested?.Invoke());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_startWithWindowsItem);
            menu.Items.Add("Save Diagnostics Report...", null, (_, _) => DiagnosticsRequested?.Invoke());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
            menu.Opening += (_, _) => _startWithWindowsItem.Checked = _settings.StartWithWindows;

            _notifyIcon = new NotifyIcon
            {
                Icon = IconFactory.CreateTrayIcon(),
                Text = "Vol OSD",
                Visible = true,
                ContextMenuStrip = menu
            };

            _notifyIcon.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    SettingsRequested?.Invoke();
            };
        }

        public void ShowNotification(string title, string text, ToolTipIcon icon = ToolTipIcon.Warning)
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(10000);
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
