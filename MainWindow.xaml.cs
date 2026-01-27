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
        private volatile bool _cancelCurrentAction = false;

        private DispatcherTimer _crmTimer;
        private bool _isCrmActive = false;
        private CancellationTokenSource? _crmCts;

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

            this.StateChanged += (s, e) =>
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    this.Hide();
                    _trayIcon?.ShowBalloonTip(2000, "iikoServiceHelper", "Приложение работает в фоне", Forms.ToolTipIcon.Info);
                }
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
            var descDetails = new Dictionary<string, string>();

            Action<string?> botCmd = (cmd) => ExecuteBotCommand(cmd);
            Action<string> reply = (text) => ExecuteReply(text);

            // Helper to register keys
            void Reg(string keys, string desc, Action action, string? fullText = null)
            {
                _hotkeyActions[keys] = action;
                if (!groupedCmds.ContainsKey(desc))
                {
                    groupedCmds[desc] = new List<string>();
                    descOrder.Add(desc);
                    descDetails[desc] = fullText ?? desc;
                }
                groupedCmds[desc].Add(keys);
            }
            
            void RegReply(string keys, string desc, string text) => Reg(keys, desc, () => reply(text), text);
            void RegBot(string keys, string desc, string cmd) => Reg(keys, desc, () => botCmd(cmd), cmd);

            // --- BOT COMMANDS ---
            // Logic: Type @chat_bot -> Enter -> Type Command -> Enter
            
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

            Reg("Alt+D0", "@chat_bot (Вызов)", botCall, "Вызов меню бота (@chat_bot)");
            Reg("Alt+C",  "@chat_bot (Вызов)", botCall, "Вызов меню бота (@chat_bot)");
            
            RegBot("Alt+D1", "cmd newtask", "cmd newtask");
            RegBot("Alt+D2", "cmd add crmid", "cmd add crmid");
            RegBot("Alt+D3", "cmd add user", "cmd add user");
            RegBot("Alt+D4", "cmd remove crmid", "cmd remove crmid");
            RegBot("Alt+D5", "cmd forcing", "cmd forcing");
            RegBot("Alt+D6", "cmd timer set 6", "cmd timer set 6");
            RegBot("Alt+Shift+D6", "cmd timer dismiss", "cmd timer dismiss");
            
            // Dynamic Date
            Reg("Alt+D7", "cmd timer delay", () => botCmd($"cmd timer delay {DateTime.Now:dd.MM.yyyy HH:mm}"), "cmd timer delay [ТекущаяДата Время]");
            
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

            RegBot("Alt+D8", "cmd duplicate", "cmd duplicate");
            Reg("Alt+Shift+D8", "cmd duplicate (список)", openCrmDialog, "Открыть диалог ввода списка ID для дубликатов");
            RegBot("Alt+D9", "cmd request info", "cmd request info");

            // --- QUICK REPLIES ---
            // Logic: Type Text -> Enter
            
            RegReply("Alt+NumPad1", "Добрый день!", "Добрый день!");
            RegReply("Alt+L",    "Добрый день!", "Добрый день!");

            RegReply("Alt+NumPad2", "У Вас остались вопросы по данному обращению?", "У Вас остались вопросы по данному обращению?");
            RegReply("Alt+D",    "У Вас остались вопросы по данному обращению?", "У Вас остались вопросы по данному обращению?");

            RegReply("Alt+NumPad3", "Ожидайте от нас обратную связь.", "Ожидайте от нас обратную связь.");
            RegReply("Alt+J",    "Ожидайте от нас обратную связь.", "Ожидайте от нас обратную связь.");

            RegReply("Alt+NumPad4", "Заявку закрываем, нет ОС.", "Заявку закрываем, так как не получили от Вас обратную связь.");
            RegReply("Alt+P",    "Заявку закрываем, нет ОС.", "Заявку закрываем, так как не получили от Вас обратную связь.");

            RegReply("Alt+NumPad5", "Ваша заявка передана специалисту.", "Ваша заявка передана специалисту.\nОтветственный специалист свяжется с Вами в ближайшее время.");
            RegReply("Alt+G",    "Ваша заявка передана специалисту.", "Ваша заявка передана специалисту.\nОтветственный специалист свяжется с Вами в ближайшее время.");

            RegReply("Alt+NumPad6", "Не удалось связаться с Вами по номеру:", "Не удалось связаться с Вами по номеру:\nПодскажите, когда с Вами можно будет связаться?");
            RegReply("Alt+H",    "Не удалось связаться с Вами по номеру:", "Не удалось связаться с Вами по номеру:\nПодскажите, когда с Вами можно будет связаться?");

            RegReply("Alt+NumPad7", "Организация определилась верно: ?", "Организация определилась верно: ?");
            RegReply("Alt+E",    "Организация определилась верно: ?", "Организация определилась верно: ?");

            RegReply("Alt+NumPad8", "Ваше обращение взято в работу.", "Ваше обращение взято в работу.");
            RegReply("Alt+M",    "Ваше обращение взято в работу.", "Ваше обращение взято в работу.");

            RegReply("Alt+NumPad9", "Подскажите пожалуйста Ваш контактный номер телефона.", "Подскажите пожалуйста Ваш контактный номер телефона.\nЭто необходимо для регистрации Вашего обращения.");
            RegReply("Alt+N",    "Подскажите пожалуйста Ваш контактный номер телефона.", "Подскажите пожалуйста Ваш контактный номер телефона.\nЭто необходимо для регистрации Вашего обращения.");

            RegReply("Alt+Multiply", "Уточняем информацию по Вашему вопросу.", "Уточняем информацию по Вашему вопросу.");
            RegReply("Alt+X",    "Уточняем информацию по Вашему вопросу.", "Уточняем информацию по Вашему вопросу.");

            RegReply("Alt+Add", "Чем могу Вам помочь?", "Чем могу Вам помочь?");
            RegReply("Alt+F",    "Чем могу Вам помочь?", "Чем могу Вам помочь?");

            RegReply("Alt+Z", "Закрываем (выполнена)", "Заявку закрываем как выполненную.\nСпасибо за обращение в iikoService и хорошего Вам дня.\nЕсли возникнут трудности или дополнительные вопросы, просим обратиться к нам повторно.");

            RegReply("Alt+Shift+Z", "От вас не поступила обратная связь.", "От вас не поступила обратная связь.\nСпасибо за обращение в iikoService и хорошего Вам дня.\nЕсли возникнут трудности или дополнительные вопросы, просим обратиться к нам повторно.\nЗаявку закрываем.");

            RegReply("Alt+B", "Закрываем (нет вопросов)", "В связи с тем, что дополнительных вопросов от вас не поступало, данное обращение закрываем.\nЕсли у вас остались вопросы, при создании новой заявки, просим указать номер текущей.\nСпасибо за обращение в iikoService и хорошего Вам дня!");

            RegReply("Alt+Divide", "Сообщить о платных работах", "Добрый день, вы обратились в техническую поддержку iikoService.  \nК сожалению, с Вашей организацией не заключен договор технической поддержки.\nРаботы могут быть выполнены только на платной основе.\n\nСтоимость работ: руб.\nВы согласны на платные работы?");
            
            Reg("Alt+Space", "Исправить раскладку (выделенное)", () => FixLayout(), "Исправление раскладки выделенного текста (или последнего слова)");
            
            Reg("Alt+Q", "Очистить очередь", ClearCommandQueue, "Принудительная очистка очереди команд");
            
            foreach (var desc in descOrder)
            {
                var formattedKeys = groupedCmds[desc].Select(FormatKeyCombo);

                _displayItems.Add(new HotkeyDisplay 
                { 
                    Keys = string.Join(" / ", formattedKeys), 
                    Desc = desc,
                    FullCommand = descDetails.ContainsKey(desc) ? descDetails[desc] : desc
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
    
                    _cancelCurrentAction = false;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        item.Act.Invoke();
                    }
                    catch (OperationCanceledException)
                    {
                        LogDetailed($"Action canceled: {item.Name}");
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
            _cancelCurrentAction = true;
            int clearedCount;
            lock (_queueLock)
            {
                clearedCount = _commandQueue.Count;
                _commandQueue.Clear();
            }

            LogDetailed($"Очередь команд принудительно очищена (удалено {clearedCount}, прервано текущее).");
            Dispatcher.Invoke(() =>
            {
                _overlay.ShowMessage("Очередь очищена");
            });
        }

        private void CheckCancellation()
        {
            if (_cancelCurrentAction) throw new OperationCanceledException();
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
                CheckCancellation();
                // 1. Prepare
                NativeMethods.ReleaseModifiers();
                NativeMethods.ReleaseAlphaKeys();
                LogDetailed("Modifiers released. Sleep 100ms.");
                Thread.Sleep(100);
                CheckCancellation();

                // 2. Type @chat_bot
                LogDetailed("Typing '@chat_bot'...");
                TypeText("@chat_bot");
                LogDetailed("Sleep 100ms.");
                Thread.Sleep(100);
                CheckCancellation();
                
                // 3. Enter to select bot
                LogDetailed("Sending Enter.");
                NativeMethods.SendKey(NativeMethods.VK_RETURN);
                
                // 4. If args exist, type them
                if (!string.IsNullOrEmpty(args))
                {
                    LogDetailed("Args present. Sleep 200ms.");
                    Thread.Sleep(200);
                    CheckCancellation();
                    NativeMethods.SendKey(NativeMethods.VK_SPACE);
                    LogDetailed("Sent Space. Sleep 50ms.");
                    Thread.Sleep(50);
                    CheckCancellation();
                    LogDetailed($"Typing args: {args}");
                    TypeText(args);
                    LogDetailed("Sleep 100ms.");
                    Thread.Sleep(100);
                    CheckCancellation();
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
                CheckCancellation();
                // 1. Prepare
                NativeMethods.ReleaseModifiers();
                NativeMethods.ReleaseAlphaKeys(); // Fix for Alt+C triggering Ctrl+C
                LogDetailed("Modifiers released. Sleep 100ms.");
                Thread.Sleep(100);
                CheckCancellation();

                // 2. Paste Text
                LogDetailed("Calling TypeText...");
                TypeText(text);
                LogDetailed("TypeText finished. Sleep 50ms.");
                Thread.Sleep(50);
                CheckCancellation();
                LogDetailed("Sleep 100ms.");
                Thread.Sleep(100);
                CheckCancellation();

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
                CheckCancellation();
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
                CheckCancellation();
                if (!string.IsNullOrEmpty(lines[i]))
                {
                    LogDetailed($"Sending text line {i}: '{lines[i]}'");
                    NativeMethods.SendText(lines[i]);
                    LogDetailed("Sleep 50ms (after text).");
                    Thread.Sleep(50);
                }
                
                if (i < lines.Length - 1)
                {
                    CheckCancellation();
                    LogDetailed("Sleep 50ms (before Enter).");
                    Thread.Sleep(50);
                    LogDetailed("Sending Enter.");
                    NativeMethods.SendKey(NativeMethods.VK_RETURN);
                    LogDetailed("Sleep 50ms (after Enter).");
                    Thread.Sleep(50);
                }
            }
        }

        private void FixLayout()
        {
            WaitForInputFocus();
            LogDetailed("[START] FixLayout.");
            var sw = Stopwatch.StartNew();
            _hotkeyManager.IsInputBlocked = true;

            try
            {
                CheckCancellation();
                NativeMethods.ReleaseModifiers();
                NativeMethods.ReleaseAlphaKeys();
                Thread.Sleep(50);

                // 1. Clear to detect selection
                Application.Current.Dispatcher.Invoke(() => { try { Clipboard.Clear(); } catch { } });

                // 2. Copy (Ctrl+C)
                Forms.SendKeys.SendWait("^c");
                Thread.Sleep(50);
                
                string? text = null;
                Application.Current.Dispatcher.Invoke(() => 
                {
                    try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
                });

                if (!string.IsNullOrEmpty(text))
                {
                    string fixedText = ConvertLayout(text);
                    if (text != fixedText)
                    {
                        Application.Current.Dispatcher.Invoke(() => 
                        {
                            try { Clipboard.SetText(fixedText); } catch { }
                        });
                        
                        Forms.SendKeys.SendWait("^v");
                        Thread.Sleep(100); 
                        
                        // 3. Clean up Clipboard History (remove the 2 items we added: Original + Fixed)
                        CleanClipboardHistory(2);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDetailed($"FixLayout Error: {ex.Message}");
            }
            finally
            {
                _hotkeyManager.IsInputBlocked = false;
                if (_hotkeyManager.IsAltPhysicallyDown)
                {
                    NativeMethods.PressAltDown();
                }
                sw.Stop();
                LogDetailed($"[END] FixLayout. Duration: {sw.ElapsedMilliseconds}ms");
            }
        }

        private void CleanClipboardHistory(int itemsToDelete)
        {
            Task.Run(async () =>
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

        private string ConvertLayout(string text)
        {
            // Mapping EN <-> RU
            // Lowercase
            var en = "qwertyuiop[]asdfghjkl;'zxcvbnm,.";
            var ru = "йцукенгшщзхъфывапролджэячсмитьбю";
            // Uppercase
            var enCap = "QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>";
            var ruCap = "ЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ";
            // Symbols
            var enSym = "`~@#$^&/?|\\";
            var ruSym = "ёЁ\"№;:?.,/\\";

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                int idx;
                if ((idx = en.IndexOf(c)) != -1) sb.Append(ru[idx]);
                else if ((idx = ru.IndexOf(c)) != -1) sb.Append(en[idx]);
                else if ((idx = enCap.IndexOf(c)) != -1) sb.Append(ruCap[idx]);
                else if ((idx = ruCap.IndexOf(c)) != -1) sb.Append(enCap[idx]);
                else if ((idx = enSym.IndexOf(c)) != -1) sb.Append(ruSym[idx]);
                else if ((idx = ruSym.IndexOf(c)) != -1) sb.Append(enSym[idx]);
                else sb.Append(c);
            }
            return sb.ToString();
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
                    }
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
                    WindowState = (int)stateToSave
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
            var targetBrowsers = new[] { "msedge", "chrome", "browser", "vivaldi", "opera", "brave", "chromium" };
            var foundBrowsers = new List<BrowserItem>();

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            // 1. Ищем по стандартным путям (даже если не запущены)
            var commonPaths = new List<(string Name, string Path)>
            {
                ("Edge", Path.Combine(programFilesX86, @"Microsoft\Edge\Application\msedge.exe")),
                ("Edge", Path.Combine(programFiles, @"Microsoft\Edge\Application\msedge.exe")),
                ("Chrome", Path.Combine(programFiles, @"Google\Chrome\Application\chrome.exe")),
                ("Chrome", Path.Combine(programFilesX86, @"Google\Chrome\Application\chrome.exe")),
                ("Yandex", Path.Combine(localAppData, @"Yandex\YandexBrowser\Application\browser.exe")),
                ("Vivaldi", Path.Combine(localAppData, @"Vivaldi\Application\vivaldi.exe")),
                ("Brave", Path.Combine(programFiles, @"BraveSoftware\Brave-Browser\Application\brave.exe")),
                ("Opera", Path.Combine(localAppData, @"Programs\Opera\launcher.exe")),
                ("Opera GX", Path.Combine(localAppData, @"Programs\Opera GX\launcher.exe")),
                ("Chromium", Path.Combine(localAppData, @"Chromium\Application\chrome.exe"))
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

                            string name = procName.ToLower() switch
                            {
                                "msedge" => "Edge",
                                "chrome" => "Chrome",
                                "browser" => "Yandex",
                                "vivaldi" => "Vivaldi",
                                "opera" => "Opera",
                                "brave" => "Brave",
                                "chromium" => "Chromium",
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
            
            if (selectedPath != null && foundBrowsers.Any(b => b.Path == selectedPath))
                cmbBrowsers.SelectedValue = selectedPath;
            else if (foundBrowsers.Count > 0)
                cmbBrowsers.SelectedIndex = 0;
        }

        private async void BtnCrmAutoLogin_Click(object sender, RoutedEventArgs e)
        {
            if (_isCrmActive)
            {
                _isCrmActive = false;
                _crmTimer.Stop();
                _crmCts?.Cancel();
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

                _isCrmActive = true;
                
                _crmCts = new CancellationTokenSource();
                CrmTimer_Tick(null, null); // Запуск сразу
                _crmTimer.Start();
                btnCrmAutoLogin.Content = "СТОП";
                txtCrmStatus.Text = $"Статус: Активно";
                txtCrmStatus.Foreground = (System.Windows.Media.Brush)FindResource("BrushAccent");
                Log("Авто-вход включен.");
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

        private void CrmTimer_Tick(object? sender, EventArgs? e)
        {
            if (_crmCts == null || _crmCts.IsCancellationRequested) return;
            Log("Таймер сработал: Выполнение авто-входа...");
            txtLastRun.Text = $"Последний запуск: {DateTime.Now:HH:mm}";
            RunBackgroundLogin(_crmCts.Token);
        }

        private async void RunBackgroundLogin(CancellationToken token)
        {
            try
            {
                string login = txtCrmLogin.Text;
                string password = txtCrmPassword.Password;
                var selectedBrowser = cmbBrowsers.SelectedItem as BrowserItem;

                if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
                {
                    Log("Ошибка: Логин или пароль не заполнены.");
                    return;
                }

                Log("=== START CRM AUTO-LOGIN ===");
                Log("Запуск авто-входа CRM...");

                using var http = new HttpClient();
                
                // 1. Проверка порта 9222
                Log("Checking port 9222...");
                string versionJson = "";
                try
                {
                    versionJson = await http.GetStringAsync("http://127.0.0.1:9222/json/version", token);
                    Log("Port 9222 is open.");
                }
                catch
                {
                    Log("Порт 9222 закрыт. Авто-вход отключен.");
                    Log("Требуется запуск браузера с параметром --remote-debugging-port=9222");

                    _isCrmActive = false;
                    _crmTimer.Stop();
                    _crmCts?.Cancel();
                    btnCrmAutoLogin.Content = "ВКЛЮЧИТЬ";
                    txtCrmStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                    return;
                }

                // Проверка соответствия браузера
                if (selectedBrowser != null)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(versionJson);
                        if (doc.RootElement.TryGetProperty("Browser", out var browserEl))
                        {
                            string remoteBrowser = browserEl.GetString() ?? "";
                            Log($"Connected to: {remoteBrowser}");

                            if (selectedBrowser.Name.Equals("Chrome", StringComparison.OrdinalIgnoreCase) && 
                                remoteBrowser.Contains("Edg", StringComparison.OrdinalIgnoreCase))
                            {
                                Log("WARN: Выбран Chrome, но порт 9222 занят Edge.");
                            }
                        }
                    }
                    catch { }
                }

                // 2. Создание новой вкладки
                Log("Creating new tab (http://crm.iiko.ru/)...");
                string tabId = "";
                string wsUrl = "";

                try
                {
                    // Попытка создать вкладку в фоне (background: true) через Browser Target
                    try 
                    {
                        string bgVersionJson = await http.GetStringAsync("http://127.0.0.1:9222/json/version", token);
                        string browserWsUrl = "";
                        using (var doc = JsonDocument.Parse(bgVersionJson))
                        {
                            if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsEl)) 
                                browserWsUrl = wsEl.GetString() ?? "";
                        }

                        if (!string.IsNullOrEmpty(browserWsUrl))
                        {
                            using var wsBrowser = new ClientWebSocket();
                            await wsBrowser.ConnectAsync(new Uri(browserWsUrl), token);
                            
                            var createCmd = new { id = 1, method = "Target.createTarget", @params = new { url = "http://crm.iiko.ru/", background = true } };
                            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(createCmd));
                            await wsBrowser.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                            
                            var buffer = new byte[4096];
                            var res = await wsBrowser.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            string responseJson = Encoding.UTF8.GetString(buffer, 0, res.Count);
                            
                            using var docResp = JsonDocument.Parse(responseJson);
                            if (docResp.RootElement.TryGetProperty("result", out var resEl) && resEl.TryGetProperty("targetId", out var tidEl))
                            {
                                tabId = tidEl.GetString() ?? "";
                            }
                        }
                    }
                    catch { /* Fallback */ }

                    // Если не вышло (или старый метод), пробуем стандартный /json/new (активная вкладка)
                    if (string.IsNullOrEmpty(tabId))
                    {
                        var response = await http.PutAsync("http://127.0.0.1:9222/json/new?http://crm.iiko.ru/", null, token);
                        string json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("id", out var idEl)) tabId = idEl.GetString() ?? "";
                        if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsEl)) wsUrl = wsEl.GetString() ?? "";
                    }

                    // Если создали через Browser Target, нужно найти WS URL вкладки
                    if (!string.IsNullOrEmpty(tabId) && string.IsNullOrEmpty(wsUrl))
                    {
                        string jsonTargets = await http.GetStringAsync("http://127.0.0.1:9222/json", token);
                        using var docTargets = JsonDocument.Parse(jsonTargets);
                        foreach (var el in docTargets.RootElement.EnumerateArray())
                        {
                            if (el.TryGetProperty("id", out var id) && id.GetString() == tabId)
                            {
                                if (el.TryGetProperty("webSocketDebuggerUrl", out var val)) wsUrl = val.GetString() ?? "";
                                break;
                            }
                        }
                    }

                    Log($"Tab created. ID: {tabId}, WS: {wsUrl}");
                }
                catch (Exception ex)
                {
                    Log($"Ошибка создания вкладки: {ex.Message}");
                    return;
                }

                if (string.IsNullOrEmpty(wsUrl))
                {
                    Log("Не удалось получить WebSocket URL.");
                    Log("WebSocket URL is empty.");
                    return;
                }

                // 3. Подключение WebSocket
                Log($"Connecting WebSocket...");
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), token);
                Log("WebSocket connected.");

                // Локальная функция для выполнения JS
                async Task<string> Eval(string js)
                {
                    try
                    {
                        var reqId = new Random().Next(10000, 99999);
                        var cmd = new
                        {
                            id = reqId,
                            method = "Runtime.evaluate",
                            @params = new { expression = js, returnByValue = true }
                        };
                        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cmd));
                        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);

                        var buffer = new byte[8192];
                        var sb = new StringBuilder();
                        var start = DateTime.Now;

                        while ((DateTime.Now - start).TotalSeconds < 5 && ws.State == WebSocketState.Open)
                        {
                            var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                            if (res.EndOfMessage)
                            {
                                var respText = sb.ToString();
                                if (respText.Contains($"\"id\":{reqId}")) return respText;
                                sb.Clear();
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { Log($"Eval Error: {ex.Message}"); }
                    return "";
                }

                // 4. Ожидание загрузки
                Log("Waiting for page load (3s)...");
                await Task.Delay(3000, token);

                // 5. Проверка статуса входа
                Log("Checking login status...");
                string checkJs = "document.querySelector('a[href*=\"action=Logout\"]') !== null";
                string resp = await Eval(checkJs);

                if (resp.Contains("\"value\":true"))
                {
                    Log("Уже авторизован.");
                    Log("Already logged in.");
                    txtCrmStatus.Text = $"Вход ОК: {DateTime.Now:HH:mm}";
                }
                else
                {
                    Log("Not logged in. Attempting to login...");

                    // Ввод данных
                    Log($"Filling form. Login: {login}");
                    string fillJs = $"var u = document.querySelector('input[name=\"user_name\"]'); if(u) u.value = '{login}'; " +
                                    $"var p = document.querySelector('input[name=\"user_password\"]'); if(p) p.value = '{password}';";
                    await Eval(fillJs);

                    // Нажатие кнопки
                    Log("Clicking Login button...");
                    string clickJs = "var btn = document.querySelector('input[name=\"Login\"]'); if(btn) btn.click();";
                    await Eval(clickJs);

                    // Ожидание
                    Log("Waiting for login (5s)...");
                    await Task.Delay(5000, token);

                    // Повторная проверка
                    Log("Checking login status again...");
                    resp = await Eval(checkJs);
                    if (resp.Contains("\"value\":true"))
                    {
                        Log("Авто-вход выполнен успешно.");
                        Log("Login successful.");
                        txtCrmStatus.Text = $"Вход ОК: {DateTime.Now:HH:mm}";
                    }
                    else
                    {
                        Log("Не удалось выполнить вход (проверка не прошла).");
                        Log("Login failed (Logout button not found).");
                        txtCrmStatus.Text = "Ошибка входа";
                    }
                }

                // 6. Закрытие вкладки
                Log($"Closing tab {tabId}...");
                try
                {
                    await http.GetStringAsync($"http://127.0.0.1:9222/json/close/{tabId}", token);
                    Log("Tab closed.");
                }
                catch (Exception ex)
                {
                    Log($"Error closing tab: {ex.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                Log("Авто-вход прерван пользователем.");
            }
            catch (Exception ex)
            {
                txtCrmStatus.Text = "Ошибка входа";
                Debug.WriteLine(ex.Message);
                Log($"КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}\n{ex.StackTrace}");
                // LogDetailed removed
            }
            Log("=== END CRM AUTO-LOGIN ===");
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
            var win = new Window
            {
                Title = title,
                Width = 350,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = (System.Windows.Media.Brush)FindResource("BrushBackground"),
                Foreground = System.Windows.Media.Brushes.White
            };

            var stack = new StackPanel { Margin = new Thickness(20) };

            var txtHeader = new TextBlock
            {
                Text = isError ? "❌ ОШИБКА" : "✅ УСПЕХ",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = isError ? System.Windows.Media.Brushes.IndianRed : System.Windows.Media.Brushes.LightGreen,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var txtMessage = new TextBlock
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
                HorizontalAlignment = HorizontalAlignment.Center
            };
            btnOk.Click += (s, e) => win.Close();

            stack.Children.Add(txtHeader);
            stack.Children.Add(txtMessage);
            stack.Children.Add(btnOk);

            win.Content = stack;
            win.ShowDialog();
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

        private async void ShowTempNotification(string message)
        {
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Width = 200,
                Height = 40,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ShowInTaskbar = false,
                Topmost = true,
                IsHitTestVisible = false
            };

            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E24")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B026FF")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5)
            };

            var text = new TextBlock
            {
                Text = message,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold
            };

            border.Child = text;
            win.Content = border;

            win.Show();
            await Task.Delay(1000);
            win.Close();
        }
    }

    public class HotkeyDisplay
    {
        public string Keys { get; set; } = "";
        public string Desc { get; set; } = "";
        public string FullCommand { get; set; } = "";
    }

    public class AppSettings
    {
        public double NotesFontSize { get; set; } = 14;
        public string CrmLogin { get; set; } = "";
        public string CrmPassword { get; set; } = "";
        public double WindowTop { get; set; } = 100;
        public double WindowLeft { get; set; } = 100;
        public double WindowWidth { get; set; } = 950;
        public double WindowHeight { get; set; } = 600;
        public int WindowState { get; set; } = 0;
        public string SelectedBrowser { get; set; } = "";
    }

    public class BrowserItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
    }
}