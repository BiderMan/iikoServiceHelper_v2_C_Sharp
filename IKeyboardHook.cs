using System;

namespace iikoServiceHelper
{
    /// <summary>
    /// Интерфейс для работы с Windows keyboard hooks
    /// </summary>
    public interface IKeyboardHook : IDisposable
    {
        /// <summary>
        /// Установить хук клавиатуры
        /// </summary>
        IntPtr SetHook(NativeMethods.LowLevelKeyboardProc proc);
        
        /// <summary>
        /// Следующий хук в цепочке
        /// </summary>
        IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        
        /// <summary>
        /// Снять хук
        /// </summary>
        void UnhookWindowsHookEx(IntPtr hhk);
        
        /// <summary>
        /// Получить текущее состояние клавиши
        /// </summary>
        short GetAsyncKeyState(int vKey);
        
        /// <summary>
        /// Получить дескриптор модуля
        /// </summary>
        IntPtr GetModuleHandle(string? lpModuleName);
    }
}
