using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace iikoServiceHelper.Utils
{
    /// <summary>
    /// Управление состоянием модификаторов (Alt, Ctrl, Shift) для предотвращения залипания клавиш
    /// </summary>
    public class ModifierKeyManager : IDisposable
    {
        private readonly Action<string> _logAction;
        
        // Вместо внутреннего состояния будем полагаться на физическое состояние
        // Это поможет избежать рассинхронизации
        
        // Таймер для периодической проверки состояния
        private Timer? _stateCheckTimer;
        
        // Константы для модификаторов
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_SHIFT = 0x10;
        private const ushort VK_MENU = 0x12; // Alt
        
        public ModifierKeyManager(Action<string> logAction)
        {
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
            
            // Запускаем таймер проверки состояния
            _stateCheckTimer = new Timer(CheckAndSyncState, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(10)); // Уменьшено до 10мс для более частой проверки
        }
        
        /// <summary>
        /// Проверяет и синхронизирует состояние модификаторов
        /// </summary>
        private void CheckAndSyncState(object? state)
        {
            try
            {
                // Получаем текущее физическое состояние
                bool currentPhysicalAlt = (NativeMethods.GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                bool currentPhysicalCtrl = (NativeMethods.GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool currentPhysicalShift = (NativeMethods.GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                
                // Проверяем, есть ли залипшие клавиши
                if (currentPhysicalAlt || currentPhysicalCtrl || currentPhysicalShift)
                {
                    // Если какие-то модификаторы физически зажаты, но не должны быть,
                    // это может указывать на залипание
                    _logAction($"⚠️ Physical state check: Alt={currentPhysicalAlt}, Ctrl={currentPhysicalCtrl}, Shift={currentPhysicalShift}");
                    
                    // Запускаем проверку залипших клавиш
                    Task.Run(() => ForceReleaseStuckKeys());
                }
            }
            catch (Exception ex)
            {
                _logAction($"Error in CheckAndSyncState: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Обновляет внутреннее состояние модификатора
        /// </summary>
        public void UpdateModifierState(int vkCode, bool isKeyDown)
        {
            // В новой реализации мы не храним внутреннее состояние, 
            // а полагаемся на физическое состояние при каждом обращении
            // Этот метод оставлен для совместимости с интерфейсом
        }
        
        /// <summary>
        /// Проверяет, является ли нажатие одиночным (только модификатор без других клавиш)
        /// </summary>
        public bool IsLoneModifierPress(int vkCode, bool isKeyDown)
        {
            if (!isKeyDown) return false; // Только для нажатий, не для отпусканий
            
            // Получаем текущее физическое состояние модификаторов
            bool physicalCtrl = (NativeMethods.GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool physicalShift = (NativeMethods.GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            
            bool isAlt = (vkCode == 164 || vkCode == 165 || vkCode == 18);
            bool isCtrl = (vkCode == 162 || vkCode == 163 || vkCode == 17);
            bool isShift = (vkCode == 160 || vkCode == 161 || vkCode == 16);
            
            if (isAlt)
            {
                // Проверяем, что другие модификаторы не зажаты (используем физическое состояние)
                return !physicalCtrl && !physicalShift;
            }
            else if (isCtrl)
            {
                // Проверяем, что другие модификаторы не зажаты (используем физическое состояние)
                return !((NativeMethods.GetAsyncKeyState(VK_MENU) & 0x8000) != 0) && !physicalShift;
            }
            else if (isShift)
            {
                // Проверяем, что другие модификаторы не зажаты (используем физическое состояние)
                return !((NativeMethods.GetAsyncKeyState(VK_MENU) & 0x8000) != 0) && !physicalCtrl;
            }
            
            return false;
        }
        
        /// <summary>
        /// Принудительно освобождает залипшие клавиши
        /// </summary>
        public void ForceReleaseStuckKeys()
        {
            try
            {
                _logAction("🔧 ForceReleaseStuckKeys: Checking for stuck modifier keys...");
                
                // Получаем текущее физическое состояние
                bool currentPhysicalAlt = (NativeMethods.GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                bool currentPhysicalCtrl = (NativeMethods.GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool currentPhysicalShift = (NativeMethods.GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                
                var keysToRelease = new System.Collections.Generic.List<ushort>();
                
                // Проверяем, какие клавиши физически зажаты
                if (currentPhysicalAlt)
                {
                    _logAction("🔧 Detected physically stuck Alt key - adding to release list");
                    keysToRelease.Add(0xA2); // VK_LMENU
                    keysToRelease.Add(0xA3); // VK_RMENU
                    keysToRelease.Add(0x12); // VK_MENU
                }
                
                if (currentPhysicalCtrl)
                {
                    _logAction("🔧 Detected physically stuck Ctrl key - adding to release list");
                    keysToRelease.Add(0xA2); // VK_LCONTROL
                    keysToRelease.Add(0xA3); // VK_RCONTROL
                    keysToRelease.Add(0x11); // VK_CONTROL
                }
                
                if (currentPhysicalShift)
                {
                    _logAction("🔧 Detected physically stuck Shift key - adding to release list");
                    keysToRelease.Add(0xA0); // VK_LSHIFT
                    keysToRelease.Add(0xA1); // VK_RSHIFT
                    keysToRelease.Add(0x10); // VK_SHIFT
                }
                
                if (keysToRelease.Count > 0)
                {
                    _logAction($"🔧 Releasing {keysToRelease.Count} stuck modifier keys via native methods...");
                    
                    // Освобождаем все потенциально залипшие клавиши
                    foreach (var key in keysToRelease)
                    {
                        try
                        {
                            NativeMethods.ReleaseModifiers(key);
                        }
                        catch (Exception ex)
                        {
                            _logAction($"❌ Error releasing key {key:X}: {ex.Message}");
                        }
                    }
                    
                    _logAction("✅ ForceReleaseStuckKeys completed.");
                }
                else
                {
                    _logAction("✅ No stuck modifier keys detected.");
                }
            }
            catch (Exception ex)
            {
                _logAction($"❌ ForceReleaseStuckKeys error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Принудительно освобождает все модификаторы
        /// </summary>
        public void ForceReleaseAllModifiers()
        {
            try
            {
                _logAction("🔧 ForceReleaseAllModifiers: Releasing all modifier keys...");
                
                // Освобождаем все модификаторы
                NativeMethods.ReleaseModifiers(
                    0xA2, // VK_LMENU
                    0xA3, // VK_RMENU
                    0x12, // VK_MENU
                    0xA0, // VK_LCONTROL
                    0xA3, // VK_RCONTROL
                    0x11, // VK_CONTROL
                    0xA0, // VK_LSHIFT
                    0xA1, // VK_RSHIFT
                    0x10  // VK_SHIFT
                );
                
                _logAction("✅ ForceReleaseAllModifiers completed.");
            }
            catch (Exception ex)
            {
                _logAction($"❌ ForceReleaseAllModifiers error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Получает текущее состояние модификаторов (возвращает физическое состояние)
        /// </summary>
        public (bool alt, bool ctrl, bool shift) GetCurrentStates()
        {
            bool alt = (NativeMethods.GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool ctrl = (NativeMethods.GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool shift = (NativeMethods.GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            
            return (alt, ctrl, shift);
        }
        
        public void Dispose()
        {
            _stateCheckTimer?.Dispose();
        }
    }
}
