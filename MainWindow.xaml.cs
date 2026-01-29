using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Forms = System.Windows.Forms; 
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using iikoServiceHelper.Services;
using iikoServiceHelper.Models;
using iikoServiceHelper.Utils;

namespace iikoServiceHelper
{
    public partial class MainWindow : Window, ICommandHost
    {
        private const string AppName = "iikoServiceHelper_v2";
        private readonly string AppDir;
        private readonly string NotesFile;
        private readonly string SettingsFile;
        private readonly string DetailedLogFile;
        private readonly object _logLock = new();

        private HotkeyManager? _hotkeyManager;
        private OverlayWindow _overlay;
        private Dictionary<string, Action> _hotkeyActions = new(StringComparer.OrdinalIgnoreCase);
        private ObservableCollection<HotkeyDisplay> _displayItems = new();
        
        private volatile bool _isPaused = false;
        private int _commandCount = 0;
        private bool _hooksDisabled = false;
        private DateTime _lastUpdateCheck = DateTime.MinValue;
        
        private readonly CommandExecutionService _commandExecutionService;
        private readonly UpdateService _updateService;
        private readonly CrmAutoLoginService _crmAutoLoginService;
        private readonly TrayIconService _trayIconService;
        private AltBlockerService? _altBlockerService;

        private Popup? _tempNotificationPopup;
        private DispatcherTimer? _tempNotificationTimer;

        public MainWindow()
        {
            InitializeComponent();

            // Paths setup
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            AppDir = Path.Combine(localAppData, AppName);
            Directory.CreateDirectory(AppDir);
            NotesFile = Path.Combine(AppDir, "notes.txt");
            SettingsFile = Path.Combine(AppDir, "settings.json");
            DetailedLogFile = Path.Combine(AppDir, "detailed_log.txt");

            // Init Services
            _hotkeyManager = new HotkeyManager();
            _commandExecutionService = new CommandExecutionService(this, _hotkeyManager);
            _updateService = new UpdateService(ShowUpdateDialog, ShowCustomMessage);
            _crmAutoLoginService = new CrmAutoLoginService();

            // UI-dependent services
            _trayIconService = new TrayIconService(ShowWindow, TogglePause, ToggleHooks, () => System.Windows.Application.Current.Shutdown());
            _altBlockerService = new AltBlockerService(_hotkeyManager, LogDetailed);

            // Init Logic
            InitializeHotkeys();
            LoadNotes();
            LoadSettings();
            InitializeTempNotificationPopup();

            // Wire up services
            SetupServiceEvents();
            _overlay = new OverlayWindow();
            
            // Set bindings
            ReferenceGrid.ItemsSource = _displayItems;

            // Setup Global Hooks
            if (_hotkeyManager != null) _hotkeyManager.HotkeyHandler = OnGlobalHotkey;
            
            // Handle close
            this.Closing += (s, e) => 
            {
                SaveNotes();
                SaveSettings();
                _trayIconService.Dispose();
                _crmAutoLoginService.Dispose();
                _hotkeyManager?.Dispose();
                _altBlockerService?.Dispose();
                System.Windows.Application.Current.Shutdown();
            };

            this.StateChanged += (s, e) =>
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    this.Hide();
                    _trayIconService.ShowBalloonTip(2000, "iikoServiceHelper", "Приложение работает в фоне", Forms.ToolTipIcon.Info);
                }
            };

            // Auto-check for updates on startup (Silent)
            Task.Run(() => _updateService.CheckForUpdates(true, _lastUpdateCheck, newTime =>
            {
                Dispatcher.Invoke(() =>
                {
                    _lastUpdateCheck = newTime;
                    SaveSettings();
                });
            }));
        }

        private void InitializeTempNotificationPopup()
        {
            var textBlock = new TextBlock
            {
                Name = "TempNotificationText",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold
            };

            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E24")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B026FF")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 6, 12, 6),
                Child = textBlock
            };

            _tempNotificationPopup = new Popup
            {
                AllowsTransparency = true,
                IsHitTestVisible = false,
                Placement = PlacementMode.Center,
                Child = border
            };

            _tempNotificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tempNotificationTimer.Tick += (s, e) => {
                if (_tempNotificationPopup != null) _tempNotificationPopup.IsOpen = false;
                _tempNotificationTimer?.Stop();
            };
        }

        private void SetupServiceEvents()
        {
            // CRM Service
            _crmAutoLoginService.LogMessage += Log;
            _crmAutoLoginService.StatusUpdated += OnCrmStatusUpdated;
            _crmAutoLoginService.LastRunUpdated += OnCrmLastRunUpdated;

            // Update Service
            _updateService.StatusChanged += (status) => Dispatcher.Invoke(() => txtUpdateLink.Text = status);
            _updateService.ProgressChanged += (progress) => Dispatcher.Invoke(() => {
                pbUpdate.Visibility = Visibility.Visible;
                pbUpdate.Value = progress;
                txtUpdateLink.Text = $"Скачивание {progress:F0}%";
            });
            _updateService.DownloadCompleted += (fileName, savePath) => Dispatcher.Invoke(() => {
                pbUpdate.Visibility = Visibility.Collapsed;
                txtUpdateLink.Text = "Обновить";
                ShowCustomMessage("Обновление", $"Файл успешно скачан:\n{fileName}", false);
                try { Process.Start("explorer.exe", $"/select,\"{savePath}\""); } catch { }
            });
            _updateService.UpdateFailed += (title, message) => Dispatcher.Invoke(() => {
                pbUpdate.Visibility = Visibility.Collapsed;
                ShowCustomMessage(title, message, true);
            });
        }

        private void OnCrmStatusUpdated(string status)
        {
            Dispatcher.Invoke(() =>
            {
                txtCrmStatus.Text = status;
                if (status.Contains("Активно") || status.Contains("Вход ОК"))
                {
                    txtCrmStatus.Foreground = (System.Windows.Media.Brush)FindResource("BrushAccent");
                }
                else
                {
                    txtCrmStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                }
            });
        }
        private void OnCrmLastRunUpdated(string text)
        {
            Dispatcher.Invoke(() => txtLastRun.Text = text);
        }

        private void LogDetailed(string message)
        {
            try
            {
                lock (_logLock)
                {
                    File.AppendAllText(DetailedLogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }

        private void InitializeHotkeys()
        {
            _hotkeyActions.Clear();
            _displayItems.Clear();

            // Это действие зависит от UI (диалоговое окно), поэтому оно определяется здесь,
            // а не в провайдере горячих клавиш.
            Action openCrmDialog = () => 
            {
                string? result = null;
                Application.Current.Dispatcher.Invoke(() => 
                {
                    try
                    {
                        // CrmIdInputDialog - это кастомное окно для ввода ID.
                        var dlg = new CrmIdInputDialog();
                        dlg.Owner = Application.Current.MainWindow;
                        if (dlg.ShowDialog() == true) result = dlg.ResultIds;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Не удалось открыть окно: {ex.Message}\nПопробуйте пересобрать проект (Rebuild).");
                    }
                });

                if (!string.IsNullOrWhiteSpace(result))
                {
                    Thread.Sleep(1000); // Пауза для возврата фокуса в окно чата
                    _commandExecutionService.Enqueue("Bot", $"cmd duplicate {result}", "Alt+Shift+D8");
                }
            };

            var (actions, displayItems) = HotkeyProvider.RegisterAll(_commandExecutionService, openCrmDialog);

            _hotkeyActions = actions;
            foreach (var item in displayItems)
            {
                _displayItems.Add(item);
            }
        }

        private void TogglePause()
        {
            _isPaused = !_isPaused;
            _trayIconService.UpdateState(_isPaused, _hooksDisabled);
        }

        private void ToggleHooks()
        {
            _hooksDisabled = !_hooksDisabled;
            if (_hooksDisabled)
            {
                _hotkeyManager?.Dispose();
                _hotkeyManager = null;
                _altBlockerService?.Disable();
            }
            else
            {
                _hotkeyManager = new HotkeyManager();
                _hotkeyManager.HotkeyHandler = OnGlobalHotkey;
                _altBlockerService = new AltBlockerService(_hotkeyManager, LogDetailed);
                UpdateAltHookState(chkAltBlocker.IsChecked == true);
            }
            _trayIconService.UpdateState(_isPaused, _hooksDisabled);
        }

        private void ShowWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        // ================= LOGIC: PASTE & MACROS =================

        private bool OnGlobalHotkey(string keyCombo)
        {
            if (_isPaused) return false;
    
            Debug.WriteLine($"Detected: {keyCombo}"); // Debugging
            if (_hotkeyActions.TryGetValue(keyCombo, out var action))
            {
                LogDetailed($"HOTKEY DETECTED: {keyCombo}");
                if (keyCombo.Equals("Alt+Q", StringComparison.OrdinalIgnoreCase))
                {
                    // Выполняем немедленно, вне очереди
                    action.Invoke();
                }
                else
                {
                    action.Invoke();
                }
                return true; // Suppress original key press
            }
            return false;
        }

        private void IncrementCommandCount()
        {
            _commandCount++;
            txtCommandCount.Text = _commandCount.ToString();
        }

        private void ResetCommandCount_Click(object sender, RoutedEventArgs e)
        {
            LogDetailed("User manually reset command count.");
            _commandCount = 0;
            txtCommandCount.Text = "0";
        }

        public bool IsInputFocused()
        {
            try
            {
                var element = AutomationElement.FocusedElement;
                if (element == null) return false;

                var type = element.Current.ControlType;
                return type == ControlType.Edit || 
                       type == ControlType.Document || 
                       type == ControlType.Custom ||
                       type == ControlType.Text;
            }
            catch 
            {
                return true; // В случае ошибки не блокируем выполнение
            }
        }

        #region ICommandHost Implementation
        void ICommandHost.UpdateOverlay(string message) => _overlay.ShowMessage(message);
        void ICommandHost.HideOverlay() => _overlay.HideMessage();
        void ICommandHost.LogDetailed(string message) => LogDetailed(message);
        void ICommandHost.IncrementCommandCount() => IncrementCommandCount();
        void ICommandHost.RunOnUIThread(Action action) => Dispatcher.Invoke(action);
        void ICommandHost.ClipboardClear() { try { Clipboard.Clear(); } catch { } }
        void ICommandHost.ClipboardSetText(string text) { try { Clipboard.SetText(text); } catch { } }
        string? ICommandHost.ClipboardGetText() { try { return Clipboard.GetText(); } catch { return null; } }
        bool ICommandHost.ClipboardContainsText() { try { return Clipboard.ContainsText(); } catch { return false; } }
        void ICommandHost.SendKeysWait(string keys) => Forms.SendKeys.SendWait(keys);
        async Task ICommandHost.CleanClipboardHistoryAsync(int itemsToDelete)
        {
            await Task.Run(async () =>
            {
                try
                {
                    // Requires Windows 10/11 and WinRT API support
                    if (!Windows.ApplicationModel.DataTransfer.Clipboard.IsHistoryEnabled()) return;

                    var history = await Windows.ApplicationModel.DataTransfer.Clipboard.GetHistoryItemsAsync();
                    if (history.Status == Windows.ApplicationModel.DataTransfer.ClipboardHistoryItemsResultStatus.Success)
                    {
                        var items = history.Items;
                        // Items are sorted by time (most recent first)
                        for (int i = 0; i < itemsToDelete && i < items.Count; i++)
                        {
                            Windows.ApplicationModel.DataTransfer.Clipboard.DeleteItemFromHistory(items[i]);
                        }
                    }
                }
                catch (Exception ex) { LogDetailed($"Clipboard History Error: {ex.Message}"); }
            });
        }
        #endregion

        // Notes
        private void LoadNotes()
        {
            if (File.Exists(NotesFile)) txtNotes.Text = File.ReadAllText(NotesFile);
        }

        private void SaveNotes()
        {
            try { File.WriteAllText(NotesFile, txtNotes.Text); } catch { }
        }

        private void txtNotes_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveNotes();
        }

        // External App
        private async void OpenOrderCheck_Click(object sender, RoutedEventArgs e)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string exePath = Path.Combine(appDir, "OrderCheck.exe");

            if (File.Exists(exePath))
            {
                try { Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"Ошибка запуска: {ex.Message}"); }
            }
            else
            {
                if (sender is Button btn)
                {
                    btn.IsEnabled = false;
                    btn.Content = "СКАЧИВАНИЕ...";
                }

                try
                {
                    using var client = new HttpClient();
                    var bytes = await client.GetByteArrayAsync("https://clearbat.iiko.online/downloads/OrderCheck.exe");
                    await File.WriteAllBytesAsync(exePath, bytes);
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show($"Ошибка скачивания: {ex.Message}"); }
                finally
                {
                    if (sender is Button btnEnd)
                    {
                        btnEnd.IsEnabled = true;
                        btnEnd.Content = "ORDERCHECK";
                    }
                }
            }
        }

        private async void OpenClearBat_Click(object sender, RoutedEventArgs e)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string exePath = Path.Combine(appDir, "CLEAR.bat.exe");

            if (File.Exists(exePath))
            {
                try { Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"Ошибка запуска: {ex.Message}"); }
            }
            else
            {
                if (sender is Button btn)
                {
                    btn.IsEnabled = false;
                    btn.Content = "СКАЧИВАНИЕ...";
                }

                try
                {
                    using var client = new HttpClient();
                    var bytes = await client.GetByteArrayAsync("https://clearbat.iiko.online/downloads/CLEAR.bat.exe");
                    await File.WriteAllBytesAsync(exePath, bytes);
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show($"Ошибка скачивания: {ex.Message}"); }
                finally
                {
                    if (sender is Button btnEnd)
                    {
                        btnEnd.IsEnabled = true;
                        btnEnd.Content = "CLEAR.bat";
                    }
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private void MoveExplorerTo(int x, int y)
        {
            Task.Run(async () =>
            {
                try
                {
                    IntPtr hWnd = IntPtr.Zero;
                    for (int i = 0; i < 10; i++)
                    {
                        await Task.Delay(200);
                        IntPtr fg = GetForegroundWindow();
                        GetWindowThreadProcessId(fg, out uint pid);
                        try
                        {
                            var p = Process.GetProcessById((int)pid);
                            if (p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                            {
                                hWnd = fg;
                                break;
                            }
                        }
                        catch { }
                    }

                    if (hWnd != IntPtr.Zero) SetWindowPos(hWnd, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004);
                }
                catch { }
            });
        }

        private void OpenFtp_Click(object sender, RoutedEventArgs e)
        {
            string path = @"\\files.resto.lan\";
            string user = txtCrmLogin.Text;
            string pass = txtCrmPassword.Password;

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            {
                MessageBox.Show("Для доступа к FTP заполните Логин и Пароль от CRM.");
                return;
            }

            int targetX = (int)this.Left + 30;
            int targetY = (int)this.Top + 30;
            if (this.WindowState == WindowState.Maximized)
            {
                targetX = 50; targetY = 50;
            }

            Task.Run(() =>
            {
                try
                {
                    // Выполняем net use для подключения (сначала удаляем старое, затем создаем новое)
                    var pDel = Process.Start(new ProcessStartInfo("net", @"use \\files.resto.lan /delete /y") { CreateNoWindow = true, UseShellExecute = false });
                    pDel?.WaitForExit();

                    var pUse = Process.Start(new ProcessStartInfo("net", $@"use \\files.resto.lan /user:{user} {pass} /persistent:yes") { CreateNoWindow = true, UseShellExecute = false });
                    pUse?.WaitForExit();

                    Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                    MoveExplorerTo(targetX, targetY);
                }
                catch (Exception ex) { Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Ошибка: {ex.Message}")); }
            });
        }

        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(AppDir))
                {
                    int targetX = (int)this.Left + 30;
                    int targetY = (int)this.Top + 30;
                    if (this.WindowState == WindowState.Maximized)
                    {
                        targetX = 50; targetY = 50;
                    }
                    Process.Start(new ProcessStartInfo("explorer.exe", AppDir) { UseShellExecute = true });
                    MoveExplorerTo(targetX, targetY);
                }
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка: {ex.Message}"); }
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (txtNotes.FontSize < 72) txtNotes.FontSize += 1;
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (txtNotes.FontSize > 8) txtNotes.FontSize -= 1;
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) 
                    {
                        txtNotes.FontSize = settings.NotesFontSize;
                        txtCrmLogin.Text = settings.CrmLogin;
                        txtCrmPassword.Password = settings.CrmPassword;

                        RefreshBrowserList();
                        if (!string.IsNullOrEmpty(settings.SelectedBrowser))
                            cmbBrowsers.SelectedValue = settings.SelectedBrowser;

                        // Restore Window Position
                        if (IsOnScreen(settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight))
                        {
                            this.Top = settings.WindowTop;
                            this.Left = settings.WindowLeft;
                        }
                        else
                        {
                            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                        }
                        this.Width = settings.WindowWidth;
                        this.Height = settings.WindowHeight;
                        
                        if (settings.WindowState == (int)WindowState.Maximized)
                            this.WindowState = WindowState.Maximized;

                        // Restore Alt Blocker State
                        if (chkAltBlocker != null) chkAltBlocker.IsChecked = settings.IsAltBlockerEnabled;
                        _lastUpdateCheck = settings.LastUpdateCheck;
                        // State is applied after services are initialized
                        UpdateAltHookState(settings.IsAltBlockerEnabled);
                        _commandCount = settings.CommandCount;
                        if (txtCommandCount != null) txtCommandCount.Text = _commandCount.ToString();
                    }
                    else
                    {
                        UpdateAltHookState(true); // Default
                        if (chkAltBlocker != null) chkAltBlocker.IsChecked = true;
                    }
                }
                else
                {
                    UpdateAltHookState(true); // Default if no settings file
                    if (chkAltBlocker != null) chkAltBlocker.IsChecked = true;
                }
            }
            catch { }
        }

        private bool IsOnScreen(double left, double top, double width, double height)
        {
            var rect = new System.Drawing.Rectangle((int)left, (int)top, (int)width, (int)height);
            return Forms.Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect));
        }

        private void SaveSettings()
        {
            try
            {
                // Handle Minimized state by saving as Normal
                var stateToSave = this.WindowState == WindowState.Minimized ? WindowState.Normal : this.WindowState;

                var settings = new AppSettings 
                { 
                    NotesFontSize = txtNotes.FontSize,
                    CrmLogin = txtCrmLogin.Text,
                    CrmPassword = txtCrmPassword.Password,
                    SelectedBrowser = cmbBrowsers.SelectedValue as string ?? "",
                    
                    WindowTop = this.WindowState == WindowState.Normal ? this.Top : this.RestoreBounds.Top,
                    WindowLeft = this.WindowState == WindowState.Normal ? this.Left : this.RestoreBounds.Left,
                    WindowWidth = this.WindowState == WindowState.Normal ? this.Width : this.RestoreBounds.Width,
                    WindowHeight = this.WindowState == WindowState.Normal ? this.Height : this.RestoreBounds.Height,
                    WindowState = (int)stateToSave,
                    IsAltBlockerEnabled = chkAltBlocker?.IsChecked == true,
                    LastUpdateCheck = _lastUpdateCheck,
                    CommandCount = _commandCount
                };
                var json = JsonSerializer.Serialize(settings);
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }

        private void TxtCrm_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void Log(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                txtLog.ScrollToEnd();
            });
        }

        // ================= CRM AUTO LOGIN =================

        private void CmbBrowsers_DropDownOpened(object sender, EventArgs e)
        {
            RefreshBrowserList();
        }

        private void BtnRefreshBrowsers_Click(object sender, RoutedEventArgs e)
        {
            RefreshBrowserList();
        }

        private void RefreshBrowserList()
        {
            var selectedPath = cmbBrowsers.SelectedValue as string;
            var foundBrowsers = BrowserFinder.FindAll();

            cmbBrowsers.ItemsSource = foundBrowsers;
            
            if (selectedPath != null && foundBrowsers.Any(b => b.Path == selectedPath))
                cmbBrowsers.SelectedValue = selectedPath;
            else if (foundBrowsers.Count > 0)
                cmbBrowsers.SelectedIndex = 0;
        }

        private async void BtnCrmAutoLogin_Click(object sender, RoutedEventArgs e) 
        {
            if (_crmAutoLoginService.IsActive)
            {
                _crmAutoLoginService.Stop();
                btnCrmAutoLogin.Content = "ВКЛЮЧИТЬ";
            }
            else
            {
                if (cmbBrowsers.SelectedItem is not BrowserItem selectedBrowser)
                {
                    MessageBox.Show("Сначала выберите браузер из списка.");
                    return;
                }

                if (string.IsNullOrEmpty(txtCrmLogin.Text) || string.IsNullOrEmpty(txtCrmPassword.Password))
                {
                    MessageBox.Show("Введите Логин и Пароль для авто-входа.");
                    return;
                }

                // Проверка: Доступен ли порт отладки?
                bool cdpAvailable = false;
                try 
                { 
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                    await http.GetStringAsync("http://127.0.0.1:9222/json"); 
                    cdpAvailable = true; 
                } catch { }

                if (!cdpAvailable)
                {
                    MessageBox.Show("Порт 9222 закрыт.\nТребуется запуск браузера с параметром \"--remote-debugging-port=9222\"", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _crmAutoLoginService.Start(txtCrmLogin.Text, txtCrmPassword.Password, selectedBrowser);
                btnCrmAutoLogin.Content = "СТОП";
            }
        }

        private void BtnCrmSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new Window
            {
                Title = "Настройка авто-входа",
                Width = 400,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E24")),
                Foreground = System.Windows.Media.Brushes.White
            };

            var stack = new StackPanel { Margin = new Thickness(20) };

            var txt = new TextBox
            {
                Text = "Для работы Авто-входа в CRM зайти в свойства ярлыка браузера и в поле Объект , после \"\" через пробел добавить --remote-debugging-port=9222",
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                Margin = new Thickness(0, 0, 0, 20)
            };

            var btnClose = new Button
            {
                Content = "Закрыть",
                Width = 100,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("BrushAccent"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("BrushAccent"),
                Background = System.Windows.Media.Brushes.Transparent
            };
            btnClose.Click += (s, args) => win.Close();

            stack.Children.Add(txt);
            stack.Children.Add(btnClose);
            win.Content = stack;

            win.ShowDialog();
        }

        private async void BtnCheckPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                await http.GetStringAsync("http://127.0.0.1:9222/json");
                ShowCustomMessage("Статус порта", "Порт 9222 ДОСТУПЕН.\nБраузер готов к управлению.", false);
            }
            catch
            {
                ShowCustomMessage("Статус порта", "Порт 9222 НЕДОСТУПЕН.\nУбедитесь, что браузер запущен с флагом --remote-debugging-port=9222", true);
            }
        }

        private void ShowCustomMessage(string title, string message, bool isError)
        {
            Dispatcher.Invoke(() =>
            {
                var win = new Window
                {
                    Title = title,
                    Width = 350,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false
                };

                Style? btnStyle = null;
                try { btnStyle = (Style)this.FindResource(typeof(Button)); } catch { }

                var border = new Border
                {
                    Background = (System.Windows.Media.Brush)FindResource("BrushBackground"),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("BrushAccent"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(20),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect 
                    { 
                        BlurRadius = 20, 
                        ShadowDepth = 0, 
                        Opacity = 0.5, 
                        Color = (System.Windows.Media.Color)FindResource("ColorAccent") 
                    }
                };

                var stack = new StackPanel();

                var txtHeader = new TextBlock
                {
                    Text = isError ? "❌ ОШИБКА" : "✅ ИНФО",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = isError ? System.Windows.Media.Brushes.IndianRed : System.Windows.Media.Brushes.LightGreen,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 15)
                };

                var txtMessageBlock = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 20),
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushForeground")
                };

                var btnOk = new Button
                {
                    Content = "OK",
                    Width = 100,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Style = btnStyle
                };
                btnOk.Click += (s, e) => win.Close();

                stack.Children.Add(txtHeader);
                stack.Children.Add(txtMessageBlock);
                stack.Children.Add(btnOk);

                border.Child = stack;
                win.Content = border;
                win.ShowDialog();
            });
        }

        private void BtnCopyPosM1_Click(object sender, RoutedEventArgs e)
        {
            CopyLinkAndNotify("https://m1.iiko.cards/ru-RU/About/DownloadPosInstaller?useRc=False");
        }

        private void BtnCopyPosM_Click(object sender, RoutedEventArgs e)
        {
            CopyLinkAndNotify("https://iiko.cards/ru-RU/About/DownloadPosInstaller?useRc=False");
        }

        private void CopyLinkAndNotify(string url)
        {
            try
            {
                Clipboard.SetText(url);
                ShowTempNotification("Ссылка скопирована");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка копирования: {ex.Message}");
            }
        }

        private void ShowTempNotification(string message)
        {
            if (_tempNotificationPopup == null || _tempNotificationTimer == null) return;

            if ((_tempNotificationPopup.Child as Border)?.Child is TextBlock textBlock)
            {
                textBlock.Text = message;
            }
            
            _tempNotificationPopup.PlacementTarget = this;
            _tempNotificationPopup.IsOpen = true;
            _tempNotificationTimer.Start();
        }

        // ================= UPDATER =================

        private void TxtUpdateLink_Click(object sender, MouseButtonEventArgs e)
        {
            txtUpdateLink.Text = "Проверка...";
            Task.Run(async () => 
            {
                await _updateService.CheckForUpdates(false, _lastUpdateCheck, newTime => {
                    Dispatcher.Invoke(() =>
                    {
                        _lastUpdateCheck = newTime;
                        SaveSettings();
                    });
                });
                Dispatcher.Invoke(() => txtUpdateLink.Text = "Обновить");
            });
        }

        private bool ShowUpdateDialog(string newVersion, string currentVersion)
        {
            bool result = false;
            Dispatcher.Invoke(() =>
            {
                var win = new Window
                {
                    Title = "Доступно обновление",
                    Width = 400,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false
                };

                // Пытаемся получить стиль кнопок из ресурсов главного окна
                Style? btnStyle = null;
                try { btnStyle = (Style)this.FindResource(typeof(Button)); } catch { }

                var border = new Border
                {
                    Background = (System.Windows.Media.Brush)FindResource("BrushBackground"),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("BrushAccent"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(20),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect 
                    { 
                        BlurRadius = 20, 
                        ShadowDepth = 0, 
                        Opacity = 0.5, 
                        Color = (System.Windows.Media.Color)FindResource("ColorAccent") 
                    }
                };

                var stack = new StackPanel();

                var txtHeader = new TextBlock
                {
                    Text = "🚀 ОБНОВЛЕНИЕ",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushAccent"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 15)
                };

                var txtMessage = new TextBlock
                {
                    Text = $"Доступна новая версия: {newVersion}\n(Текущая: {currentVersion})",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 15,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 10),
                    Foreground = System.Windows.Media.Brushes.White
                };

                var txtQuestion = new TextBlock
                {
                    Text = "Скачать и установить?",
                    FontSize = 14,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 25),
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushForeground")
                };

                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

                var btnYes = new Button 
                { 
                    Content = "СКАЧАТЬ", 
                    Width = 120, 
                    Margin = new Thickness(0, 0, 15, 0),
                    Style = btnStyle 
                };
                btnYes.Click += (s, e) => { win.DialogResult = true; win.Close(); };

                var btnNo = new Button 
                { 
                    Content = "ОТМЕНА", 
                    Width = 120,
                    Style = btnStyle,
                    BorderBrush = System.Windows.Media.Brushes.Gray, 
                    Foreground = System.Windows.Media.Brushes.Gray 
                };
                btnNo.Click += (s, e) => { win.DialogResult = false; win.Close(); };

                btnPanel.Children.Add(btnYes);
                btnPanel.Children.Add(btnNo);

                stack.Children.Add(txtHeader);
                stack.Children.Add(txtMessage);
                stack.Children.Add(txtQuestion);
                stack.Children.Add(btnPanel);

                border.Child = stack;
                win.Content = border;
                
                // Показываем диалог и ждем результата
                result = win.ShowDialog() == true;
            });

            return result;
        }

        // ================= GLOBAL ALT BLOCKER =================

        private void UpdateAltHookState(bool enable)
        {
            if (_hooksDisabled || _altBlockerService == null) return;

            if (enable)
            {
                _altBlockerService.Enable();
            }
            else
            {
                _altBlockerService.Disable();
            }
        }

        private void ChkAltBlocker_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                LogDetailed($"User toggled Alt Blocker. New state: {chk.IsChecked}");
                UpdateAltHookState(chk.IsChecked == true);
                SaveSettings(); // Сохраняем настройку сразу при изменении
            }
        }
    }
}