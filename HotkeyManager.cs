using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace iikoServiceHelper
{
    public class HotkeyManager : IHotkeyManager
    {
        public Func<string, bool>? HotkeyHandler { get; set; }
        public bool IsInputBlocked { get; set; }
        public bool IsAltPhysicallyDown { get; private set; }
        public bool IsCtrlPhysicallyDown { get; private set; }
        public bool IsShiftPhysicallyDown { get; private set; }

        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private readonly IntPtr _hookId;
        private readonly IKeyboardHook _keyboardHook;

        public HotkeyManager() : this(new KeyboardHook())
        {
        }

        public HotkeyManager(IKeyboardHook keyboardHook)
        {
            _keyboardHook = keyboardHook;
            
            // Инициализируем состояние (на случай, если клавиши уже зажаты при старте)
            IsAltPhysicallyDown = (_keyboardHook.GetAsyncKeyState(18) & 0x8000) != 0;
            IsCtrlPhysicallyDown = (_keyboardHook.GetAsyncKeyState(17) & 0x8000) != 0;
            IsShiftPhysicallyDown = (_keyboardHook.GetAsyncKeyState(16) & 0x8000) != 0;

            _proc = HookCallback;
            _hookId = _keyboardHook.SetHook(_proc);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    int flags = Marshal.ReadInt32(lParam, 8);
                    bool isInjected = (flags & 0x10) != 0;

                    const int WM_KEYDOWN = 0x0100;
                    const int WM_SYSKEYDOWN = 0x0104;
                    // const int WM_KEYUP = 0x0101;
                    // const int WM_SYSKEYUP = 0x0105;

                    // Отслеживаем ФИЗИЧЕСКОЕ состояние модификаторов (игнорируем программные нажатия)
                    if (!isInjected)
                    {
                        bool isDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                        if (vkCode == 164 || vkCode == 165 || vkCode == 18) IsAltPhysicallyDown = isDown;
                        if (vkCode == 162 || vkCode == 163 || vkCode == 17) IsCtrlPhysicallyDown = isDown;
                        if (vkCode == 160 || vkCode == 161 || vkCode == 16) IsShiftPhysicallyDown = isDown;
                    }

                    if (wParam != (IntPtr)WM_KEYDOWN && wParam != (IntPtr)WM_SYSKEYDOWN)
                        return _keyboardHook.CallNextHookEx(_hookId, nCode, wParam, lParam);

                    // Игнорируем инжектированные клавиши для определения хоткеев
                    if (isInjected)
                        return _keyboardHook.CallNextHookEx(_hookId, nCode, wParam, lParam);

                    Keys key = (Keys)vkCode;

                    // Ignore modifier keys themselves
                    bool isModifier = (key == Keys.LShiftKey || key == Keys.RShiftKey ||
                                       key == Keys.LControlKey || key == Keys.RControlKey ||
                                       key == Keys.LMenu || key == Keys.RMenu ||
                                       key == Keys.LWin || key == Keys.RWin ||
                                       key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu);

                    if (!isModifier)
                    {
                        var parts = new List<string>();

                        // Используем только физическое состояние, чтобы избежать ложных срабатываний от наших же макросов
                        bool alt = IsAltPhysicallyDown;
                        bool ctrl = IsCtrlPhysicallyDown;
                        bool shift = IsShiftPhysicallyDown;

                        if (ctrl) parts.Add("Ctrl");
                        if (alt) parts.Add("Alt");
                        if (shift) parts.Add("Shift");

                        string keyName;
                        switch (key)
                        {
                            case >= Keys.D0 and <= Keys.D9:
                                keyName = (key - Keys.D0).ToString();
                                break;
                            case >= Keys.NumPad0 and <= Keys.NumPad9:
                                keyName = "NumPad" + (key - Keys.NumPad0).ToString();
                                break;
                            case Keys.Oem1: // OemSemicolon
                                keyName = ";";
                                break;
                            case Keys.OemQuestion: // Oem2, /?
                                keyName = "/";
                                break;
                            case Keys.Oemtilde: // Oem3, `~
                                keyName = "`";
                                break;
                            case Keys.OemOpenBrackets: // Oem4, [{
                                keyName = "[";
                                break;
                            case Keys.OemPipe: // Oem5, \|
                                keyName = "\\";
                                break;
                            case Keys.OemCloseBrackets: // Oem6, ]}
                                keyName = "]";
                                break;
                            case Keys.OemQuotes: // Oem7, '"
                                keyName = "'";
                                break;
                            default:
                                keyName = key.ToString();
                                break;
                        }
                        parts.Add(keyName);

                        string combo = string.Join("+", parts);

                        if (HotkeyHandler != null && HotkeyHandler.Invoke(combo))
                        {
                            return (IntPtr)1; // Suppress key
                        }
                    }

                    if (IsInputBlocked)
                        return (IntPtr)1; // Block input if not a hotkey
                }
                catch (Exception ex)
                {
                    // Логируем ошибку для диагностики
                    System.Diagnostics.Debug.WriteLine($"HotkeyManager Error: {ex}");
                }
            }
            return _keyboardHook.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            _keyboardHook.UnhookWindowsHookEx(_hookId);
        }
    }
}