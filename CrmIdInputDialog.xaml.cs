using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace iikoServiceHelper
{
    public partial class CrmIdInputDialog : Window
    {
        public string ResultIds { get; private set; } = "";
        private DispatcherTimer _clipboardTimer;
        private string _lastClipboardText = "";

        public CrmIdInputDialog()
        {
            InitializeComponent();
            this.Loaded += (s, e) => this.Activate();
            // Таймер для слежения за буфером обмена
            _clipboardTimer = new DispatcherTimer();
            _clipboardTimer.Interval = TimeSpan.FromMilliseconds(500);
            _clipboardTimer.Tick += ClipboardTimer_Tick;
            _clipboardTimer.Start();
            
            CheckClipboard();
        }

        private void ClipboardTimer_Tick(object? sender, EventArgs e)
        {
            CheckClipboard();
        }

        private void CheckClipboard()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText().Trim();
                    // Если текст изменился и является числом
                    if (text != _lastClipboardText)
                    {
                        _lastClipboardText = text;
                        if (Regex.IsMatch(text, @"^\d+$"))
                        {
                            AppendId(text);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Логируем ошибку доступа к буферу обмена
                System.Diagnostics.Debug.WriteLine($"Clipboard access error in CrmIdInputDialog: {ex.Message}");
            }
        }

        private void AppendId(string id)
        {
            if (!string.IsNullOrWhiteSpace(txtList.Text) && !txtList.Text.EndsWith("\n"))
            {
                txtList.AppendText(Environment.NewLine);
            }
            txtList.AppendText(id);
            txtList.ScrollToEnd();
        }

        private void BtnTransfer_Click(object sender, RoutedEventArgs e)
        {
            var lines = txtList.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(x => x.Trim())
                                    .Where(x => !string.IsNullOrEmpty(x));
            
            ResultIds = string.Join(",", lines);
            try { Clipboard.Clear(); } catch { }
            
            DialogResult = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _clipboardTimer.Stop();
            base.OnClosed(e);
        }
    }
}