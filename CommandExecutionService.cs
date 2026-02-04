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
        private readonly HotkeyManager _hotkeyManager;
        private readonly ILogger<CommandExecutionService> _logger;
        private readonly AppSettings _settings;

        private readonly Queue<(string Command, object? Parameter, string HotkeyName)> _commandQueue = new();
        private readonly object _queueLock = new();
        private volatile bool _isQueueRunning = false;
        private string _currentActionName = "";
        private volatile bool _cancelCurrentAction = false;
        private CancellationTokenSource? _currentCts;

        public CommandExecutionService(HotkeyManager hotkeyManager, ILogger<CommandExecutionService> logger, AppSettings settings)
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
                    Task.Run(ProcessQueue);
                }
            }
        }

        private async void ProcessQueue()
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
                    catch (Exception ex) { Log($"Action failed: {ex.Message}", LogLevel.Error); }
                    sw.Stop();
                    Log($"Finished item: {item.HotkeyName}. Duration: {sw.ElapsedMilliseconds}ms");
                }

                _host?.RunOnUIThread(async () =>
                {
                    await Task.Delay(250); // Короткая задержка перед скрытием
                    if (!_isQueueRunning) _host?.HideOverlay();
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
                    if (!_isQueueRunning) {
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
                        await TypeText("@chat_bot", token);
                        await Task.Delay(_settings.Delays.ActionPause, token); CheckCancellation(token);
                        NativeMethods.SendKey(NativeMethods.VK_RETURN);
                        if (parameter is string args && !string.IsNullOrEmpty(args))
                        {
                            await Task.Delay(200, token); CheckCancellation(token);
                            NativeMethods.SendKey(NativeMethods.VK_SPACE);
                            await Task.Delay(_settings.Delays.KeyPress, token); CheckCancellation(token);
                            await TypeText(args, token);
                        }
                        break;

                    case "BotCall":
                        await TypeText("@chat_bot", token);
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
                            await Task.Delay(150, token); CheckCancellation(token);
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

        private async Task TypeText(string text, CancellationToken token)
        {
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
            var en = "qwertyuiop[]asdfghjkl;'zxcvbnm,.";
            var ru = "йцукенгшщзхъфывапролджэячсмитьбю";
            var enCap = "QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>";
            var ruCap = "ЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ";
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
    }
}