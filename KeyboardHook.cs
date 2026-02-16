using System;
using System.Diagnostics;

namespace iikoServiceHelper
{
    /// <summary>
    /// Реализация IKeyboardHook с использованием Windows API
    /// </summary>
    public class KeyboardHook : IKeyboardHook
    {
        public IntPtr SetHook(NativeMethods.LowLevelKeyboardProc proc)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            return NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, proc, NativeMethods.GetModuleHandle(curModule?.ModuleName), 0);
        }

        public IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam)
        {
            return NativeMethods.CallNextHookEx(hhk, nCode, wParam, lParam);
        }

        public void UnhookWindowsHookEx(IntPtr hhk)
        {
            NativeMethods.UnhookWindowsHookEx(hhk);
        }

        public short GetAsyncKeyState(int vKey)
        {
            return NativeMethods.GetAsyncKeyState(vKey);
        }

        public IntPtr GetModuleHandle(string? lpModuleName)
        {
            return NativeMethods.GetModuleHandle(lpModuleName);
        }

        public void Dispose()
        {
            // Ничего не нужно освобождать - статические методы
        }
    }
}
