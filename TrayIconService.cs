using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace iikoServiceHelper.Services
{
    public class TrayIconService : IDisposable
    {
        private readonly Forms.NotifyIcon _trayIcon;
        private readonly Forms.ToolStripMenuItem _hooksMenuItem;

        public TrayIconService(Action showWindowAction, Action toggleHooksAction, Action exitAction)
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
            catch (Exception ex)
            {
                // Логируем ошибку загрузки иконки
                System.Diagnostics.Debug.WriteLine($"Failed to load tray icon: {ex.Message}");
                trayIcon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = trayIcon,
                Visible = true,
                Text = "iikoServiceHelper_v2"
            };

            var contextMenu = new Forms.ContextMenuStrip();
            
            // Применяем темную тему
            contextMenu.Renderer = new NoHighlightRenderer();
            contextMenu.BackColor = Color.FromArgb(30, 30, 36); // #1E1E24
            contextMenu.ForeColor = Color.WhiteSmoke;

            // Хелпер для создания пунктов меню
            Forms.ToolStripMenuItem AddItem(string text, Action onClick)
            {
                var item = new Forms.ToolStripMenuItem(text);
                item.Click += (s, e) => onClick();
                item.ForeColor = Color.WhiteSmoke;
                contextMenu.Items.Add(item);
                return item;
            }

            AddItem("Развернуть", showWindowAction);
            _hooksMenuItem = AddItem("Отключить перехват", toggleHooksAction);

            contextMenu.Items.Add(new Forms.ToolStripSeparator());
            AddItem("Выход", exitAction);

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => showWindowAction();

            contextMenu.MouseLeave += (s, e) =>
            {
                if (!contextMenu.Bounds.Contains(Forms.Cursor.Position))
                {
                    contextMenu.Close();
                }
            };
        }

        public void UpdateState(bool hooksDisabled)
        {
            if (hooksDisabled)
            {
                _hooksMenuItem.Text = "Включить перехват";
                _trayIcon.Text = "iikoServiceHelper_v2 (Hooks Disabled)";
            }
            else
            {
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

        // Кастомный рендерер для полного отключения подсветки
        private class NoHighlightRenderer : Forms.ToolStripProfessionalRenderer
        {
            public NoHighlightRenderer() : base(new DarkColorTable()) { }

            protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
            {
                // Рисуем фон всегда одним цветом, игнорируя наведение мыши
                var rc = new Rectangle(Point.Empty, e.Item.Size);
                using (var brush = new SolidBrush(Color.FromArgb(30, 30, 36))) // #1E1E24
                {
                    e.Graphics.FillRectangle(brush, rc);
                }
            }
        }

        // Класс для стилизации темного меню
        private class DarkColorTable : Forms.ProfessionalColorTable
        {
            private readonly Color _backColor = Color.FromArgb(30, 30, 36); // #1E1E24
            private readonly Color _borderColor = Color.FromArgb(62, 62, 66);
            private readonly Color _selectionColor = Color.FromArgb(30, 30, 36);

            public override Color MenuItemSelected => _selectionColor;
            public override Color MenuItemBorder => _selectionColor;
            public override Color MenuBorder => _borderColor;
            public override Color MenuItemSelectedGradientBegin => _selectionColor;
            public override Color MenuItemSelectedGradientEnd => _selectionColor;
            public override Color MenuItemPressedGradientBegin => _selectionColor;
            public override Color MenuItemPressedGradientEnd => _selectionColor;
            public override Color ImageMarginGradientBegin => _backColor;
            public override Color ImageMarginGradientMiddle => _backColor;
            public override Color ImageMarginGradientEnd => _backColor;
            public override Color ToolStripDropDownBackground => _backColor;
        }
    }
}