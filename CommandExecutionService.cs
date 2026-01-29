using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using iikoServiceHelper.Utils;

namespace iikoServiceHelper.Services
{
    public class CommandExecutionService
    {
        private readonly ICommandHost _host;
        private readonly HotkeyManager _hotkeyManager;

        private readonly Queue<(string Command, object? Parameter, string HotkeyName)> _commandQueue = new();
        private readonly object _queueLock = new();
        private volatile bool _isQueueRunning = false;
        private string _currentActionName = "";
        private volatile bool _cancelCurrentAction = false;

        public CommandExecutionService(ICommandHost host, HotkeyManager hotkeyManager)
        {
            _host = host;
            _hotkeyManager = hotkeyManager;
        }

        public void Enqueue(string command, object? parameter, string hotkeyName)
        {
            lock (_queueLock)
            {
                _commandQueue.Enqueue((command, parameter, hotkeyName));
                _host.LogDetailed($"Enqueued: {hotkeyName}. Queue size: {_commandQueue.Count}");

                if (_isQueueRunning)
                {
                    _host.RunOnUIThread(UpdateOverlayMessage);
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
                _host.LogDetailed("Queue processor started.");
                while (true)
                {
                    (string Command, object? Parameter, string HotkeyName) item;
                    int queueCount;
                    lock (_queueLock)
                    {
                        if (_commandQueue.Count == 0)
                        {
                            _host.LogDetailed("Queue empty. Processor stopping.");
                            _isQueueRunning = false;
                            _currentActionName = ""; // Очищаем состояние, когда очередь пуста
                            break;
                        }
                        item = _commandQueue.Dequeue();
                        queueCount = _commandQueue.Count;
                        _currentActionName = StringUtils.FormatKeyCombo(item.HotkeyName);
                    }
                    _host.LogDetailed($"Processing item: {item.HotkeyName}. Remaining in queue: {queueCount}");

                    _host.RunOnUIThread(() =>
                    {
                        _host.IncrementCommandCount();
                        UpdateOverlayMessage();
                    });

                    _cancelCurrentAction = false;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        await ExecuteCommand(item.Command, item.Parameter);
                    }
                    catch (OperationCanceledException)
                    {
                        _host.LogDetailed($"Action canceled: {item.HotkeyName}");
                    }
                    catch (Exception ex) { _host.LogDetailed($"Action failed: {ex.Message}"); }
                    sw.Stop();
                    _host.LogDetailed($"Finished item: {item.HotkeyName}. Duration: {sw.ElapsedMilliseconds}ms");
                }

                _host.RunOnUIThread(async () =>
                {
                    await Task.Delay(250); // Короткая задержка перед скрытием
                    if (!_isQueueRunning) _host.HideOverlay();
                });
            }
            catch (Exception ex)
            {
                _host.LogDetailed($"CRITICAL QUEUE ERROR: {ex.Message}");
                lock (_queueLock)
                {
                    _isQueueRunning = false;
                    _commandQueue.Clear();
                }
            }
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
            _host.UpdateOverlay(msg);
        }

        public void ClearQueue()
        {
            _cancelCurrentAction = true;
            int clearedCount;
            bool wasRunning;
            lock (_queueLock)
            {
                clearedCount = _commandQueue.Count;
                _commandQueue.Clear();
                wasRunning = _isQueueRunning;
            }

            _host.LogDetailed($"Очередь команд принудительно очищена (удалено {clearedCount}, прервано текущее).");
            _host.RunOnUIThread(async () =>
            {
                _host.UpdateOverlay("Очередь очищена");
                // Если обработчик очереди не был запущен, он не сможет скрыть оверлей.
                // Берем эту задачу на себя.
                if (!wasRunning)
                {
                    await Task.Delay(1000); // Показываем сообщение 1 секунду
                    // Перепроверяем, не запустилось ли что-то новое за это время
                    if (!_isQueueRunning) {
                        _host.HideOverlay();
                    }
                }
            });
        }

        private void CheckCancellation()
        {
            if (_cancelCurrentAction) throw new OperationCanceledException();
        }

        private async Task ExecuteCommand(string command, object? parameter)
        {
            WaitForInputFocus();
            _host.LogDetailed($"[START] Execute {command}. Param: {parameter}");
            var sw = Stopwatch.StartNew();
            _hotkeyManager.IsInputBlocked = true;
            try
            {
                CheckCancellation();
                SafeReleaseModifiers();
                NativeMethods.ReleaseAlphaKeys();
                _host.LogDetailed("Modifiers released. Sleep 100ms.");
                Thread.Sleep(100);
                CheckCancellation();

                switch (command)
                {
                    case "Bot":
                        TypeText("@chat_bot");
                        Thread.Sleep(100); CheckCancellation();
                        NativeMethods.SendKey(NativeMethods.VK_RETURN);
                        if (parameter is string args && !string.IsNullOrEmpty(args))
                        {
                            Thread.Sleep(200); CheckCancellation();
                            NativeMethods.SendKey(NativeMethods.VK_SPACE);
                            Thread.Sleep(50); CheckCancellation();
                            TypeText(args);
                        }
                        break;

                    case "BotCall":
                        TypeText("@chat_bot");
                        Thread.Sleep(100); CheckCancellation();
                        NativeMethods.SendKey(NativeMethods.VK_RETURN);
                        Thread.Sleep(100); CheckCancellation();
                        NativeMethods.ReleaseModifiers();
                        NativeMethods.SendKey(NativeMethods.VK_RETURN);
                        break;

                    case "Reply":
                        if (parameter is string text)
                        {
                            TypeText(text);
                            Thread.Sleep(150); CheckCancellation();
                            NativeMethods.SendKey(NativeMethods.VK_RETURN);
                        }
                        break;

                    case "FixLayout":
                        await FixLayout();
                        break;
                }
            }
            finally
            {
                _hotkeyManager.IsInputBlocked = false;
                if (_hotkeyManager.IsAltPhysicallyDown)
                {
                    NativeMethods.PressAltDown();
                    _host.LogDetailed("Restored Alt key.");
                }
                sw.Stop();
                _host.LogDetailed($"[END] Execute {command}. Total duration: {sw.ElapsedMilliseconds}ms");
            }
        }

        private void WaitForInputFocus()
        {
            if (_host.IsInputFocused()) return;

            _host.LogDetailed("Waiting for input focus...");
            while (!_host.IsInputFocused())
            {
                CheckCancellation();
                _host.RunOnUIThread(() => _host.UpdateOverlay("Ожидание поля ввода..."));
                Thread.Sleep(500);
            }
            _host.LogDetailed("Input focus detected.");
            _host.RunOnUIThread(UpdateOverlayMessage);
        }

        private void TypeText(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                CheckCancellation();
                if (!string.IsNullOrEmpty(lines[i]))
                {
                    NativeMethods.SendText(lines[i]);
                    Thread.Sleep(50);
                }
                if (i < lines.Length - 1)
                {
                    CheckCancellation();
                    Thread.Sleep(50);
                    NativeMethods.SendKey(NativeMethods.VK_RETURN);
                    Thread.Sleep(50);
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

        private async Task FixLayout()
        {
            _host.RunOnUIThread(() => _host.ClipboardClear());

            _host.SendKeysWait("^c");
            Thread.Sleep(50);

            string? text = null;
            _host.RunOnUIThread(() => { if (_host.ClipboardContainsText()) text = _host.ClipboardGetText(); });

            if (!string.IsNullOrEmpty(text))
            {
                string fixedText = ConvertLayout(text);
                if (text != fixedText)
                {
                    _host.RunOnUIThread(() => _host.ClipboardSetText(fixedText));

                    _host.SendKeysWait("^v");
                    Thread.Sleep(100);

                    await _host.CleanClipboardHistoryAsync(2);
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