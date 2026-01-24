using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace iikoServiceHelper
{
    public static class NativeMethods
    {
        // --- INPUT SIMULATION ---
        public const ushort VK_CONTROL = 0x11;
        public const ushort VK_SHIFT = 0x10;
        public const ushort VK_MENU = 0x12; // Alt
        public const ushort VK_V = 0x56;
        public const ushort VK_RETURN = 0x0D;
        public const ushort VK_SPACE = 0x20;

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT 
        { 
            public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; 
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_UNICODE = 0x0004;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public static void SendCtrlV()
        {
            // Ctrl Down, V Down, V Up, Ctrl Up
            SendKeyStroke(VK_CONTROL, false);
            Thread.Sleep(20);
            SendKeyStroke(VK_V, false);
            Thread.Sleep(20);
            SendKeyStroke(VK_V, true);
            Thread.Sleep(20);
            SendKeyStroke(VK_CONTROL, true);
        }

        public static void SendKey(ushort vk)
        {
            SendKeyStroke(vk, false);
            Thread.Sleep(20);
            SendKeyStroke(vk, true);
        }

        public static void SendText(string text)
        {
            foreach (char c in text)
            {
                INPUT[] inputs = new INPUT[2];

                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].u.ki.wVk = 0;
                inputs[0].u.ki.wScan = c;
                inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;

                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].u.ki.wVk = 0;
                inputs[1].u.ki.wScan = c;
                inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

                SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                Thread.Sleep(1); // Small delay to ensure app processes char
            }
        }

        public static void ReleaseModifiers()
        {
            // Force release of modifier keys to prevent "sticking" during injection
            bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

            if (alt)
            {
                // Prevent Menu bar activation by pressing Ctrl before releasing Alt
                if (!ctrl)
                {
                    SendKeyStroke(VK_CONTROL, false);
                    Thread.Sleep(5);
                }
                SendKeyStroke(VK_MENU, true);
                Thread.Sleep(5);
                if (!ctrl)
                {
                    SendKeyStroke(VK_CONTROL, true);
                }
            }
            if (ctrl) 
                SendKeyStroke(VK_CONTROL, true);
            if (shift) 
                SendKeyStroke(VK_SHIFT, true);
        }

        public static void ReleaseAlphaKeys()
        {
            // Release A-Z keys to prevent conflicts (like Alt+C -> Ctrl+C)
            for (int i = 0x41; i <= 0x5A; i++)
            {
                if ((GetAsyncKeyState(i) & 0x8000) != 0)
                {
                    SendKeyStroke((ushort)i, true);
                }
            }
        }

        public static void PressAltDown()
        {
            SendKeyStroke(VK_MENU, false);
        }

        private static void SendKeyStroke(ushort vk, bool up)
        {
            INPUT input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        dwFlags = up ? KEYEVENTF_KEYUP : 0
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        // --- HOOKS ---
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
        
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);
    }
}