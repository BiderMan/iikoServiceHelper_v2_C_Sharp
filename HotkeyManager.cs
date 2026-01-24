using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace iikoServiceHelper
{
    public class HotkeyManager : IDisposable
    {
        public Func<string, bool>? HotkeyHandler; // Return true to suppress key
        public bool IsInputBlocked { get; set; }
        public bool IsAltPhysicallyDown { get; private set; }

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private readonly IntPtr _hookId;

        public HotkeyManager()
        {
            _proc = HookCallback;
            _hookId = SetHook(_proc);
        }

        private IntPtr SetHook(NativeMethods.LowLevelKeyboardProc proc)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            return NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                NativeMethods.GetModuleHandle(curModule?.ModuleName ?? ""), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // Track Alt Physical State
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == 164 || vkCode == 165 || vkCode == 18) // VK_LMENU, VK_RMENU, VK_MENU
                {
                    int altFlags = Marshal.ReadInt32(lParam, 8);
                    if ((altFlags & 0x10) == 0) // Not injected
                    {
                        if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                            IsAltPhysicallyDown = true;
                        else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                            IsAltPhysicallyDown = false;
                    }
                }

                if (wParam != (IntPtr)WM_KEYDOWN && wParam != (IntPtr)WM_SYSKEYDOWN)
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

                int flags = Marshal.ReadInt32(lParam, 8);
                if ((flags & 0x10) != 0) // Ignore injected keys (LLKHF_INJECTED)
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

                Key key = KeyInterop.KeyFromVirtualKey(vkCode);

                // Ignore modifier keys themselves
                if (key != Key.LeftAlt && key != Key.RightAlt && 
                    key != Key.LeftCtrl && key != Key.RightCtrl && 
                    key != Key.LeftShift && key != Key.RightShift &&
                    key != Key.LWin && key != Key.RWin && key != Key.System)
                {
                    var parts = new List<string>();
                    
                    // Check modifiers using GetKeyState for accuracy
                    bool alt = IsAltPhysicallyDown || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
                    bool ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
                    bool shift = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;

                    if (ctrl) parts.Add("Ctrl");
                    if (alt) parts.Add("Alt");
                    if (shift) parts.Add("Shift");
                    
                    parts.Add(key.ToString());

                    string combo = string.Join("+", parts);
                    
                    if (HotkeyHandler != null && HotkeyHandler.Invoke(combo))
                    {
                        return (IntPtr)1; // Suppress key
                    }
                }

                if (IsInputBlocked)
                    return (IntPtr)1; // Block input if not a hotkey
            }
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
        }
    }
}