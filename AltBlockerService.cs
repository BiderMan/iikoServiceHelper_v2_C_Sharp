using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using static iikoServiceHelper.NativeMethods;

namespace iikoServiceHelper.Services
{
    public class AltBlockerService : IDisposable
    {
        private readonly IHotkeyManager? _hotkeyManager;
        private readonly Action<string> _logAction;

        private IntPtr _hookId = IntPtr.Zero;
        private readonly LowLevelKeyboardProc _proc;
        private bool _isAltDown = false;
        private bool _otherKeyDuringAlt = false;
        private Timer? _keyStateTimer;
        private int _altKeyDownCount = 0;
        private int _altKeyUpCount = 0;
        private DateTime _lastAltDownTime = DateTime.MinValue;
        private DateTime _lastAltUpTime = DateTime.MinValue;

        public AltBlockerService(IHotkeyManager? hotkeyManager, Action<string> logAction)
        {
            _hotkeyManager = hotkeyManager;
            _logAction = logAction;
            _proc = HookCallback;
        }

        public void Enable()
        {
            if (_hookId != IntPtr.Zero) return;
            try
            {
                _hookId = SetHook(_proc);
                _logAction("Global Alt Hook installed.");

                // Запускаем таймер контроля состояния
                _keyStateTimer = new Timer(CheckKeyState, null, 0, 100);
                _logAction("Key state monitor started (100ms interval).");
            }
            catch (Exception ex) { _logAction($"Hook Error: {ex.Message}"); }
        }

        public void Disable()
        {
            if (_hookId == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _logAction("Global Alt Hook removed.");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                bool isAlt = (vkCode == 164 || vkCode == 165 || vkCode == 18); // VK_LMENU, VK_RMENU, VK_MENU

                if (wParam == (IntPtr)0x0100 || wParam == (IntPtr)0x0104) // WM_KEYDOWN, WM_SYSKEYDOWN
                {
                    if (isAlt)
                    {
                        _isAltDown = true;
                        _otherKeyDuringAlt = false;

                        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0)
                            _otherKeyDuringAlt = true;
                    }
                    else if (_isAltDown)
                    {
                        _otherKeyDuringAlt = true;
                    }
                }
                else if (wParam == (IntPtr)0x0101 || wParam == (IntPtr)0x0105) // WM_KEYUP, WM_SYSKEYUP
                {
                    if (isAlt)
                    {
                        bool wasAltOnly = !_otherKeyDuringAlt;
                        _isAltDown = false;
                        _otherKeyDuringAlt = false; // Reset for next sequence

                        if (wasAltOnly && (_hotkeyManager == null || !_hotkeyManager.IsInputBlocked))
                        {
                            // FIX: The "stuck Alt" issue is caused by consuming the Alt-up event (returning 1).
                            // We will now let the event pass through to fix the stuck key. This may cause
                            // the browser menu to occasionally appear if Alt is pressed alone, but that is
                            // a minor issue compared to a stuck modifier key.
                            _logAction("Global Alt Hook: Lonely Alt-up detected. Passing event through to prevent stuck keys.");
                        }
                    }
                }
            }

            IntPtr currentHookId = _hookId;
            return CallNextHookEx(currentHookId, nCode, wParam, lParam);
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            try
            {
                using Process curProcess = Process.GetCurrentProcess();
                using ProcessModule? curModule = curProcess.MainModule;
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule?.ModuleName), 0);
            }
            catch (Exception ex)
            {
                // Логируем ошибку установки хука
                System.Diagnostics.Debug.WriteLine($"Failed to set keyboard hook: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            _keyStateTimer?.Dispose();
            Disable();
        }

        private void CheckKeyState(object state)
        {
            try
            {
                // Проверяем реальное состояние клавиши Alt через GetAsyncKeyState
                bool altPhysicallyDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;

                // Если наш внутренний флаг говорит, что ALT зажат, но физически он отпущен - сбрасываем
                if (_isAltDown && !altPhysicallyDown)
                {
                    _logAction($"⚠️ State mismatch detected: _isAltDown={_isAltDown}, real state={altPhysicallyDown}. Forcing ALT release.");
                    ForceReleaseAlt();
                }
                // Если физически ALT зажат, но наш флаг не сигнализирует об этом - обновляем
                else if (!_isAltDown && altPhysicallyDown)
                {
                    _logAction($"⚠️ State mismatch detected: _isAltDown={_isAltDown}, real state={altPhysicallyDown}. Updating internal state.");
                    _isAltDown = true;
                    _otherKeyDuringAlt = false;
                }
            }
            catch (Exception ex)
            {
                _logAction($"Error in CheckKeyState: {ex.Message}");
            }
        }

        private void ForceReleaseAlt()
        {
            try
            {
                _logAction("🔧 ForceReleaseAlt: Releasing ALT key via native methods...");

                // Используем улучшенный метод ReleaseModifiers из CommandExecutionService
                // Но здесь мы просто явно отправляем события отпускания ALT
                NativeMethods.ReleaseModifiers(NativeMethods.VK_LMENU, NativeMethods.VK_RMENU, NativeMethods.VK_MENU);

                // Принудительно сбрасываем наши флаги
                _isAltDown = false;
                _otherKeyDuringAlt = false;

                _logAction("✅ ForceReleaseAlt completed.");
            }
            catch (Exception ex)
            {
                _logAction($"❌ ForceReleaseAlt error: {ex.Message}");
            }
        }

        private void LogAltState(string context, int vkCode, bool isAlt, string eventType)
        {
            string inputBlockedStatus = _hotkeyManager?.IsInputBlocked.ToString() ?? "null";
            _logAction($"[{DateTime.Now:HH:mm:ss.fff}] {context}\n" +
                      $"  Event: {eventType}\n" +
                      $"  VK_CODE: {vkCode} (IsAlt: {isAlt})\n" +
                      $"  _isAltDown: {_isAltDown}\n" +
                      $"  _otherKeyDuringAlt: {_otherKeyDuringAlt}\n" +
                      $"  IsInputBlocked: {inputBlockedStatus}\n" +
                      $"  _altKeyDownCount: {_altKeyDownCount}\n" +
                      $"  _altKeyUpCount: {_altKeyUpCount}");
        }
    }
}