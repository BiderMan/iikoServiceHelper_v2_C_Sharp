using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

        public void Dispose() => Disable();
    }
}