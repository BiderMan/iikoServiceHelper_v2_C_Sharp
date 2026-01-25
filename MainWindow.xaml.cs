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
using Forms = System.Windows.Forms; 
using System.Net.Http;
using System.Runtime.InteropServices;

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

            Reg("Alt+End", "Закрываем (выполнена)", () => reply("Заявку закрываем как выполненную."));

            Reg("Alt+Shift+End", "От вас не поступила обратная связь.", () => reply("От вас не поступила обратная связь.\nСпасибо за обращение в iikoService и хорошего Вам дня.\nЕсли возникнут трудности или дополнительные вопросы, просим обратиться к нам повторно.\nЗаявку закрываем."));

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
                    if (settings != null) txtNotes.FontSize = settings.NotesFontSize;
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new AppSettings { NotesFontSize = txtNotes.FontSize };
                var json = JsonSerializer.Serialize(settings);
                File.WriteAllText(SettingsFile, json);
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
    }
}