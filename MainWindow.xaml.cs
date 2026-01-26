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

namespace iikoServiceHelper
{
    public partial class MainWindow : Window
    {
        private const string AppName = "iikoServiceHelper_v2";
        private readonly string AppDir;
        private readonly string NotesFile;
        private readonly string SettingsFile;

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
            _crmTimer.Interval = TimeSpan.FromHours(1);
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

            Reg("Alt+Divide", "Сообщить о платных работах", () => reply("Добрый день, вы обратились в техническую поддержку iikoService.\nК сожалению, с Вашей организацией не заключен договор технической поддержки.\nРаботы могут быть выполнены только на платной основе.\n\nСтоимость работ: руб.\nВы согласны на платные работы?\n"));
            
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
                lock (_queueLock)
                {
                    _commandQueue.Enqueue((action, keyCombo));
                    if (!_isQueueRunning)
                    {
                        _isQueueRunning = true;
                        Task.Run(ProcessQueue);
                    }
                }
                return true; // Suppress original key press
            }
            return false;
        }

        private void ProcessQueue()
        {
            while (true)
            {
                (Action Act, string Name) item;
                int queueCount;
                lock (_queueLock)
                {
                    if (_commandQueue.Count == 0)
                    {
                        _isQueueRunning = false;
                        break;
                    }
                    item = _commandQueue.Dequeue();
                    queueCount = _commandQueue.Count;
                }

                Dispatcher.Invoke(() => 
                {
                    IncrementCommandCount();
                    string msg = FormatKeyCombo(item.Name);
                    if (queueCount > 0)
                        msg += $" (Очередь: {queueCount})";
                    _overlay.ShowMessage(msg);
                });

                try
                {
                    item.Act.Invoke();
                }
                catch { }
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
            _hotkeyManager.IsInputBlocked = true;
            try
            {
                // 1. Prepare
                NativeMethods.ReleaseModifiers();
                NativeMethods.ReleaseAlphaKeys();
                Thread.Sleep(100); // Increased for stability

                // 2. Type @chat_bot
                TypeText("@chat_bot");
                Thread.Sleep(100);
                
                // 3. Enter to select bot
                NativeMethods.SendKey(NativeMethods.VK_RETURN);
                
                // 4. If args exist, type them
                if (!string.IsNullOrEmpty(args))
                {
                    Thread.Sleep(200); // Wait for UI to react to Enter
                    NativeMethods.SendKey(NativeMethods.VK_SPACE);
                    Thread.Sleep(50);
                    TypeText(args);
                    Thread.Sleep(100);
                    NativeMethods.SendKey(NativeMethods.VK_SPACE);
                }
            }
            finally
            {
                _hotkeyManager.IsInputBlocked = false;
                if (_hotkeyManager.IsAltPhysicallyDown)
                {
                    NativeMethods.PressAltDown();
                }
            }
        }

        private void ExecuteReply(string text)
        {
            _hotkeyManager.IsInputBlocked = true;
            try
            {
                // 1. Prepare
                NativeMethods.ReleaseModifiers();
                NativeMethods.ReleaseAlphaKeys(); // Fix for Alt+C triggering Ctrl+C
                Thread.Sleep(100); // Increased for stability

                // 2. Paste Text
                TypeText(text);
                Thread.Sleep(50);
                Thread.Sleep(100);

                // 3. Send Enter (Auto-send)
                NativeMethods.SendKey(NativeMethods.VK_RETURN);
            }
            finally
            {
                _hotkeyManager.IsInputBlocked = false;
                if (_hotkeyManager.IsAltPhysicallyDown)
                {
                    NativeMethods.PressAltDown();
                }
            }
        }

        private void TypeText(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrEmpty(lines[i]))
                    NativeMethods.SendText(lines[i]);
                
                if (i < lines.Length - 1)
                {
                    Thread.Sleep(50);
                    NativeMethods.SendKey(NativeMethods.VK_RETURN);
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
                        var res = MessageBox.Show(
                            $"Браузер {selectedBrowser.Name} запущен без порта отладки.\n" +
                            "Для работы авто-входа необходимо перезапустить браузер.\n\n" +
                            "Перезапустить сейчас?", 
                            "Требуется перезапуск", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        
                        if (res == MessageBoxResult.Yes)
                        {
                            Log($"Перезапуск браузера {selectedBrowser.Name}...");
                            foreach (var p in Process.GetProcessesByName(procName)) { try { p.Kill(); } catch { } }
                            await Task.Delay(2000);
                        }
                        else return;
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
            RunVisibleLogin();
        }

        private async void RunVisibleLogin()
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

                    bool weLaunchedIt = false;

                    // =========================================================
                    // СЦЕНАРИЙ 2: ИСПОЛЬЗОВАНИЕ CDP (Свернутый/Фоновый режим)
                    // =========================================================
                    
                    if (cdpAvailable)
                    {
                        Log("Подключение к активному браузеру через CDP (Свернутый режим)...");
                    }
                    else
                    {
                        Log($"Браузер закрыт. Запуск в СВЕРНУТОМ режиме (Основной профиль)...");
                        
                        // Запускаем свернутым, чтобы подхватился основной профиль
                        string args = $"--remote-debugging-port={port} --no-first-run --no-default-browser-check \"http://crm.iiko.ru/\"";
                        var psi = new ProcessStartInfo(browserPath) 
                        { 
                            Arguments = args, 
                            UseShellExecute = true, 
                            WindowStyle = ProcessWindowStyle.Minimized 
                        };
                        _browserProcess = Process.Start(psi);
                        weLaunchedIt = true;
                        await Task.Delay(5000);
                    }

                    // Подключение WebSocket
                    string? wsUrl = null;

                    // Всегда пробуем найти или создать правильную вкладку
                    // Это предотвращает захват чужой активной вкладки (например, если weLaunchedIt определился неверно)
                    try
                    {
                        // 1. Сначала ищем существующую вкладку
                        string jsonTabs = await http.GetStringAsync($"http://127.0.0.1:{port}/json");
                        using var docTabs = JsonDocument.Parse(jsonTabs);
                        
                        foreach (var el in docTabs.RootElement.EnumerateArray())
                        {
                            if (el.TryGetProperty("type", out var type) && type.GetString() == "page")
                            {
                                if (el.TryGetProperty("url", out var urlProp) && urlProp.GetString()?.Contains("crm.iiko.ru") == true)
                                {
                                    if (el.TryGetProperty("webSocketDebuggerUrl", out var val))
                                    {
                                        wsUrl = val.GetString();
                                        Log("Найдена существующая вкладка CRM.");
                                        // Активируем вкладку
                                        if (el.TryGetProperty("id", out var id))
                                            try { await http.GetStringAsync($"http://127.0.0.1:{port}/json/activate/{id.GetString()}"); } catch { }
                                        break;
                                    }
                                }
                            }
                        }

                        // 2. Если не нашли, создаем новую
                        if (string.IsNullOrEmpty(wsUrl))
                        {
                            Log("Вкладка CRM не найдена. Создание новой...");
                            var response = await http.PutAsync($"http://127.0.0.1:{port}/json/new?http://crm.iiko.ru/", new StringContent(""));
                            if (response.IsSuccessStatusCode)
                            {
                                string jsonNew = await response.Content.ReadAsStringAsync();
                                using var docNew = JsonDocument.Parse(jsonNew);
                                if (docNew.RootElement.TryGetProperty("webSocketDebuggerUrl", out var val))
                                {
                                    wsUrl = val.GetString();
                                    Log("Вкладка создана успешно.");
                                }
                            }
                        }
                    }
                    catch (Exception ex) { Log($"Ошибка поиска/создания вкладки: {ex.Message}"); }

                    if (string.IsNullOrEmpty(wsUrl))
                    {
                        Log("Поиск существующей страницы...");
                        for (int i = 0; i < 10; i++)
                        {
                            try
                            {
                                string json = await http.GetStringAsync($"http://127.0.0.1:{port}/json");
                                using var doc = JsonDocument.Parse(json);
                                foreach (var el in doc.RootElement.EnumerateArray())
                                {
                                    if (el.TryGetProperty("type", out var type) && type.GetString() == "page")
                                    {
                                        if (el.TryGetProperty("webSocketDebuggerUrl", out var val)) { wsUrl = val.GetString(); break; }
                                    }
                                }
                            }
                            catch { }
                            if (!string.IsNullOrEmpty(wsUrl)) break;
                            await Task.Delay(1000);
                        }
                    }

                    if (!string.IsNullOrEmpty(wsUrl))
                    {
                        using var ws = new ClientWebSocket();
                        await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
                        Log("WebSocket подключен.");

                        // Мы уже задали URL при создании вкладки или нашли готовую.
                        // Явный переход не требуется.
                        Log("Ожидание загрузки вкладки...");
                        await Task.Delay(4000);

                        // Проверка: Если мы уже залогинены - не пытаемся вводить пароль
                        try
                        {
                            var checkAuthCmd = new { id = 50, method = "Runtime.evaluate", @params = new { expression = "!!document.querySelector('a[href*=\"action=Logout\"]')" } };
                            var checkAuthBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(checkAuthCmd));
                            await ws.SendAsync(new ArraySegment<byte>(checkAuthBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                            
                            var authBuffer = new byte[2048];
                            var authRes = await ws.ReceiveAsync(new ArraySegment<byte>(authBuffer), CancellationToken.None);
                            string authResponse = Encoding.UTF8.GetString(authBuffer, 0, authRes.Count);
                            
                            if (authResponse.Contains("\"value\":true") || authResponse.Contains("\"value\": true"))
                            {
                                txtCrmStatus.Text = $"Уже в системе: {DateTime.Now:HH:mm}";
                                Log("Вкладка уже авторизована. Вход не требуется.");
                                return;
                            }
                        }
                        catch { }

                        string login = txtCrmLogin.Text;
                        string password = txtCrmPassword.Password;

                        // Скрипт для ввода данных
                        string js = $@"
                            (function() {{
                                var attempts = 0;
                                var interval = setInterval(function() {{
                                    var user = document.querySelector('input[name=""user_name""]');
                                    var pass = document.querySelector('input[name=""user_password""]');
                                    var btn = document.querySelector('input[name=""Login""]');
                                    if (!btn) btn = document.querySelector('input[src*=""btnSignInNEW""]');

                                    if(user && pass) {{
                                        clearInterval(interval);
                                        
                                        user.focus();
                                        user.value = '{login}';
                                        user.dispatchEvent(new Event('input', {{ bubbles: true }}));
                                        user.dispatchEvent(new Event('change', {{ bubbles: true }}));
                                        
                                        pass.focus();
                                        pass.value = '{password}';
                                        pass.dispatchEvent(new Event('input', {{ bubbles: true }}));
                                        pass.dispatchEvent(new Event('change', {{ bubbles: true }}));

                                        setTimeout(function() {{
                                            if(btn) btn.click();
                                            else {{ var f = pass.closest('form'); if(f) f.submit(); }}
                                        }}, 500);
                                    }}
                                    
                                    attempts++;
                                    if (attempts > 20) clearInterval(interval); // Ждем появления полей до 10 сек
                                }}, 500);
                            }})();
                        ";

                        var cmd = new { id = 1, method = "Runtime.evaluate", @params = new { expression = js } };
                        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cmd));
                        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                        Log("JS скрипт ввода данных отправлен.");
                        
                        txtCrmStatus.Text = "Выполняется вход...";
                        
                        // Проверка успешности входа (Polling)
                        bool isLogged = false;
                        var buffer = new byte[8192];
                        Log("Начало проверки входа (цикл 60 сек)...");

                        for (int i = 0; i < 60; i++) // Ждем до 60 секунд
                        {
                            await Task.Delay(1000);
                            try
                            {
                                if (weLaunchedIt && _browserProcess != null && _browserProcess.HasExited)
                                {
                                    Log("Браузер был закрыт.");
                                    break;
                                }

                                if (ws.State != WebSocketState.Open)
                                {
                                    Log("Связь с браузером потеряна (WebSocket закрыт).");
                                    break;
                                }

                                var checkCmd = new { id = 1000 + i, method = "Runtime.evaluate", @params = new { expression = "document.querySelector('a[href*=\"action=Logout\"]') ? 'AUTH_SUCCESS' : window.location.href" } };
                                var checkBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(checkCmd));
                                await ws.SendAsync(new ArraySegment<byte>(checkBytes), WebSocketMessageType.Text, true, CancellationToken.None);

                                var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                                string response = Encoding.UTF8.GetString(buffer, 0, res.Count);

                                if (response.Contains("AUTH_SUCCESS"))
                                {
                                    isLogged = true;
                                    Log($"Успешный вход подтвержден (найдена кнопка Выход).");
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"Ошибка проверки ({i}): {ex.Message}");
                            }
                        }

                        if (isLogged)
                        {
                            txtCrmStatus.Text = $"Успешный вход: {DateTime.Now:HH:mm}";
                            Log("Вход выполнен успешно.");
                            Log("Браузер оставлен открытым.");
                        }
                        else
                        {
                            txtCrmStatus.Text = "Вход не подтвержден (Таймаут)";
                            Log("Таймаут ожидания входа.");
                        }
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