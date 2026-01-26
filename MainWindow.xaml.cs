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
using System.Windows.Threading;
using Forms = System.Windows.Forms; 
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Net.WebSockets;
using System.Text;
using System.Net;
using System.Windows.Automation;

namespace iikoServiceHelper
{
    public partial class MainWindow : Window
    {
        private const string AppName = "iikoServiceHelper_v2";
        private readonly string AppDir;
        private readonly string NotesFile;
        private readonly string SettingsFile;
        private readonly string DetailedLogFile;
        private readonly object _logLock = new();

        private Forms.NotifyIcon? _trayIcon;
        private HotkeyManager _hotkeyManager;
        private OverlayWindow _overlay;
        private Dictionary<string, Action> _hotkeyActions = new(StringComparer.OrdinalIgnoreCase);
        private ObservableCollection<HotkeyDisplay> _displayItems = new();
        
        private bool _isPaused = false;
        private int _commandCount = 0;
        private Forms.ToolStripMenuItem? _pauseMenuItem;

        private readonly Queue<(Action Act, string Name)> _commandQueue = new();
        private readonly object _queueLock = new();
        private bool _isQueueRunning = false;
        private string _currentActionName = "";

        private DispatcherTimer _crmTimer;
        private bool _isCrmActive = false;
        private Process? _browserProcess; // Храним ссылку на процесс браузера

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

            // Init Logic
            InitializeHotkeys();
            LoadNotes();
            LoadSettings();
            InitializeTray();
            _overlay = new OverlayWindow();
            
            // Set bindings
            ReferenceGrid.ItemsSource = _displayItems;

            // Setup Global Hooks
            _hotkeyManager = new HotkeyManager();
            _hotkeyManager.HotkeyHandler = OnGlobalHotkey;

            // CRM Timer
            _crmTimer = new DispatcherTimer();
            _crmTimer.Interval = TimeSpan.FromMinutes(30);
            _crmTimer.Tick += CrmTimer_Tick;

            // Handle close
            this.Closing += (s, e) => 
            {
                SaveNotes();
                SaveSettings();
                if (_trayIcon != null) _trayIcon.Visible = false;
                _hotkeyManager.Dispose();
                System.Windows.Application.Current.Shutdown();
            };
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
            
            var groupedCmds = new Dictionary<string, List<string>>();
            var descOrder = new List<string>();

            // Helper to register keys
            void Reg(string keys, string desc, Action action)
            {
                _hotkeyActions[keys] = action;
                if (!groupedCmds.ContainsKey(desc))
                {
                    groupedCmds[desc] = new List<string>();
                    descOrder.Add(desc);
                }
                groupedCmds[desc].Add(keys);
            }

            // --- BOT COMMANDS ---
            // Logic: Type @chat_bot -> Enter -> Type Command -> Enter
            
            Action<string?> botCmd = (cmd) => ExecuteBotCommand(cmd);

            // Special handler for @chat_bot call with double Enter
            Action botCall = () => 
            {
                ExecuteBotCommand(null);
                Thread.Sleep(100);
                NativeMethods.ReleaseModifiers();
                NativeMethods.SendKey(NativeMethods.VK_RETURN);
                if (_hotkeyManager.IsAltPhysicallyDown)
                {
                    NativeMethods.PressAltDown();
                }
            };

            Reg("Alt+D0", "@chat_bot (Вызов)", botCall);
            Reg("Alt+C",  "@chat_bot (Вызов)", botCall);
            
            Reg("Alt+D1", "cmd newtask", () => botCmd("cmd newtask"));
            Reg("Alt+D2", "cmd add crmid", () => botCmd("cmd add crmid"));
            Reg("Alt+D3", "cmd add user", () => botCmd("cmd add user"));
            Reg("Alt+D4", "cmd remove crmid", () => botCmd("cmd remove crmid"));
            Reg("Alt+D5", "cmd forcing", () => botCmd("cmd forcing"));
            Reg("Alt+D6", "cmd timer set 6", () => botCmd("cmd timer set 6"));
            Reg("Alt+Shift+D6", "cmd timer dismiss", () => botCmd("cmd timer dismiss"));
            
            // Dynamic Date
            Reg("Alt+D7", "cmd timer delay", () => botCmd($"cmd timer delay {DateTime.Now:dd.MM.yyyy HH:mm}"));
            
            Action openCrmDialog = () => 
            {
                string? result = null;
                Application.Current.Dispatcher.Invoke(() => 
                {
                    try
                    {
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
                    botCmd($"cmd duplicate {result}");
                }
            };

            Reg("Alt+D8", "cmd duplicate", () => botCmd("cmd duplicate"));
            Reg("Alt+Shift+D8", "cmd duplicate (список)", openCrmDialog);
            Reg("Alt+D9", "cmd request info", () => botCmd("cmd request info"));

            // --- QUICK REPLIES ---
            // Logic: Type Text -> Enter
            
            Action<string> reply = (text) => ExecuteReply(text);

            Reg("Alt+NumPad1", "Добрый день!", () => reply("Добрый день!"));
            Reg("Alt+L",    "Добрый день!", () => reply("Добрый день!"));

            Reg("Alt+NumPad2", "У Вас остались вопросы по данному обращению?", () => reply("У Вас остались вопросы по данному обращению?"));
            Reg("Alt+D",    "У Вас остались вопросы по данному обращению?", () => reply("У Вас остались вопросы по данному обращению?"));

            Reg("Alt+NumPad3", "Ожидайте от нас обратную связь.", () => reply("Ожидайте от нас обратную связь."));
            Reg("Alt+J",    "Ожидайте от нас обратную связь.", () => reply("Ожидайте от нас обратную связь."));

            Reg("Alt+NumPad4", "Заявку закрываем, нет ОС.", () => reply("Заявку закрываем, так как не получили от Вас обратную связь."));
            Reg("Alt+P",    "Заявку закрываем, нет ОС.", () => reply("Заявку закрываем, так как не получили от Вас обратную связь."));

            Reg("Alt+NumPad5", "Ваша заявка передана специалисту.", () => reply("Ваша заявка передана специалисту.\nОтветственный специалист свяжется с Вами в ближайшее время."));
            Reg("Alt+G",    "Ваша заявка передана специалисту.", () => reply("Ваша заявка передана специалисту.\nОтветственный специалист свяжется с Вами в ближайшее время."));

            Reg("Alt+NumPad6", "Не удалось связаться с Вами по номеру:", () => reply("Не удалось связаться с Вами по номеру:\nПодскажите, когда с Вами можно будет связаться?"));
            Reg("Alt+H",    "Не удалось связаться с Вами по номеру:", () => reply("Не удалось связаться с Вами по номеру:\nПодскажите, когда с Вами можно будет связаться?"));

            Reg("Alt+NumPad7", "Организация определилась верно: ?", () => reply("Организация определилась верно: ?"));
            Reg("Alt+E",    "Организация определилась верно: ?", () => reply("Организация определилась верно: ?"));

            Reg("Alt+NumPad8", "Ваше обращение взято в работу.", () => reply("Ваше обращение взято в работу."));
            Reg("Alt+M",    "Ваше обращение взято в работу.", () => reply("Ваше обращение взято в работу."));

            Reg("Alt+NumPad9", "Подскажите пожалуйста Ваш контактный номер телефона.", () => reply("Подскажите пожалуйста Ваш контактный номер телефона.\nЭто необходимо для регистрации Вашего обращения."));
            Reg("Alt+N",    "Подскажите пожалуйста Ваш контактный номер телефона.", () => reply("Подскажите пожалуйста Ваш контактный номер телефона.\nЭто необходимо для регистрации Вашего обращения."));

            Reg("Alt+Multiply", "Уточняем информацию по Вашему вопросу.", () => reply("Уточняем информацию по Вашему вопросу."));
            Reg("Alt+X",    "Уточняем информацию по Вашему вопросу.", () => reply("Уточняем информацию по Вашему вопросу."));

            Reg("Alt+Add", "Чем могу Вам помочь?", () => reply("Чем могу Вам помочь?"));
            Reg("Alt+F",    "Чем могу Вам помочь?", () => reply("Чем могу Вам помочь?"));

            Reg("Alt+Z", "Закрываем (выполнена)", () => reply("Заявку закрываем как выполненную.\nСпасибо за обращение в iikoService и хорошего Вам дня.\nЕсли возникнут трудности или дополнительные вопросы, просим обратиться к нам повторно."));

            Reg("Alt+Shift+Z", "От вас не поступила обратная связь.", () => reply("От вас не поступила обратная связь.\nСпасибо за обращение в iikoService и хорошего Вам дня.\nЕсли возникнут трудности или дополнительные вопросы, просим обратиться к нам повторно.\nЗаявку закрываем."));

            Reg("Alt+B", "Закрываем (нет вопросов)", () => reply("В связи с тем, что дополнительных вопросов от вас не поступало, данное обращение закрываем.\nЕсли у вас остались вопросы, при создании новой заявки, просим указать номер текущей.\nСпасибо за обращение в iikoService и хорошего Вам дня!"));

            Reg("Alt+Divide", "Сообщить о платных работах", () => reply("Добрый день, вы обратились в техническую поддержку iikoService.  \nК сожалению, с Вашей организацией не заключен договор технической поддержки.\nРаботы могут быть выполнены только на платной основе.\n\nСтоимость работ: руб.\nВы согласны на платные работы?"));
            
            Reg("Alt+Q", "Очистить очередь", ClearCommandQueue);
            
            foreach (var desc in descOrder)
            {
                var formattedKeys = groupedCmds[desc].Select(FormatKeyCombo);

                _displayItems.Add(new HotkeyDisplay 
                { 
                    Keys = string.Join(" / ", formattedKeys), 
                    Desc = desc 
                });
            }
        }

        private void InitializeTray()
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
            contextMenu.Items.Add("Развернуть", null, (s, e) => { ShowWindow(); });
            
            _pauseMenuItem = new Forms.ToolStripMenuItem("Приостановить", null, (s, e) => TogglePause());
            contextMenu.Items.Add(_pauseMenuItem);
            
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Выход", null, (s, e) => { System.Windows.Application.Current.Shutdown(); });
            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => ShowWindow();
        }

        private void TogglePause()
        {
            _isPaused = !_isPaused;
            if (_pauseMenuItem != null) _pauseMenuItem.Text = _isPaused ? "Возобновить" : "Приостановить";
            if (_trayIcon != null) _trayIcon.Text = _isPaused ? "iikoServiceHelper_v2 (Paused)" : "iikoServiceHelper_v2";
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
                    LogDetailed("Executing Alt+Q (Immediate).");
                    action.Invoke();
                }
                else
                {
                    // Ставим в очередь для последовательного выполнения
                    lock (_queueLock)
                    {
                        _commandQueue.Enqueue((action, keyCombo));
                        LogDetailed($"Enqueued: {keyCombo}. Queue size: {_commandQueue.Count}");
                        
                        if (_isQueueRunning)
                        {
                            Dispatcher.Invoke(UpdateOverlayMessage);
                        }

                        if (!_isQueueRunning)
                        {
                            _isQueueRunning = true;
                            Task.Run(ProcessQueue);
                        }
                    }
                }
                return true; // Suppress original key press
            }
            return false;
        }
    
        private void ProcessQueue()
        {
            try
            {
                LogDetailed("Queue processor started.");
                while (true)
                {
                    (Action Act, string Name) item;
                    int queueCount;
                    lock (_queueLock)
                    {
                        if (_commandQueue.Count == 0)
                        {
                            LogDetailed("Queue empty. Processor stopping.");
                            _isQueueRunning = false;
                            break;
                        }
                        item = _commandQueue.Dequeue();
                        queueCount = _commandQueue.Count;
                    }
                    LogDetailed($"Processing item: {item.Name}. Remaining in queue: {queueCount}");
    
                    _currentActionName = FormatKeyCombo(item.Name);

                    Dispatcher.Invoke(() => 
                    {
                        IncrementCommandCount();
                        UpdateOverlayMessage();
                    });
    
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        item.Act.Invoke();
                    }
                    catch (Exception ex) { LogDetailed($"Action failed: {ex.Message}"); }
                    sw.Stop();
                    LogDetailed($"Finished item: {item.Name}. Duration: {sw.ElapsedMilliseconds}ms");
                }
    
                Dispatcher.Invoke(async () => 
                {
                    await Task.Delay(500);
                    lock (_queueLock)
                    {
                        if (!_isQueueRunning) _overlay.HideMessage();
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"КРИТИЧЕСКАЯ ОШИБКА В ОЧЕРЕДИ: {ex.Message}");
                LogDetailed($"CRITICAL QUEUE ERROR: {ex.Message}");
                lock (_queueLock)
                {
                    _isQueueRunning = false;
                    _commandQueue.Clear();
                }
            }
        }

        private void UpdateOverlayMessage()
        {
            int count;
            lock (_queueLock) count = _commandQueue.Count;
            
            string msg = _currentActionName;
            if (count > 0) msg += $" (Очередь: {count})";
            _overlay.ShowMessage(msg);
        }

        private void ClearCommandQueue()
        {
            int clearedCount;
            lock (_queueLock)
            {
                clearedCount = _commandQueue.Count;
                if (clearedCount > 0)
                {
                    _commandQueue.Clear();
                }
            }

            if (clearedCount > 0)
            {
                Log($"Очередь команд принудительно очищена ({clearedCount} команд).");
                Dispatcher.Invoke(() =>
                {
                    _overlay.ShowMessage("Очередь очищена");
                });
            }
        }

        private string FormatKeyCombo(string keyCombo)
        {
            return keyCombo.Replace("NumPad", "Num ")
                     .Replace("D0", "0")
                     .Replace("D1", "1")
                     .Replace("D2", "2")
                     .Replace("D3", "3")
                     .Replace("D4", "4")
                     .Replace("D5", "5")
                     .Replace("D6", "6")
                     .Replace("D7", "7")
                     .Replace("D8", "8")
                     .Replace("D9", "9")
                     .Replace("Multiply", "Num*")
                     .Replace("Add", "Num+")
                     .Replace("Divide", "Num/");
        }

        private void IncrementCommandCount()
        {
            _commandCount++;
            txtCommandCount.Text = _commandCount.ToString();
        }

        private void ResetCommandCount_Click(object sender, RoutedEventArgs e)
        {
            _commandCount = 0;
            txtCommandCount.Text = "0";
        }

        private void ExecuteBotCommand(string? args)
        {
            WaitForInputFocus();
            LogDetailed($"[START] ExecuteBotCommand. Args: {args ?? "null"}");
            var sw = Stopwatch.StartNew();
            _hotkeyManager.IsInputBlocked = true;
            try
            {
                // 1. Prepare
                NativeMethods.ReleaseModifiers();
                NativeMethods.ReleaseAlphaKeys();
                LogDetailed("Modifiers released. Sleep 100ms.");
                Thread.Sleep(100);

                // 2. Type @chat_bot
                LogDetailed("Typing '@chat_bot'...");
                TypeText("@chat_bot");
                LogDetailed("Sleep 100ms.");
                Thread.Sleep(100);
                
                // 3. Enter to select bot
                LogDetailed("Sending Enter.");
                NativeMethods.SendKey(NativeMethods.VK_RETURN);
                
                // 4. If args exist, type them
                if (!string.IsNullOrEmpty(args))
                {
                    LogDetailed("Args present. Sleep 200ms.");
                    Thread.Sleep(200);
                    NativeMethods.SendKey(NativeMethods.VK_SPACE);
                    LogDetailed("Sent Space. Sleep 50ms.");
                    Thread.Sleep(50);
                    LogDetailed($"Typing args: {args}");
                    TypeText(args);
                    LogDetailed("Sleep 100ms.");
                    Thread.Sleep(100);
                    NativeMethods.SendKey(NativeMethods.VK_SPACE);
                    LogDetailed("Sent final Space.");
                }
            }
            finally
            {
                _hotkeyManager.IsInputBlocked = false;
                if (_hotkeyManager.IsAltPhysicallyDown)
                {
                    NativeMethods.PressAltDown();
                    LogDetailed("Restored Alt key.");
                }
                sw.Stop();
                LogDetailed($"[END] ExecuteBotCommand. Total duration: {sw.ElapsedMilliseconds}ms");
            }
        }

        private void ExecuteReply(string text)
        {
            WaitForInputFocus();
            LogDetailed($"[START] ExecuteReply. Text length: {text.Length}");
            var sw = Stopwatch.StartNew();
            _hotkeyManager.IsInputBlocked = true;
            try
            {
                // 1. Prepare
                NativeMethods.ReleaseModifiers();
                NativeMethods.ReleaseAlphaKeys(); // Fix for Alt+C triggering Ctrl+C
                LogDetailed("Modifiers released. Sleep 100ms.");
                Thread.Sleep(100);

                // 2. Paste Text
                LogDetailed("Calling TypeText...");
                TypeText(text);
                LogDetailed("TypeText finished. Sleep 50ms.");
                Thread.Sleep(50);
                LogDetailed("Sleep 100ms.");
                Thread.Sleep(100);

                // 3. Send Enter (Auto-send)
                LogDetailed("Sending Enter.");
                NativeMethods.SendKey(NativeMethods.VK_RETURN);
            }
            finally
            {
                _hotkeyManager.IsInputBlocked = false;
                if (_hotkeyManager.IsAltPhysicallyDown)
                {
                    NativeMethods.PressAltDown();
                    LogDetailed("Restored Alt key.");
                }
                sw.Stop();
                LogDetailed($"[END] ExecuteReply. Total duration: {sw.ElapsedMilliseconds}ms");
            }
        }

        private void WaitForInputFocus()
        {
            if (IsInputFocused()) return;

            LogDetailed("Waiting for input focus...");
            while (!IsInputFocused())
            {
                Dispatcher.Invoke(() => _overlay.ShowMessage("Ожидание поля ввода..."));
                Thread.Sleep(500);
            }
            LogDetailed("Input focus detected.");
            Dispatcher.Invoke(UpdateOverlayMessage);
        }

        private bool IsInputFocused()
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

        private void TypeText(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            LogDetailed($"TypeText: Splitting into {lines.Length} lines.");
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrEmpty(lines[i]))
                {
                    LogDetailed($"Sending text line {i}: '{lines[i]}'");
                    NativeMethods.SendText(lines[i]);
                    LogDetailed("Sleep 50ms (after text).");
                    Thread.Sleep(50);
                }
                
                if (i < lines.Length - 1)
                {
                    LogDetailed("Sleep 50ms (before Enter).");
                    Thread.Sleep(50);
                    LogDetailed("Sending Enter.");
                    NativeMethods.SendKey(NativeMethods.VK_RETURN);
                    LogDetailed("Sleep 50ms (after Enter).");
                    Thread.Sleep(50);
                }
            }
        }


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

            Task.Run(() =>
            {
                try
                {
                    // Используем cmdkey для сохранения учетных данных в Windows (Credential Manager)
                    // Это позволяет Проводнику автоматически подхватить пароль при открытии папки
                    string target = "files.resto.lan";

                    var pDel = Process.Start(new ProcessStartInfo("cmdkey", $"/delete:{target}") { CreateNoWindow = true, UseShellExecute = false });
                    pDel?.WaitForExit();

                    var pAdd = Process.Start(new ProcessStartInfo("cmdkey", $"/add:{target} /user:{user} /pass:\"{pass}\"") { CreateNoWindow = true, UseShellExecute = false });
                    pAdd?.WaitForExit();

                    Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
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
                    Process.Start(new ProcessStartInfo("explorer.exe", AppDir) { UseShellExecute = true });
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
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new AppSettings 
                { 
                    NotesFontSize = txtNotes.FontSize,
                    CrmLogin = txtCrmLogin.Text,
                    CrmPassword = txtCrmPassword.Password
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
            var targetBrowsers = new[] { "msedge", "chrome", "browser", "vivaldi" };
            var foundBrowsers = new List<BrowserItem>();

            // 1. Ищем по стандартным путям (даже если не запущены)
            var commonPaths = new List<(string Name, string Path)>
            {
                ("Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
                ("Edge", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
                ("Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
                ("Chrome", @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"),
                ("Yandex", @"C:\Users\" + Environment.UserName + @"\AppData\Local\Yandex\YandexBrowser\Application\browser.exe")
            };

            foreach (var item in commonPaths)
            {
                if (File.Exists(item.Path))
                {
                    if (!foundBrowsers.Any(b => b.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        foundBrowsers.Add(new BrowserItem { Name = item.Name, Path = item.Path });
                    }
                }
            }

            // 2. Ищем запущенные процессы (для нестандартных путей)
            foreach (var procName in targetBrowsers)
            {
                var processes = Process.GetProcessesByName(procName);
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.MainModule != null)
                        {
                            string? path = p.MainModule.FileName;
                            if (string.IsNullOrEmpty(path)) continue;

                            string name = procName switch
                            {
                                "msedge" => "Edge",
                                "chrome" => "Chrome",
                                "browser" => "Yandex",
                                "vivaldi" => "Vivaldi",
                                _ => procName
                            };

                            if (!foundBrowsers.Any(b => b.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                            {
                                foundBrowsers.Add(new BrowserItem { Name = name, Path = path });
                            }
                            break; // Достаточно одного процесса для получения пути
                        }
                    }
                    catch { /* Игнорируем ошибки доступа к системным процессам */ }
                }
            }

            cmbBrowsers.ItemsSource = foundBrowsers;
            if (foundBrowsers.Count > 0 && cmbBrowsers.SelectedIndex == -1)
                cmbBrowsers.SelectedIndex = 0;
        }

        private async void BtnCrmAutoLogin_Click(object sender, RoutedEventArgs e)
        {
            if (_isCrmActive)
            {
                _isCrmActive = false;
                _crmTimer.Stop();
                btnCrmAutoLogin.Content = "ВКЛЮЧИТЬ";
                txtCrmStatus.Text = "Статус: Отключено";
                txtCrmStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                Log("Авто-вход остановлен пользователем.");
            }
            else
            {
                if (cmbBrowsers.SelectedValue == null)
                {
                    MessageBox.Show("Сначала выберите браузер из списка (браузер должен быть запущен).");
                    return;
                }

                if (string.IsNullOrEmpty(txtCrmLogin.Text) || string.IsNullOrEmpty(txtCrmPassword.Password))
                {
                    MessageBox.Show("Введите Логин и Пароль для авто-входа.");
                    return;
                }

                // Проверка: Запущен ли браузер без порта отладки?
                var selectedBrowser = cmbBrowsers.SelectedItem as BrowserItem;
                if (selectedBrowser != null)
                {
                    string procName = System.IO.Path.GetFileNameWithoutExtension(selectedBrowser.Path);
                    bool isRunning = Process.GetProcessesByName(procName).Any();
                    bool cdpAvailable = false;
                    try 
                    { 
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                        await http.GetStringAsync("http://127.0.0.1:9222/json"); 
                        cdpAvailable = true; 
                    } catch { }

                    if (isRunning && !cdpAvailable)
                    {
                        // Автоматический перезапуск для открытия порта, как требовалось
                        Log($"Браузер {selectedBrowser.Name} запущен без порта. Перезапуск...");
                        foreach (var p in Process.GetProcessesByName(procName)) { try { p.Kill(); } catch { } }
                        await Task.Delay(2000);
                    }
                }

                _isCrmActive = true;
                
                CrmTimer_Tick(null, null); // Запуск сразу
                _crmTimer.Start();
                btnCrmAutoLogin.Content = "СТОП";
                txtCrmStatus.Text = $"Статус: Активно";
                txtCrmStatus.Foreground = (System.Windows.Media.Brush)FindResource("BrushAccent");
                Log("Авто-вход включен.");
            }
        }

        private void CrmTimer_Tick(object? sender, EventArgs? e)
        {
            Log("Таймер сработал: Выполнение авто-входа...");
            txtLastRun.Text = $"Последний запуск: {DateTime.Now:HH:mm}";
            RunBackgroundLogin();
        }

        private async void RunBackgroundLogin()
        {
            try
            {
                var selectedBrowser = cmbBrowsers.SelectedItem as BrowserItem;
                string? browserPath = selectedBrowser?.Path;
                string browserName = selectedBrowser?.Name ?? "Unknown";

                if (!string.IsNullOrEmpty(browserPath) && File.Exists(browserPath))
                {
                    using var http = new HttpClient();
                    http.Timeout = TimeSpan.FromSeconds(2);

                    int port = 9222;
                    bool cdpAvailable = false;

                    // 1. Проверяем, доступен ли уже порт отладки (CDP)
                    try 
                    {
                        await http.GetStringAsync($"http://127.0.0.1:{port}/json");
                        cdpAvailable = true;
                    }
                    catch { }

                    if (cdpAvailable)
                    {
                        Log("Порт активен. Выполнение скрипта без открытия вкладок...");
                    }
                    else
                    {
                        Log($"Порт закрыт. Запуск браузера с открытым портом...");
                        
                        string args = $"--remote-debugging-port={port} --no-first-run --no-default-browser-check \"http://crm.iiko.ru/\"";
                        var psi = new ProcessStartInfo(browserPath) 
                        { 
                            Arguments = args, 
                            UseShellExecute = true, 
                            WindowStyle = ProcessWindowStyle.Normal 
                        };
                        _browserProcess = Process.Start(psi);
                        await Task.Delay(5000);
                    }

                    // 1. Выполняем вход через HTTP (без браузера)
                    Log("Выполнение HTTP-входа...");
                    var cookies = await PerformHttpLogin(txtCrmLogin.Text, txtCrmPassword.Password);
                    
                    if (cookies != null && cookies.Count > 0)
                    {
                        Log($"Получено куки: {cookies.Count}. Внедрение в браузер...");

                        // 2. Подключаемся к браузеру (любая страница подойдет, нам нужен только Network домен)
                        // Ищем любую страницу для подключения WebSocket
                        string json = await http.GetStringAsync($"http://127.0.0.1:{port}/json");
                        string? wsUrl = null;
                        using (var doc = JsonDocument.Parse(json))
                        {
                            foreach (var el in doc.RootElement.EnumerateArray())
                            {
                                if (el.TryGetProperty("webSocketDebuggerUrl", out var val))
                                {
                                    wsUrl = val.GetString();
                                    break;
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(wsUrl))
                        {
                            Log("Ошибка: Не удалось подключиться к CDP (нет доступных таргетов).");
                            return;
                        }

                        using var ws = new ClientWebSocket();
                        await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
                        
                        // 3. Внедряем куки через Network.setCookie
                        foreach (Cookie cookie in cookies)
                        {
                            var cookieCmd = new 
                            { 
                                id = new Random().Next(1000, 9999), 
                                method = "Network.setCookie", 
                                @params = new 
                                { 
                                    name = cookie.Name, 
                                    value = cookie.Value, 
                                    domain = "crm.iiko.ru", // Принудительно ставим домен
                                    path = "/",
                                    expires = (long)(DateTime.UtcNow.AddYears(1) - new DateTime(1970, 1, 1)).TotalSeconds
                                } 
                            };
                            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cookieCmd));
                            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                        }

                        txtCrmStatus.Text = $"Успешный вход: {DateTime.Now:HH:mm}";
                        Log("Куки успешно внедрены в браузер.");
                    }
                    else
                    {
                        txtCrmStatus.Text = "Ошибка входа";
                        Log("Не удалось выполнить вход через HTTP.");
                    }
                }
                else
                {
                    _isCrmActive = false;
                    _crmTimer.Stop();
                    btnCrmAutoLogin.Content = "ВКЛЮЧИТЬ";
                    MessageBox.Show("Браузер не найден. Авто-вход остановлен.");
                    Log("Ошибка: Браузер не найден.");
                }
            }
            catch (Exception ex)
            {
                txtCrmStatus.Text = "Ошибка входа";
                Debug.WriteLine(ex.Message);
                Log($"КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
            }
        }

        private async Task<CookieCollection?> PerformHttpLogin(string login, string password)
        {
            try
            {
                var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true, AllowAutoRedirect = true };
                using var client = new HttpClient(handler);
                
                // 1. Загружаем страницу для инициализации сессии
                await client.GetAsync("http://crm.iiko.ru/");

                // 2. Отправляем форму входа
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("user_name", login),
                    new KeyValuePair<string, string>("user_password", password),
                    new KeyValuePair<string, string>("Login", "Login") // Эмуляция нажатия кнопки
                });

                var response = await client.PostAsync("http://crm.iiko.ru/", content);
                
                return handler.CookieContainer.GetCookies(new Uri("http://crm.iiko.ru/"));
            }
            catch (Exception ex) { Log($"HTTP Login Error: {ex.Message}"); return null; }
        }

        private void BtnShowBrowser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool found = false;

                // 1. Пробуем найти через сохраненный процесс
                if (_browserProcess != null && !_browserProcess.HasExited)
                {
                    _browserProcess.Refresh();
                    IntPtr handle = _browserProcess.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
                        NativeMethods.SetForegroundWindow(handle);
                        Log("Команда 'Показать' отправлена окну браузера (Process).");
                        found = true;
                    }
                }

                // 2. Если не вышло, ищем любое окно выбранного браузера
                if (!found)
                {
                    var selectedBrowser = cmbBrowsers.SelectedItem as BrowserItem;
                    if (selectedBrowser != null)
                    {
                        string procName = System.IO.Path.GetFileNameWithoutExtension(selectedBrowser.Path);
                        var procs = Process.GetProcessesByName(procName);
                        foreach (var p in procs)
                        {
                            if (p.MainWindowHandle != IntPtr.Zero)
                            {
                                NativeMethods.ShowWindow(p.MainWindowHandle, NativeMethods.SW_RESTORE);
                                NativeMethods.SetForegroundWindow(p.MainWindowHandle);
                                found = true;
                                break; // Разворачиваем первое найденное
                            }
                        }
                        if (found) Log($"Команда 'Показать' отправлена окну браузера ({procName}).");
                    }
                }

                if (!found) Log("Процесс браузера не найден или не имеет окна.");
            }
            catch
            {
                Log("Браузер не найден или не запущен.");
            }
        }

        private void BtnKillBrowser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_browserProcess != null && !_browserProcess.HasExited)
                {
                    _browserProcess.Kill();
                    Log("Браузер принудительно закрыт.");
                }
                else
                {
                    Log("Нет активного процесса для закрытия.");
                }
            }
            catch { }
        }
    }

    public class HotkeyDisplay
    {
        public string Keys { get; set; } = "";
        public string Desc { get; set; } = "";
    }

    public class AppSettings
    {
        public double NotesFontSize { get; set; } = 14;
        public string CrmLogin { get; set; } = "";
        public string CrmPassword { get; set; } = "";
    }

    public class BrowserItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
    }
}