using System;
using Forms = System.Windows.Forms;

namespace iikoServiceHelper.Services
{
    public class TrayIconService : IDisposable
    {
        private readonly Forms.NotifyIcon _trayIcon;
        private readonly Forms.ToolStripMenuItem _pauseMenuItem;
        private readonly Forms.ToolStripMenuItem _hooksMenuItem;

        public TrayIconService(Action showWindowAction, Action togglePauseAction, Action toggleHooksAction, Action exitAction)
        {
            System.Drawing.Icon trayIcon;
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("iikoServiceHelper.Logo_trey.ico");

                if (stream != null)
                    trayIcon = new System.Drawing.Icon(stream);
                else
                    trayIcon = System.Drawing.SystemIcons.Application;
            }
            catch
            {
                trayIcon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = trayIcon,
                Visible = true,
                Text = "iikoServiceHelper_v2"
            };

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("Развернуть", null, (s, e) => showWindowAction());

            _pauseMenuItem = new Forms.ToolStripMenuItem("Приостановить", null, (s, e) => togglePauseAction());
            contextMenu.Items.Add(_pauseMenuItem);

            _hooksMenuItem = new Forms.ToolStripMenuItem("Отключить перехват", null, (s, e) => toggleHooksAction());
            contextMenu.Items.Add(_hooksMenuItem);

            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Выход", null, (s, e) => exitAction());
            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => showWindowAction();
        }

        public void UpdateState(bool isPaused, bool hooksDisabled)
        {
            if (hooksDisabled)
            {
                _hooksMenuItem.Text = "Включить перехват";
                _trayIcon.Text = "iikoServiceHelper_v2 (Hooks Disabled)";
            }
            else if (isPaused)
            {
                _pauseMenuItem.Text = "Возобновить";
                _trayIcon.Text = "iikoServiceHelper_v2 (Paused)";
            }
            else
            {
                _pauseMenuItem.Text = "Приостановить";
                _hooksMenuItem.Text = "Отключить перехват";
                _trayIcon.Text = "iikoServiceHelper_v2";
            }
        }

        public void ShowBalloonTip(int timeout, string title, string text, Forms.ToolTipIcon icon)
        {
            _trayIcon?.ShowBalloonTip(timeout, title, text, icon);
        }

        public void Dispose()
        {
            _trayIcon?.Dispose();
        }
    }
}