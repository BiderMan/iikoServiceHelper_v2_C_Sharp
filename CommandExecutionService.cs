using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using iikoServiceHelper.Models;
using iikoServiceHelper.Utils;

namespace iikoServiceHelper.Services
{
    /// <summary>
    /// Сервис для последовательного выполнения команд и макросов.
    /// </summary>
    public class CommandExecutionService : ICommandExecutionService
    {
        private ICommandHost? _host;
        private readonly IHotkeyManager _hotkeyManager;
        private readonly ILogger<CommandExecutionService> _logger;
        private readonly AppSettings _settings;

        private readonly Queue<(string Command, object? Parameter, string HotkeyName)> _commandQueue = new();
        private readonly object _queueLock = new();
        private volatile bool _isQueueRunning = false;
        private string _currentActionName = "";
        private volatile bool _cancelCurrentAction = false;
        private int _pasteCount = 0;
        private CancellationTokenSource? _currentCts;
        private DateTime _clipboardStartTime = DateTime.MinValue;
        private CancellationTokenSource? _cleanupTimerCts;
        private const int CleanupDelayMs = 1500; // 1.5 секунды задержки перед очисткой

        public CommandExecutionService(IHotkeyManager hotkeyManager, ILogger<CommandExecutionService> logger, AppSettings settings)
        {
            _hotkeyManager = hotkeyManager;
            _logger = logger;
            _settings = settings;
        }

        public void SetHost(ICommandHost host)
        {
            _host = host;
        }

        /// <summary>
        /// Добавляет команду в очередь выполнения.
        /// </summary>
        public void Enqueue(string command, object? parameter, string hotkeyName)
        {
            lock (_queueLock)
            {
                _commandQueue.Enqueue((command, parameter, hotkeyName));
                Log($"Enqueued: {hotkeyName}. Queue size: {_commandQueue.Count}");

                if (_isQueueRunning)
                {
                    _host?.RunOnUIThread(UpdateOverlayMessage);
                }

                if (!_isQueueRunning)
                {
                    _isQueueRunning = true;
                    _ = Task.Run(ProcessQueueAsync);
                }
            }
        }

        private async Task ProcessQueueAsync()
        {
            try
            {
                Log("Queue processor started.");
                while (true)
                {
                    (string Command, object? Parameter, string HotkeyName) item;
                    int queueCount;
                    lock (_queueLock)
                    {
                        if (_commandQueue.Count == 0)
                        {
                            Log("Queue empty. Processor stopping.");
                            _isQueueRunning = false;
                            _currentActionName = ""; // Очищаем состояние, когда очередь пуста
                            break;
                        }
                        item = _commandQueue.Dequeue();
                        queueCount = _commandQueue.Count;
                        _currentActionName = StringUtils.FormatKeyCombo(item.HotkeyName);
                    }
                    Log($"Processing item: {item.HotkeyName}. Remaining in queue: {queueCount}");

                    _host?.RunOnUIThread(() =>
                    {
                        _host?.IncrementCommandCount();
                        UpdateOverlayMessage();
                    });

                    _cancelCurrentAction = false;
                    _currentCts?.Dispose();
                    _currentCts = new CancellationTokenSource();

                    var sw = Stopwatch.StartNew();
                    try
                    {
                        await ExecuteCommand(item.Command, item.Parameter, _currentCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Log($"Action canceled: {item.HotkeyName}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Action failed: {ex.Message}", LogLevel.Error);
                        Log($"Exception type: {ex.GetType().Name}", LogLevel.Error);
                        Log($"Stack trace: {ex.StackTrace}", LogLevel.Error);
                    }
                    sw.Stop();
                    Log($"Finished item: {item.HotkeyName}. Duration: {sw.ElapsedMilliseconds}ms");
                }

                _host?.RunOnUIThread(() =>
                {
                    // Запускаем асинхронную операцию очистки
                    _ = ProcessQueueCleanupAsync();
                });
            }
            catch (Exception ex)
            {
                Log($"CRITICAL QUEUE ERROR: {ex.Message}", LogLevel.Critical);
                lock (_queueLock)
                {
                    _isQueueRunning = false;
                    _commandQueue.Clear();
                }
            }
        }

        private void Log(string message, LogLevel level = LogLevel.Information)
        {
            _logger.Log(level, message);
            _host?.LogDetailed(message);
        }

        /// <summary>
        /// Обрабатывает очистку буфера обмена после завершения очереди команд.
        /// </summary>
        private async Task ProcessQueueCleanupAsync()
        {
            // "Умная" очистка: выполняется один раз после завершения всей очереди
            // С таймером для ожидания новых команд
            if (_pasteCount > 0)
            {
                Log($"Queue finished. Starting cleanup timer ({CleanupDelayMs}ms). Paste count: {_pasteCount}");
                
                // Отменяем предыдущий таймер, если есть
                _cleanupTimerCts?.Cancel();
                _cleanupTimerCts?.Dispose();
                _cleanupTimerCts = new CancellationTokenSource();
                
                try
                {
                    // Ждём указанное время
                    await Task.Delay(CleanupDelayMs, _cleanupTimerCts.Token);
                    
                    // Проверяем, не была ли очередь снова запущена
                    if (!_isQueueRunning && _pasteCount > 0)
                    {
                        var startTime = _clipboardStartTime;
                        var endTime = DateTime.Now;
                        
                        Log($"Cleanup timer elapsed. Cleaning clipboard history from {startTime:HH:mm:ss.fff} to {endTime:HH:mm:ss.fff}");
                        
                        int countToClean = _pasteCount;
                        _pasteCount = 0; // Сбрасываем счетчик
                        _clipboardStartTime = DateTime.MinValue; // Сбрасываем время
                        
                        if (_host != null)
                        {
                            await _host.ClearClipboardHistoryByTimeRangeAsync(startTime, endTime);
                        }
                        Log("Clipboard history cleaned.");
                    }
                    else
                    {
                        Log("Cleanup skipped - queue became active again.");
                    }
                }
                catch (TaskCanceledException)
                {
                    // Таймер был отменён - новая команда поступила
                    Log("Cleanup timer cancelled - new command received.");
                }
            }

            await Task.Delay(250); // Короткая задержка перед скрытием
            if (!_isQueueRunning) _host?.HideOverlay();
        }

        private void UpdateOverlayMessage()
        {
            string msg;
            int count;
            // Читаем все связанные данные в одном блоке lock для консистентности
            lock (_queueLock)
            {
                msg = _currentActionName;
                count = _commandQueue.Count;
            }
            if (count > 0) msg += $" (Очередь: {count})";
            _host?.UpdateOverlay(msg);
        }

        /// <summary>
        /// Принудительно очищает очередь команд и отменяет текущую операцию.
        /// </summary>
        public void ClearQueue()
        {
            _cancelCurrentAction = true;
            _currentCts?.Cancel();
            int clearedCount;
            bool wasRunning;
            lock (_queueLock)
            {
                clearedCount = _commandQueue.Count;
                _commandQueue.Clear();
                _pasteCount = 0; // Сбрасываем счетчик вставленных элементов
                wasRunning = _isQueueRunning;
            }

            Log($"Очередь команд принудительно очищена (удалено {clearedCount}, прервано текущее).", LogLevel.Warning);
            _host?.RunOnUIThread(async () =>
            {
                _host?.UpdateOverlay("Очередь очищена");
                // Если обработчик очереди не был запущен, он не сможет скрыть оверлей.
                // Берем эту задачу на себя.
                if (!wasRunning)
                {
                    await Task.Delay(1000); // Показываем сообщение 1 секунду
                    // Перепроверяем, не запустилось ли что-то новое за это время
                    if (!_isQueueRunning)
                    {
                        _host?.HideOverlay();
                    }
                }
            });
        }

        private void CheckCancellation(CancellationToken token)
        {
            if (_cancelCurrentAction) throw new OperationCanceledException();
            token.ThrowIfCancellationRequested();
        }

        private async Task ExecuteCommand(string command, object? parameter, CancellationToken token)
        {
            await WaitForInputFocus(token);
            Log($"[START] Execute {command}. Param: {parameter}");
            var sw = Stopwatch.StartNew();
            _hotkeyManager.IsInputBlocked = true;
            try
            {
                CheckCancellation(token);
                SafeReleaseModifiers();
                NativeMethods.ReleaseAlphaKeys();
                Log($"Modifiers released. Sleep {_settings.Delays.ActionPause}ms.");
                await Task.Delay(_settings.Delays.ActionPause, token);
                CheckCancellation(token);

                switch (command)
                {
                    case "Bot":
                        await TypeText("@chat_bot", token, forceTypeMode: true);
                        await Task.Delay(_settings.Delays.ActionPause, token); CheckCancellation(token);
                        NativeMethods.SendKey(NativeMethods.VK_RETURN);
                        if (parameter is string args && !string.IsNullOrEmpty(args))
                        {
                            await Task.Delay(200, token); CheckCancellation(token);
                            NativeMethods.SendKey(NativeMethods.VK_SPACE);
                            await Task.Delay(_settings.Delays.KeyPress, token); CheckCancellation(token);
                            await TypeText(args, token, forceTypeMode: true);
                        }
                        break;

                    case "BotCall":
                        await TypeText("@chat_bot", token, forceTypeMode: true);
                        await Task.Delay(_settings.Delays.ActionPause, token); CheckCancellation(token);
                        NativeMethods.SendKey(NativeMethods.VK_RETURN);
                        await Task.Delay(_settings.Delays.ActionPause, token); CheckCancellation(token);
                        NativeMethods.ReleaseModifiers();
                        NativeMethods.SendKey(NativeMethods.VK_RETURN);
                        break;

                    case "Reply":
                        if (parameter is string text)
                        {
                            await TypeText(text, token);
                            // Небольшая задержка, чтобы приложение-получатель успело обработать ввод/вставку перед нажатием Enter.
                            await Task.Delay(50, token); CheckCancellation(token);
                            NativeMethods.SendKey(NativeMethods.VK_RETURN);
                        }
                        break;

                    case "FixLayout":
                        await FixLayout(token);
                        break;
                }
            }
            finally
            {
                _hotkeyManager.IsInputBlocked = false;
                if (_hotkeyManager.IsAltPhysicallyDown)
                {
                    NativeMethods.PressAltDown();
                    Log("Restored Alt key.");
                }
                sw.Stop();
                Log($"[END] Execute {command}. Total duration: {sw.ElapsedMilliseconds}ms");
            }
        }

        private async Task WaitForInputFocus(CancellationToken token)
        {
            if (_host != null && _host.IsInputFocused()) return;

            Log("Waiting for input focus...");
            while (_host != null && !_host.IsInputFocused())
            {
                CheckCancellation(token);
                _host.RunOnUIThread(() => _host.UpdateOverlay("Ожидание поля ввода..."));
                await Task.Delay(_settings.Delays.FocusWait, token);
            }
            Log("Input focus detected.");
            _host?.RunOnUIThread(UpdateOverlayMessage);
        }

        private async Task TypeText(string text, CancellationToken token, bool forceTypeMode = false)
        {
            // Проверяем, нужно ли использовать режим вставки для быстрых ответов
            if (!forceTypeMode && _settings.UsePasteModeForQuickReplies)
            {
                Log($"Режим вставки активен. Вставляем текст: {text}");
                // Режим вставки: копируем в буфер обмена, вставляем, затем очищаем историю буфера
                
                // Фиксируем время начала вставки при первой операции
                if (_clipboardStartTime == DateTime.MinValue)
                {
                    _clipboardStartTime = DateTime.Now;
                    Log($"Зафиксировано время начала вставки: {_clipboardStartTime:HH:mm:ss.fff}");
                }
                
                try
                {
                    _host?.RunOnUIThread(() => _host.ClipboardSetText(text));
                    // Задержка перед вставкой не нужна, т.к. RunOnUIThread (Dispatcher.Invoke) - блокирующий вызов.

                    // Используем более прямой способ вставки через нативный метод
                    NativeMethods.SendCtrlV();
                    Log("Выполнена вставка через NativeMethods.SendCtrlV()");
                    _pasteCount++;
                    Log($"Paste operation registered. Total pastes in queue: {_pasteCount}");
                    
                    // Очищаем буфер обмена сразу после вставки
                    _host?.RunOnUIThread(() => _host.ClipboardClear());
                    Log("Буфер обмена очищен после вставки.");
                    
                    // Задержка после вставки перенесена в вызывающий метод (ExecuteCommand), чтобы она была непосредственно перед нажатием Enter.
                }
                catch (Exception ex)
                {
                    Log($"Ошибка в режиме вставки: {ex.Message}", LogLevel.Warning);
                    // В случае ошибки возвращаемся к обычному режиму ввода
                    await TypeTextNormal(text, token);
                }
            }
            else
            {
                if (forceTypeMode)
                {
                    Log($"Принудительный режим ввода для команды БОТА. Вводим текст: {text}");
                }
                else
                {
                    Log($"Обычный режим ввода. Вводим текст: {text}");
                }
                // Обычный режим ввода
                await TypeTextNormal(text, token);
            }
        }

        private async Task TypeTextNormal(string text, CancellationToken token)
        {
            text = text.TrimEnd('\r', '\n');
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                CheckCancellation(token);
                if (!string.IsNullOrEmpty(lines[i]))
                {
                    NativeMethods.SendText(lines[i]);
                    await Task.Delay(_settings.Delays.KeyPress, token);
                }
                if (i < lines.Length - 1)
                {
                    CheckCancellation(token);
                    await Task.Delay(_settings.Delays.KeyPress, token);
                    NativeMethods.SendKey(NativeMethods.VK_RETURN);
                    await Task.Delay(_settings.Delays.KeyPress, token);
                }
            }
        }

        private void SafeReleaseModifiers()
        {
            if (_hotkeyManager.IsAltPhysicallyDown)
            {
                NativeMethods.SendKey(0x11); // Ctrl
                Thread.Sleep(20);
            }
            NativeMethods.ReleaseModifiers();
        }

        private async Task FixLayout(CancellationToken token)
        {
            _host?.RunOnUIThread(() => _host.ClipboardClear());

            _host?.SendKeysWait("^c");
            await Task.Delay(_settings.Delays.KeyPress, token);

            string? text = null;
            _host?.RunOnUIThread(() => { if (_host.ClipboardContainsText()) text = _host.ClipboardGetText(); });

            if (!string.IsNullOrEmpty(text))
            {
                string fixedText = ConvertLayout(text);
                if (text != fixedText)
                {
                    _host?.RunOnUIThread(() => _host.ClipboardSetText(fixedText));

                    _host?.SendKeysWait("^v");
                    await Task.Delay(_settings.Delays.ActionPause, token);

                    if (_host != null) await _host.CleanClipboardHistoryAsync(2);
                }
            }
        }

        private string ConvertLayout(string text)
        {
            var layoutMap = new Dictionary<char, char>
            {
                // English lowercase to Russian
                {'q', 'й'}, {'w', 'ц'}, {'e', 'у'}, {'r', 'к'}, {'t', 'е'}, {'y', 'н'}, {'u', 'г'}, {'i', 'ш'}, {'o', 'щ'}, {'p', 'з'},
                {'[', 'х'}, {']', 'ъ'}, {'a', 'ф'}, {'s', 'ы'}, {'d', 'в'}, {'f', 'а'}, {'g', 'п'}, {'h', 'р'}, {'j', 'о'}, {'k', 'л'},
                {'l', 'д'}, {'\'', 'ж'}, {'z', 'я'}, {'x', 'ч'}, {'c', 'с'}, {'v', 'м'}, {'b', 'и'}, {'n', 'т'}, {'m', 'ь'}, {'.', 'ю'},
                // Russian to English lowercase
                {'й', 'q'}, {'ц', 'w'}, {'у', 'e'}, {'к', 'r'}, {'е', 't'}, {'н', 'y'}, {'г', 'u'}, {'ш', 'i'}, {'щ', 'o'}, {'з', 'p'},
                {'х', '['}, {'ъ', ']'}, {'ф', 'a'}, {'ы', 's'}, {'в', 'd'}, {'а', 'f'}, {'п', 'g'}, {'р', 'h'}, {'о', 'j'}, {'л', 'k'},
                {'д', 'l'}, {'ж', ';'}, {'э', '\''}, {'я', 'z'}, {'ч', 'x'}, {'с', 'c'}, {'м', 'v'}, {'и', 'b'}, {'т', 'n'}, {'ь', 'm'},
                {',', ','}, {'ю', '.'},
                // English uppercase to Russian
                {'Q', 'Й'}, {'W', 'Ц'}, {'E', 'У'}, {'R', 'К'}, {'T', 'Е'}, {'Y', 'Н'}, {'U', 'Г'}, {'I', 'Ш'}, {'O', 'Щ'}, {'P', 'З'},
                {'{', 'Х'}, {'}', 'Ъ'}, {'A', 'Ф'}, {'S', 'Ы'}, {'D', 'В'}, {'F', 'А'}, {'G', 'П'}, {'H', 'Р'}, {'J', 'О'}, {'K', 'Л'},
                {'L', 'Д'}, {':', 'Ж'}, {'"', '"'}, {'Z', 'Я'}, {'X', 'Ч'}, {'C', 'С'}, {'V', 'М'}, {'B', 'И'}, {'N', 'Т'}, {'M', 'Ь'},
                {'<', '<'}, {'>', '>'},
                // Russian to English uppercase
                {'Й', 'Q'}, {'Ц', 'W'}, {'У', 'E'}, {'К', 'R'}, {'Е', 'T'}, {'Н', 'Y'}, {'Г', 'U'}, {'Ш', 'I'}, {'Щ', 'O'}, {'З', 'P'},
                {'Х', '{'}, {'Ъ', '}'}, {'Ф', 'A'}, {'Ы', 'S'}, {'В', 'D'}, {'А', 'F'}, {'П', 'G'}, {'Р', 'H'}, {'О', 'J'}, {'Л', 'K'},
                {'Д', 'L'}, {'Ж', ':'}, {'Э', '"'}, {'Я', 'Z'}, {'Ч', 'X'}, {'С', 'C'}, {'М', 'V'}, {'И', 'B'}, {'Т', 'N'}, {'Ь', 'M'},
                {'Б', '<'}, {'Ю', '>'},
                // Symbols
                {'`', 'ё'}, {'~', 'Ё'}, {'@', '"'}, {'#', '№'}, {'$', ';'}, {'^', ':'}, {'&', '?'}, {'/', '.'}, {'?', ','}, {'|', '/'}, {'\\', '\\'}
            };

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (layoutMap.TryGetValue(c, out char converted))
                {
                    sb.Append(converted);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}