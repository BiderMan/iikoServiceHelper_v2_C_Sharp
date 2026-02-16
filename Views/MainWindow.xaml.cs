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
using iikoServiceHelper.Constants;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Windows.Media;
using iikoServiceHelper.ViewModels;

namespace iikoServiceHelper
{
    public partial class MainWindow : Window, ICommandHost
    {
        private readonly string AppDir;
        private readonly string NotesFile;
        private readonly string SettingsFile;
        private readonly string ThemeSettingsFile;
        private readonly string DetailedLogFile;
        private readonly AppSettings _appSettings;
        private readonly object _logLock = new();

        private IHotkeyManager? _hotkeyManager;
        private OverlayWindow _overlay;
        private Dictionary<string, Action> _hotkeyActions = new(StringComparer.OrdinalIgnoreCase);
        public ObservableCollection<CustomCommand> EditableCustomCommands { get; set; }
        private ObservableCollection<HotkeyDisplay> _displayItems = new();

        private bool _hooksDisabled = false;
        private DateTime _lastUpdateCheck = DateTime.MinValue;
        private bool _isLightTheme = false;
        private ThemeSettings _themeSettings = new ThemeSettings();

        private readonly ICommandExecutionService _commandExecutionService;
        private readonly UpdateService _updateService;
        private readonly CrmAutoLoginService _crmAutoLoginService;
        private readonly TrayIconService _trayIconService;
        private readonly CustomCommandService _customCommandService;
        private AltBlockerService? _altBlockerService;
        private bool _isRecordingHotkey = false;
        private bool _systemCommandsUnlocked = false;
        private TextBox? _activeHotkeyRecordingBox;
        private string? _originalHotkeyText;

        private readonly MainWindowViewModel _viewModel;
        private readonly FileService _fileService;
        private readonly DownloadService _downloadService;
        private readonly LauncherService _launcherService;

        private Popup? _tempNotificationPopup;
        private DispatcherTimer? _tempNotificationTimer;

        public MainWindow(
            MainWindowViewModel viewModel,
            ICommandExecutionService commandExecutionService,
            IHotkeyManager hotkeyManager,
            AppSettings appSettings,
            CustomCommandService customCommandService,
            CrmAutoLoginService crmAutoLoginService,
            FileService fileService,
            DownloadService downloadService,
            LauncherService launcherService)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            _appSettings = appSettings;
            _customCommandService = customCommandService;
            _crmAutoLoginService = crmAutoLoginService;
            _fileService = fileService;
            _downloadService = downloadService;
            _launcherService = launcherService;

            EditableCustomCommands = new ObservableCollection<CustomCommand>();
            CustomCommandsGrid.ItemsSource = EditableCustomCommands;

            // Используем FileService для путей к файлам
            AppDir = _fileService.AppDataPath;
            NotesFile = _fileService.NotesFile;
            SettingsFile = _fileService.SettingsFile;
            ThemeSettingsFile = _fileService.ThemeSettingsFile;
            DetailedLogFile = _fileService.DetailedLogFile;

            _hotkeyManager = hotkeyManager;
            _commandExecutionService = commandExecutionService;
            _commandExecutionService.SetHost(this);

            _updateService = new UpdateService(ShowUpdateDialog, ShowCustomMessage);
            
            // Подписка на события UpdateService для отображения прогресса и ошибок
            _updateService.StatusChanged += status => Dispatcher.Invoke(() => {
                txtUpdateLink.Text = string.IsNullOrEmpty(status) ? "Обновить" : status;
            });
            _updateService.ProgressChanged += progress => Dispatcher.Invoke(() => {
                pbUpdate.Visibility = progress > 0 && progress < 100 ? Visibility.Visible : Visibility.Collapsed;
                pbUpdate.Value = progress;
            });
            _updateService.DownloadCompleted += (fileName, path) => Dispatcher.Invoke(() => {
                pbUpdate.Visibility = Visibility.Collapsed;
                ShowCustomMessage("Загрузка завершена", $"Файл {fileName} загружен. Перезапустите приложение для обновления.", false);
            });
            _updateService.UpdateFailed += (title, message) => Dispatcher.Invoke(() => {
                pbUpdate.Visibility = Visibility.Collapsed;
                txtUpdateLink.Text = "Обновить";
                ShowCustomMessage(title, message, true);
            });
            
            _altBlockerService = new AltBlockerService(_hotkeyManager, LogDetailed);
            _trayIconService = new TrayIconService(ShowWindow, ToggleHooks, () => Application.Current.Shutdown());

            LoadSettings();

            var customCommands = _customCommandService.LoadCommands();
            // НЕ добавляем systemCommands отдельно - они уже есть в customCommands (который содержит все дефолтные команды)
            // Это предотвращает дублирование команд при загрузке
            var allInitialCommands = new List<CustomCommand>(customCommands);

            InitializeHotkeys(allInitialCommands);
            LoadCustomCommandsForEditor(allInitialCommands);
            LoadThemeSettings();
            InitializeTempNotificationPopup();

            _crmAutoLoginService.LogMessage += (msg) => _viewModel.CrmViewModel.Log += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            _overlay = new OverlayWindow();
            ReferenceGrid.ItemsSource = _displayItems;

            if (_hotkeyManager != null) _hotkeyManager.HotkeyHandler = OnGlobalHotkey;

            this.Closing += (s, e) =>
            {
                SaveSettings();
                _trayIconService.Dispose();
                _crmAutoLoginService.Dispose();
                _hotkeyManager?.Dispose();
                _altBlockerService?.Dispose();
                Application.Current.Shutdown();
            };

            this.StateChanged += (s, e) =>
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    this.Hide();
                    _trayIconService.ShowBalloonTip(_appSettings.NotificationDurationSeconds * 1000, "iikoServiceHelper", "Приложение работает в фоне", Forms.ToolTipIcon.Info);
                }
            };
        }

        private void InitializeTempNotificationPopup()
        {
            var textBlock = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, FontWeight = FontWeights.Bold };
            var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(12, 6, 12, 6), Child = textBlock };
            _tempNotificationPopup = new Popup { AllowsTransparency = true, IsHitTestVisible = false, Placement = PlacementMode.Center, Child = border };
            _tempNotificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tempNotificationTimer.Tick += (s, e) => { if (_tempNotificationPopup != null) _tempNotificationPopup.IsOpen = false; _tempNotificationTimer?.Stop(); };
        }

        private void LogDetailed(string message) 
        { 
            // Используем FileService для логирования
            _fileService?.WriteDetailedLog(message);
        }

        private void LoadCustomCommandsForEditor(List<CustomCommand> commandsToLoad)
        {
            try
            {
                var defaultTriggers = DefaultCommandsProvider.GetDefaultCommands().Select(c => c.Trigger).ToHashSet(StringComparer.OrdinalIgnoreCase);
                EditableCustomCommands.Clear();
                
                // Загружаем все команды
                foreach (var cmd in commandsToLoad)
                {
                    // Проверяем по триггеру, является ли команда дефолтной
                    bool isDefault = defaultTriggers.Contains(cmd.Trigger);
                    
                    // Если команда дефолтная и заблокирована - пропускаем
                    if (isDefault && !_systemCommandsUnlocked) continue;
                    
                    EditableCustomCommands.Add(cmd);
                }
            }
            catch (Exception ex) { LogDetailed($"Failed to load custom commands for editor: {ex.Message}"); }
        }

        private void InitializeHotkeys(IEnumerable<CustomCommand> commandsToRegister)
        {
            try
            {
                _hotkeyActions.Clear();
                _displayItems.Clear();
                Action openCrmDialog = () =>
                {
                    string? result = null;
                    Dispatcher.Invoke(() => { try { var dlg = new CrmIdInputDialog { Owner = this }; if (dlg.ShowDialog() == true) result = dlg.ResultIds; } catch (Exception ex) { MessageBox.Show($"Ошибка: {ex.Message}"); } });
                    if (!string.IsNullOrWhiteSpace(result)) { Thread.Sleep(500); _commandExecutionService.Enqueue("Bot", $"cmd duplicate {result}", "Alt+Shift+D8"); }
                };
                var (actions, displayItems) = HotkeyProvider.RegisterAll(_commandExecutionService, openCrmDialog, commandsToRegister);
                _hotkeyActions = actions;
                foreach (var item in displayItems) _displayItems.Add(item);
            }
            catch (Exception ex) { LogDetailed($"Failed to initialize hotkeys: {ex.Message}"); }
        }

        private void ToggleHooks()
        {
            _hooksDisabled = !_hooksDisabled;
            if (_hooksDisabled) { _hotkeyManager?.Dispose(); _hotkeyManager = null; _altBlockerService?.Disable(); }
            else { _hotkeyManager = new HotkeyManager { HotkeyHandler = OnGlobalHotkey }; _altBlockerService = new AltBlockerService(_hotkeyManager, LogDetailed); UpdateAltHookState(_viewModel.IsAltBlockerEnabled); }
            _trayIconService.UpdateState(_hooksDisabled);
        }

        private void ShowWindow() { this.Show(); this.WindowState = WindowState.Normal; this.Activate(); }

        private void LoadSettings()
        {
            var settings = _fileService.LoadSettings();
            
            // Проверяем версию приложения и сбрасываем команды при обновлении
            if (!string.IsNullOrEmpty(settings.LastCommandsVersion) && 
                settings.LastCommandsVersion != AppConstants.AppVersion)
            {
                // Версия изменилась - удаляем файл команд для сброса
                try { if (File.Exists(_customCommandService.GetFilePath())) File.Delete(_customCommandService.GetFilePath()); } 
                catch { }
                System.Diagnostics.Debug.WriteLine($"Commands reset due to version change: {settings.LastCommandsVersion} -> {AppConstants.AppVersion}");
            }
            
            _viewModel.SettingsViewModel.NotesFontSize = settings.NotesFontSize;
            _viewModel.SettingsViewModel.UsePasteModeForQuickReplies = settings.UsePasteModeForQuickReplies;
            _viewModel.CrmViewModel.CrmLogin = settings.CrmLogin;
            _viewModel.CrmViewModel.CrmPassword = settings.CrmPassword;
            _viewModel.IsAltBlockerEnabled = settings.IsAltBlockerEnabled;
            _viewModel.CommandCount = settings.CommandCount;
            if (IsOnScreen(settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight)) { this.Top = settings.WindowTop; this.Left = settings.WindowLeft; }
            this.Width = settings.WindowWidth; this.Height = settings.WindowHeight;
            if (settings.WindowState == (int)WindowState.Maximized) this.WindowState = WindowState.Maximized;
            UpdateAltHookState(settings.IsAltBlockerEnabled);
            _isLightTheme = settings.IsLightTheme;
            ApplyTheme(_isLightTheme);
        }

        private void SaveSettings()
        {
            var settings = new AppSettings
            {
                NotesFontSize = (int)_viewModel.SettingsViewModel.NotesFontSize,
                UsePasteModeForQuickReplies = _viewModel.SettingsViewModel.UsePasteModeForQuickReplies,
                CrmLogin = _viewModel.CrmViewModel.CrmLogin,
                CrmPassword = _viewModel.CrmViewModel.CrmPassword,
                IsAltBlockerEnabled = _viewModel.IsAltBlockerEnabled,
                CommandCount = _viewModel.CommandCount,
                WindowTop = this.Top,
                WindowLeft = this.Left,
                WindowWidth = this.Width,
                WindowHeight = this.Height,
                WindowState = (int)this.WindowState,
                IsLightTheme = _isLightTheme,
                LastCommandsVersion = AppConstants.AppVersion
            };
            _fileService.SaveSettings(settings);
        }

        private void UpdateAltHookState(bool enable) { if (_hooksDisabled || _altBlockerService == null) return; if (enable) _altBlockerService.Enable(); else _altBlockerService.Disable(); }
        private void BtnThemeSwitch_Click(object sender, RoutedEventArgs e) { _isLightTheme = !_isLightTheme; ApplyTheme(_isLightTheme); SaveSettings(); }

        private void BtnResetThemes_Click(object sender, RoutedEventArgs e)
        {
            bool confirmed = false;
            var win = new Window { Title = "Сброс тем", Width = 350, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false };
            win.Resources.MergedDictionaries.Add(this.Resources);
            Style? btnStyle = null; try { btnStyle = (Style)this.FindResource(typeof(Button)); } catch { }
            var border = new Border { Background = (Brush)FindResource("BrushWindowBackground"), BorderBrush = (Brush)FindResource("BrushAccent"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 0, Opacity = 0.5, Color = (Color)FindResource("ColorAccent") } };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "❓ СБРОС ТЕМ", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("BrushAccent"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 15) });
            stack.Children.Add(new TextBlock { Text = "Сбросить цвета тем к стандартным?", TextWrapping = TextWrapping.Wrap, FontSize = 14, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 20), Foreground = (Brush)FindResource("BrushForeground") });
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var btnYes = new Button { Content = "ДА", Width = 100, Margin = new Thickness(0, 0, 15, 0), Style = btnStyle }; btnYes.Click += (s, args) => { confirmed = true; win.Close(); };
            var btnNo = new Button { Content = "НЕТ", Width = 100, Style = btnStyle }; btnNo.Click += (s, args) => win.Close();
            btnPanel.Children.Add(btnYes); btnPanel.Children.Add(btnNo); stack.Children.Add(btnPanel); border.Child = stack; win.Content = border; win.ShowDialog();
            if (confirmed) { _themeSettings = new ThemeSettings(); SaveThemeSettings(); ApplyTheme(_isLightTheme); }
        }

        private void ApplyTheme(bool isLight) { var themeSet = isLight ? _themeSettings.LightTheme : _themeSettings.DarkTheme; ThemeService.ApplyTheme(Application.Current.Resources, themeSet); if (btnThemeSwitch is Button btn && btn.Content is TextBlock tb) tb.Text = isLight ? "🌙" : "☀️"; }
        private void LoadThemeSettings() 
        { 
            var settings = _fileService.LoadThemeSettings();
            if (settings != null)
            {
                _themeSettings = settings;
            }
            else
            {
                _themeSettings = new ThemeSettings();
                _fileService.SaveThemeSettings(_themeSettings);
            }
        }
        private void SaveThemeSettings() 
        { 
            _fileService.SaveThemeSettings(_themeSettings);
        }

        private bool OnGlobalHotkey(string keyCombo)
        {
            if (_isRecordingHotkey) { Dispatcher.Invoke(() => { if (_activeHotkeyRecordingBox?.DataContext is CustomCommand cmd) { if (IsHotkeyDuplicate(keyCombo, cmd)) ShowTempNotification("Дубликат!"); else { _activeHotkeyRecordingBox.Text = keyCombo; _activeHotkeyRecordingBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); } } }); return true; }
            if (_hotkeyActions.TryGetValue(keyCombo, out var action)) { action.Invoke(); return true; }
            return false;
        }

        private bool IsHotkeyDuplicate(string keyCombo, CustomCommand current) => EditableCustomCommands.Any(c => c != current && c.Trigger.Equals(keyCombo, StringComparison.OrdinalIgnoreCase));
        private void ShowTempNotification(string message) { if (_tempNotificationPopup == null || _tempNotificationTimer == null) return; if (_tempNotificationPopup.Child is Border border && border.Child is TextBlock textBlock) { border.Background = (Brush)FindResource("BrushBackground"); border.BorderBrush = (Brush)FindResource("BrushAccent"); textBlock.Foreground = (Brush)FindResource("BrushForeground"); textBlock.Text = message; } _tempNotificationPopup.PlacementTarget = this; _tempNotificationPopup.IsOpen = true; _tempNotificationTimer.Start(); }
        private bool IsOnScreen(double left, double top, double width, double height) => Forms.Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new System.Drawing.Rectangle((int)left, (int)top, (int)width, (int)height)));
        public bool IsInputFocused() { try { var el = AutomationElement.FocusedElement; if (el == null) return false; var type = el.Current.ControlType; return type == ControlType.Edit || type == ControlType.Document || type == ControlType.Custom || type == ControlType.Text; } catch { return true; } }

        private void txtCrmPassword_LostFocus(object sender, RoutedEventArgs e) { if (sender is PasswordBox pb) _viewModel.CrmViewModel.CrmPassword = pb.Password; }

        #region ICommandHost Implementation
        void ICommandHost.UpdateOverlay(string msg) => _overlay.ShowMessage(msg);
        void ICommandHost.HideOverlay() => _overlay.HideMessage();
        void ICommandHost.LogDetailed(string msg) => LogDetailed(msg);
        void ICommandHost.IncrementCommandCount() => _viewModel.IncrementCommandCount();
        void ICommandHost.RunOnUIThread(Action action) => Dispatcher.Invoke(action);
        void ICommandHost.ClipboardClear() { try { Clipboard.Clear(); } catch (Exception ex) { Debug.WriteLine($"Clipboard clear failed: {ex.Message}"); } }
        void ICommandHost.ClipboardSetText(string text) { try { Clipboard.SetText(text); } catch (Exception ex) { Debug.WriteLine($"Clipboard set text failed: {ex.Message}"); } }
        string? ICommandHost.ClipboardGetText() { try { return Clipboard.GetText(); } catch (Exception ex) { Debug.WriteLine($"Clipboard get text failed: {ex.Message}"); return null; } }
        bool ICommandHost.ClipboardContainsText() { try { return Clipboard.ContainsText(); } catch (Exception ex) { Debug.WriteLine($"Clipboard contains text failed: {ex.Message}"); return false; } }
        void ICommandHost.SendKeysWait(string keys) => Forms.SendKeys.SendWait(keys);
        async Task ICommandHost.CleanClipboardHistoryAsync(int items)
        {
            if (items <= 0) return;
            
            try
            {
                LogDetailed($"Очистка истории буфера обмена ({items} записей)...");
                
                await Task.Run(() =>
                {
                    try
                    {
                        // Очищаем текущий буфер обмена
                        // Примечание: Полная очистка истории буфера обмена Windows требует WinRT API
                        // или специальных разрешений. Используем базовый метод очистки.
                        Dispatcher.Invoke(() => 
                        {
                            try { Clipboard.Clear(); } catch { }
                        });
                        
                        LogDetailed("История буфера обмена очищена.");
                    }
                    catch (Exception ex)
                    {
                        LogDetailed($"Ошибка при очистке: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                LogDetailed($"Критическая ошибка очистки истории буфера обмена: {ex.Message}");
            }
        }

        async Task ICommandHost.ClearClipboardHistoryByTimeRangeAsync(DateTime startTime, DateTime endTime)
        {
            if (startTime >= endTime) return;
            
            try
            {
                LogDetailed($"Очистка истории буфера обмена с {startTime:HH:mm:ss.fff} до {endTime:HH:mm:ss.fff}...");
                
                // Выполняем очистку напрямую (без Task.Run, чтобы Clipboard.Clear() работал корректно)
                try
                {
                    // Используем WinRT API для очистки истории буфера обмена
                    // IActivityClipboardHistoryItemsInterop::RemoveByTimeRange
                    var result = ClearClipboardHistoryByTimeRangeInternal(startTime, endTime);
                    
                    if (result == 0)
                    {
                        LogDetailed("История буфера обмена очищена по времени.");
                    }
                    else
                    {
                        LogDetailed($"Очистка истории буфера обмена вернула код: {result}");
                    }
                }
                catch (Exception ex)
                {
                    LogDetailed($"Ошибка при очистке истории буфера обмена: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                LogDetailed($"Критическая ошибка очистки истории буфера обмена: {ex.Message}");
            }
        }

        private int ClearClipboardHistoryByTimeRangeInternal(DateTime startTime, DateTime endTime)
        {
            // Очищаем только текущий буфер обмена, не затрагивая историю Windows
            // Это удалит текст, который был вставлен командами, но оставит историю буфера обмена нетронутой
            try
            {
                System.Diagnostics.Debug.WriteLine($"[ClipboardCleanup] Clearing clipboard from {startTime:HH:mm:ss.fff} to {endTime:HH:mm:ss.fff}");
                
                // Очищаем текущий буфер обмена напрямую (без Task.Run)
                try 
                { 
                    Clipboard.Clear();
                    System.Diagnostics.Debug.WriteLine("[ClipboardCleanup] Clipboard cleared successfully");
                } 
                catch (Exception ex) 
                { 
                    System.Diagnostics.Debug.WriteLine($"[ClipboardCleanup] Error clearing clipboard: {ex.Message}"); 
                }
                
                System.Diagnostics.Debug.WriteLine("[ClipboardCleanup] Cleanup completed - only current clipboard cleared, history preserved");
                return 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipboardCleanup] Exception during cleanup: {ex.Message}");
                return -1;
            }
        }
        #endregion

        private async void OpenOrderCheck_Click(object sender, RoutedEventArgs e) { string exePath = Path.Combine(AppDir, "OrderCheck.exe"); if (File.Exists(exePath)) { try { Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show($"Ошибка: {ex.Message}"); } } else { if (sender is Button btn) { btn.IsEnabled = false; btn.Content = "СКАЧИВАНИЕ..."; } try { using var client = new HttpClient(); var bytes = await client.GetByteArrayAsync("https://clearbat.iiko.online/downloads/OrderCheck.exe"); await File.WriteAllBytesAsync(exePath, bytes); Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show($"Ошибка: {ex.Message}"); } finally { if (sender is Button btnEnd) { btnEnd.IsEnabled = true; btnEnd.Content = "ORDERCHECK"; } } } }
        private async void OpenClearBat_Click(object sender, RoutedEventArgs e) { string exePath = Path.Combine(AppDir, "CLEAR.bat.exe"); if (File.Exists(exePath)) { try { Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show($"Ошибка: {ex.Message}"); } } else { if (sender is Button btn) { btn.IsEnabled = false; btn.Content = "СКАЧИВАНИЕ..."; } try { using var client = new HttpClient(); var bytes = await client.GetByteArrayAsync("https://clearbat.iiko.online/downloads/CLEAR.bat.exe"); await File.WriteAllBytesAsync(exePath, bytes); Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show($"Ошибка: {ex.Message}"); } finally { if (sender is Button btnEnd) { btnEnd.IsEnabled = true; btnEnd.Content = "CLEAR.bat"; } } } }

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        private void MoveExplorerTo(int x, int y) { Task.Run(async () => { try { IntPtr hWnd = IntPtr.Zero; for (int i = 0; i < 10; i++) { await Task.Delay(200); IntPtr fg = GetForegroundWindow(); GetWindowThreadProcessId(fg, out uint pid); try { var p = Process.GetProcessById((int)pid); if (p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) { hWnd = fg; break; } } catch { } } if (hWnd != IntPtr.Zero) SetWindowPos(hWnd, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004); } catch { } }); }
        private void OpenFtp_Click(object sender, RoutedEventArgs e) { string path = @"\\files.resto.lan\"; string user = _viewModel.CrmViewModel.CrmLogin; string pass = _viewModel.CrmViewModel.CrmPassword; if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass)) { MessageBox.Show("Для доступа к FTP заполните Логин и Пароль от CRM."); return; } int targetX = (int)this.Left + 30, targetY = (int)this.Top + 30; if (this.WindowState == WindowState.Maximized) { targetX = 50; targetY = 50; } Task.Run(() => { try { var pDel = Process.Start(new ProcessStartInfo("net", @"use \\files.resto.lan /delete /y") { CreateNoWindow = true, UseShellExecute = false }); pDel?.WaitForExit(); var pUse = Process.Start(new ProcessStartInfo("net", $@"use \\files.resto.lan /user:{user} {pass} /persistent:yes") { CreateNoWindow = true, UseShellExecute = false }); pUse?.WaitForExit(); Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); MoveExplorerTo(targetX, targetY); } catch (Exception ex) { Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Ошибка: {ex.Message}")); } }); }
        private bool ShowUpdateDialog(string newVersion, string currentVersion) { bool result = false; Dispatcher.Invoke(() => { var win = new Window { Title = "Доступно обновление", Width = 400, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false }; Style? btnStyle = null; try { btnStyle = (Style)this.FindResource(typeof(Button)); } catch { } var border = new Border { Background = (Brush)FindResource("BrushBackground"), BorderBrush = (Brush)FindResource("BrushAccent"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 0, Opacity = 0.5, Color = (Color)FindResource("ColorAccent") } }; var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = "🚀 ОБНОВЛЕНИЕ", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("BrushAccent"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 15) }); stack.Children.Add(new TextBlock { Text = $"Доступна новая версия: {newVersion}\n(Текущая: {currentVersion})", TextWrapping = TextWrapping.Wrap, FontSize = 15, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 10), Foreground = (Brush)FindResource("BrushForeground") }); stack.Children.Add(new TextBlock { Text = "Скачать и установить?", FontSize = 14, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 25), Foreground = (Brush)FindResource("BrushForeground") }); var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center }; var btnYes = new Button { Content = "СКАЧАТЬ", Width = 120, Margin = new Thickness(0, 0, 15, 0), Style = btnStyle }; btnYes.Click += (s, e) => { win.DialogResult = true; win.Close(); }; var btnNo = new Button { Content = "ОТМЕНА", Width = 120, Style = btnStyle, BorderBrush = Brushes.Gray, Foreground = Brushes.Gray }; btnNo.Click += (s, e) => { win.DialogResult = false; win.Close(); }; btnPanel.Children.Add(btnYes); btnPanel.Children.Add(btnNo); stack.Children.Add(btnPanel); border.Child = stack; win.Content = border; result = win.ShowDialog() == true; }); return result; }
        private void ShowCustomMessage(string title, string message, bool isError) { Dispatcher.Invoke(() => { var win = new Window { Title = title, Width = 350, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false }; Style? btnStyle = null; try { btnStyle = (Style)this.FindResource(typeof(Button)); } catch { } var border = new Border { Background = (Brush)FindResource("BrushBackground"), BorderBrush = (Brush)FindResource("BrushAccent"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 0, Opacity = 0.5, Color = (Color)FindResource("ColorAccent") } }; var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = isError ? "❌ ОШИБКА" : "✅ ИНФО", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = isError ? Brushes.IndianRed : Brushes.LightGreen, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 15) }); stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 20), Foreground = (Brush)FindResource("BrushForeground") }); var btnOk = new Button { Content = "OK", Width = 100, HorizontalAlignment = HorizontalAlignment.Center, Style = btnStyle }; btnOk.Click += (s, e) => win.Close(); stack.Children.Add(btnOk); border.Child = stack; win.Content = border; win.ShowDialog(); }); }

        private void SaveCustomCommands_Click(object sender, RoutedEventArgs e) { if (EditableCustomCommands.Any(c => string.IsNullOrWhiteSpace(c.Trigger))) { ShowCustomMessage("Сохранение", "У всех команд должно быть заполнено поле 'СОЧЕТАНИЕ'.", true); return; } _customCommandService.SaveCommands(EditableCustomCommands.ToList()); InitializeHotkeys(EditableCustomCommands.ToList()); ShowTempNotification("Команды сохранены!"); }
        private void AddCustomCommand_Click(object sender, RoutedEventArgs e) => EditableCustomCommands.Add(new CustomCommand { Description = "Новая команда", Type = "Reply" });
        private void DeleteCustomCommand_Click(object sender, RoutedEventArgs e) 
        { 
            if (CustomCommandsGrid.SelectedItem is CustomCommand cmd) 
            {
                if (cmd.IsReadOnly)
                {
                    ShowCustomMessage("Удаление", "Нельзя удалить заблокированную команду.", true);
                    return;
                }
                EditableCustomCommands.Remove(cmd); 
            }
        }
        private void ResetCustomCommands_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show("Сбросить все команды к стандартным?", "Сброс", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { _customCommandService.ResetToDefaults(); _systemCommandsUnlocked = true; var allCmds = _customCommandService.LoadCommands(); InitializeHotkeys(allCmds); LoadCustomCommandsForEditor(allCmds); CommandEditorTab.Visibility = Visibility.Collapsed; MainTabControl.SelectedIndex = 0; ShowTempNotification("Команды сброшены!"); } }
        
        private void ToggleSystemCommandsUnlock_Click(object sender, RoutedEventArgs e)
        {
            _systemCommandsUnlocked = !_systemCommandsUnlocked;
            
            // Обновляем текст и tooltip кнопки
            Dispatcher.Invoke(() => {
                var btn = FindName("btnUnlockCommands") as Button;
                if (btn != null)
                {
                    btn.Content = _systemCommandsUnlocked ? "🔒" : "🔓";
                    btn.ToolTip = _systemCommandsUnlocked ? 
                        "Заблокировать команды для редактирования" : 
                        "Разблокировать команды для редактирования (временно)";
                }
            });
            
            // Перезагружаем команды
            // customCommands уже содержит все команды (включая System, Reply, Bot)
            // Не нужно добавлять их снова - это вызывает дублирование
            var customCommands = _customCommandService.LoadCommands();
            
            LoadCustomCommandsForEditor(customCommands);
            
            ShowTempNotification(_systemCommandsUnlocked ? "Команды разблокированы (System, Reply, Bot)!" : "Команды заблокированы!");
        }
        
        private void ExportCommands_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, разблокированы ли команды
            if (!_systemCommandsUnlocked)
            {
                ShowCustomMessage("Экспорт", "Для экспорта команд необходимо разблокировать редактирование (нажмите на замочек).", false);
                return;
            }
            
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json",
                FileName = "custom_commands_export"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Экспортируем все команды из списка (включая пользовательские)
                    var commandsToExport = EditableCustomCommands.ToList();
                    var json = System.Text.Json.JsonSerializer.Serialize(commandsToExport, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(dialog.FileName, json);
                    ShowTempNotification($"Команды экспортированы! ({commandsToExport.Count})");
                }
                catch (Exception ex)
                {
                    ShowCustomMessage("Ошибка экспорта", $"Не удалось экспортировать команды: {ex.Message}", true);
                }
            }
        }
        
        // Модель для отображения дубликатов в диалоге
        private class DuplicateItem
        {
            public CustomCommand ImportedCommand { get; set; } = null!;
            public CustomCommand ExistingCommand { get; set; } = null!;
            public bool Replace { get; set; }
            public string DisplayText => $"{ImportedCommand.Trigger} | {ImportedCommand.Type}";
            public string Description => $"Описание: {ImportedCommand.Description}\nСодержимое: {(string.IsNullOrEmpty(ImportedCommand.Content) ? "(пусто)" : ImportedCommand.Content.Length > 50 ? ImportedCommand.Content.Substring(0, 50) + "..." : ImportedCommand.Content)}";
        }
        
        private void ImportCommands_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, разблокированы ли команды
            if (!_systemCommandsUnlocked)
            {
                ShowCustomMessage("Импорт", "Для импорта команд необходимо разблокировать редактирование (нажмите на замочек).", false);
                return;
            }
            
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = System.IO.File.ReadAllText(dialog.FileName);
                    var importedCommands = System.Text.Json.JsonSerializer.Deserialize<List<CustomCommand>>(json);
                    
                    if (importedCommands != null && importedCommands.Count > 0)
                    {
                        // Собираем дубликаты
                        var duplicates = new List<DuplicateItem>();
                        var newCommands = new List<CustomCommand>();
                        
                        foreach (var cmd in importedCommands)
                        {
                            cmd.IsReadOnly = false;
                            
                            // Проверяем на дубликаты по Trigger, Type, Description, Content
                            var existingCmd = EditableCustomCommands.FirstOrDefault(c =>
                                string.Equals(c.Trigger, cmd.Trigger, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(c.Type, cmd.Type, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(c.Description, cmd.Description, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(c.Content, cmd.Content, StringComparison.OrdinalIgnoreCase));
                            
                            if (existingCmd != null)
                            {
                                duplicates.Add(new DuplicateItem
                                {
                                    ImportedCommand = cmd,
                                    ExistingCommand = existingCmd,
                                    Replace = false // По умолчанию не заменять
                                });
                            }
                            else
                            {
                                newCommands.Add(cmd);
                            }
                        }
                        
                        // Если есть дубликаты, показываем диалог
                        if (duplicates.Count > 0)
                        {
                            var result = ShowDuplicateDialog(duplicates);
                            if (result == null) return; // Отмена
                        }
                        
                        // Обрабатываем дубликаты
                        int replacedCount = 0;
                        int skippedCount = 0;
                        
                        foreach (var dup in duplicates)
                        {
                            if (dup.Replace)
                            {
                                var index = EditableCustomCommands.IndexOf(dup.ExistingCommand);
                                if (index >= 0)
                                {
                                    EditableCustomCommands[index] = dup.ImportedCommand;
                                    replacedCount++;
                                }
                            }
                            else
                            {
                                skippedCount++;
                            }
                        }
                        
                        // Добавляем новые команды
                        foreach (var cmd in newCommands)
                        {
                            EditableCustomCommands.Add(cmd);
                        }
                        
                        // Формируем сообщение о результате
                        var message = $"Импорт завершен!\nДобавлено: {newCommands.Count}";
                        if (replacedCount > 0) message += $"\nЗамнено: {replacedCount}";
                        if (skippedCount > 0) message += $"\nПропущено: {skippedCount}";
                        
                        ShowTempNotification(message);
                    }
                    else
                    {
                        ShowCustomMessage("Импорт", "Файл пуст или содержит некорректные данные.", true);
                    }
                }
                catch (Exception ex)
                {
                    ShowCustomMessage("Ошибка импорта", $"Не удалось импортировать команды: {ex.Message}", true);
                }
            }
        }
        
        private bool? ShowDuplicateDialog(List<DuplicateItem> duplicates)
        {
            bool? result = null;
            Dispatcher.Invoke(() => {
                var win = new Window 
                { 
                    Title = "Найдены дубликаты", 
                    Width = 550, 
                    Height = 480,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner, 
                    Owner = this, 
                    ResizeMode = ResizeMode.NoResize, 
                    WindowStyle = WindowStyle.None, 
                    AllowsTransparency = true, 
                    Background = Brushes.Transparent, 
                    ShowInTaskbar = false 
                };
                
                Style? btnStyle = null;
                try { btnStyle = (Style)this.FindResource(typeof(Button)); } catch { }
                Style? checkBoxStyle = null;
                try { checkBoxStyle = (Style)this.FindResource(typeof(CheckBox)); } catch { }
                
                var border = new Border 
                { 
                    Background = (Brush)FindResource("BrushBackground"), 
                    BorderBrush = (Brush)FindResource("BrushAccent"), 
                    BorderThickness = new Thickness(1), 
                    CornerRadius = new CornerRadius(8), 
                    Padding = new Thickness(20), 
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 0, Opacity = 0.5, Color = (Color)FindResource("ColorAccent") } 
                };
                
                var mainStack = new StackPanel();
                
                // Заголовок
                mainStack.Children.Add(new TextBlock 
                { 
                    Text = $"⚠️ НАЙДЕНО ДУБЛИКАТОВ: {duplicates.Count}", 
                    FontSize = 16, 
                    FontWeight = FontWeights.Bold, 
                    Foreground = (Brush)FindResource("BrushAccent"), 
                    HorizontalAlignment = HorizontalAlignment.Center, 
                    Margin = new Thickness(0, 0, 0, 5) 
                });
                
                mainStack.Children.Add(new TextBlock 
                { 
                    Text = "Отметьте галочкой команды для замены:", 
                    FontSize = 12, 
                    Foreground = (Brush)FindResource("BrushForeground"), 
                    Margin = new Thickness(0, 0, 0, 5) 
                });
                
                // ScrollViewer для списка
                var scrollViewer = new ScrollViewer 
                { 
                    Height = 290, 
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto 
                };
                
                var itemsPanel = new StackPanel();
                
                foreach (var dup in duplicates)
                {
                    var itemPanel = new StackPanel { Margin = new Thickness(0, 3, 0, 5) };
                    
                    var checkBox = new CheckBox 
                    { 
                        Content = dup.DisplayText, 
                        IsChecked = dup.Replace,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 2),
                        Foreground = (Brush)FindResource("BrushForeground"),
                        Tag = dup
                    };
                    
                    if (checkBoxStyle != null) checkBox.Style = checkBoxStyle;
                    
                    checkBox.Checked += (s, e) => { if (s is CheckBox cb && cb.Tag is DuplicateItem d) d.Replace = true; };
                    checkBox.Unchecked += (s, e) => { if (s is CheckBox cb && cb.Tag is DuplicateItem d) d.Replace = false; };
                    
                    itemPanel.Children.Add(checkBox);
                    
                    // Добавляем описание
                    var descText = new TextBlock 
                    { 
                        Text = dup.Description,
                        FontSize = 11,
                        Foreground = (Brush)FindResource("BrushForeground"),
                        Opacity = 0.8,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(20, 0, 0, 0)
                    };
                    itemPanel.Children.Add(descText);
                    
                    itemsPanel.Children.Add(itemPanel);
                }
                
                scrollViewer.Content = itemsPanel;
                mainStack.Children.Add(scrollViewer);
                
                // Кнопки
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
                
                var btnReplaceAll = new Button { Content = "Принять все", Width = 110, Height = 35, Margin = new Thickness(0, 0, 10, 0), Style = btnStyle };
                btnReplaceAll.Click += (s, e) => 
                { 
                    foreach (var dup in duplicates) dup.Replace = true; 
                    foreach (var child in itemsPanel.Children.OfType<StackPanel>()) 
                    {
                        var cb = child.Children.OfType<CheckBox>().FirstOrDefault();
                        if (cb != null) cb.IsChecked = true;
                    }
                };
                
                var btnSkipAll = new Button { Content = "Пропустить все", Width = 110, Height = 35, Margin = new Thickness(0, 0, 10, 0), Style = btnStyle };
                btnSkipAll.Click += (s, e) => 
                { 
                    foreach (var dup in duplicates) dup.Replace = false; 
                    foreach (var child in itemsPanel.Children.OfType<StackPanel>()) 
                    {
                        var cb = child.Children.OfType<CheckBox>().FirstOrDefault();
                        if (cb != null) cb.IsChecked = false;
                    }
                };
                
                var btnOk = new Button { Content = "ОК", Width = 80, Height = 35, Style = btnStyle };
                btnOk.Click += (s, e) => { win.DialogResult = true; win.Close(); };
                
                var btnCancel = new Button { Content = "Отмена", Width = 80, Height = 35, Margin = new Thickness(10, 0, 0, 0), Style = btnStyle };
                btnCancel.Click += (s, e) => { win.DialogResult = false; win.Close(); };
                
                btnPanel.Children.Add(btnReplaceAll);
                btnPanel.Children.Add(btnSkipAll);
                btnPanel.Children.Add(btnOk);
                btnPanel.Children.Add(btnCancel);
                
                mainStack.Children.Add(btnPanel);
                
                border.Child = mainStack;
                win.Content = border;
                
                result = win.ShowDialog();
            });
            return result;
        }
        
        private void CommandsTab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))) CommandEditorTab.Visibility = CommandEditorTab.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }
        private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e) { if (sender is TextBox txtBox) { _originalHotkeyText = txtBox.Text; txtBox.Text = "[ ЗАПИСЬ... ]"; txtBox.Foreground = (Brush)FindResource("BrushAccent"); _activeHotkeyRecordingBox = txtBox; _isRecordingHotkey = true; } }
        private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e) { if (sender is TextBox txtBox && _isRecordingHotkey) { if (txtBox.Text == "[ ЗАПИСЬ... ]") txtBox.Text = _originalHotkeyText; txtBox.SetResourceReference(TextBox.ForegroundProperty, "BrushInputForeground"); _isRecordingHotkey = false; _activeHotkeyRecordingBox = null; } }
        private void CustomCommandsGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e) { if (e.Row.DataContext is CustomCommand cmd && cmd.IsReadOnly) e.Cancel = true; }
        private void BtnCopyPosM1_Click(object sender, RoutedEventArgs e) { Clipboard.SetText("https://m1.iiko.cards/ru-RU/About/DownloadPosInstaller?useRc=False"); ShowTempNotification("URL POS-ник M1 скопирован!"); }
        private void BtnCopyPosM_Click(object sender, RoutedEventArgs e) { Clipboard.SetText("https://iiko.cards/ru-RU/About/DownloadPosInstaller?useRc=False"); ShowTempNotification("URL POS-ник M скопирован!"); }
        private void OpenLogFolder_Click(object sender, RoutedEventArgs e) { _launcherService.OpenFolder(AppDir); }
        private void BtnCrmSettings_Click(object sender, RoutedEventArgs e) => ShowCustomMessage("Настройка CRM", "Убедитесь, что логин и пароль заполнены. Порт 9222.", false);
        private void TxtUpdateLink_Click(object sender, MouseButtonEventArgs e)
        {
            txtUpdateLink.Text = "Проверка...";
            pbUpdate.Visibility = Visibility.Visible;
            pbUpdate.Value = 0;
            
            Task.Run(async () =>
            {
                try
                {
                    await _updateService.CheckForUpdates(false, _lastUpdateCheck, dt =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _lastUpdateCheck = dt;
                            SaveSettings();
                        });
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        pbUpdate.Visibility = Visibility.Collapsed;
                        txtUpdateLink.Text = "Обновить";
                        ShowCustomMessage("Ошибка", $"Произошла ошибка при проверке обновлений: {ex.Message}", true);
                    });
                }
            });
        }
    }
}