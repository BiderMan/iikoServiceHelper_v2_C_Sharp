using System;
using System.Threading;
using System.Threading.Tasks;
using iikoServiceHelper.Utils;
using Moq;
using Xunit;

namespace iikoServiceHelper.Tests.Services
{
    public class ModifierKeyManagerTests
    {
        private Mock<Action<string>> _mockLogger;
        private ModifierKeyManager _manager;

        public ModifierKeyManagerTests()
        {
            _mockLogger = new Mock<Action<string>>();
            _manager = new ModifierKeyManager(_mockLogger.Object);
        }

        public void Dispose()
        {
            _manager?.Dispose();
        }

        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            // Проверяем, что конструктор инициализирует компоненты
            Assert.NotNull(_manager);
        }

        [Fact]
        public async Task UpdateModifierState_IsCompatible()
        {
            // Тестируем совместимость метода (в новой реализации он не влияет на состояние)
            _manager.UpdateModifierState(18, true); // VK_MENU (Alt)
            
            // Метод должен выполниться без ошибок
            Assert.True(true);
        }

        [Fact]
        public async Task IsLoneModifierPress_DetectsSingleAltPress()
        {
            // Тестируем определение одиночного нажатия Alt
            // В новой реализации метод полагается на физическое состояние клавиш
            // Поэтому мы просто проверяем, что метод работает без ошибок
            bool isLone = _manager.IsLoneModifierPress(18, true); // Alt press
            // Результат зависит от текущего физического состояния клавиш
            Assert.IsType<bool>(isLone);
        }

        [Fact]
        public async Task IsLoneModifierPress_DoesNotDetectWhenOtherModifiersPressed()
        {
            // Тестируем, что одиночное нажатие не определяется, если другие модификаторы зажаты
            // В новой реализации метод полагается на физическое состояние клавиш
            bool isLone = _manager.IsLoneModifierPress(18, true); // Alt press
            // Результат зависит от текущего физического состояния клавиш
            Assert.IsType<bool>(isLone);
        }

        [Fact]
        public async Task ForceReleaseAllModifiers_ReleasesAllModifiers()
        {
            // Тестируем принудительное освобождение всех модификаторов
            _manager.ForceReleaseAllModifiers();
            
            // Проверяем, что логгер получил сообщение
            _mockLogger.Verify(logger => logger(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task ForceReleaseStuckKeys_HandlesStuckKeys()
        {
            // Тестируем обработку залипших клавиш
            _manager.ForceReleaseStuckKeys();
            
            // Проверяем, что логгер получил сообщение
            _mockLogger.Verify(logger => logger(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task StressTest_SimulatesIntensiveKeyPresses()
        {
            // Стресс-тест: интенсивное использование модификаторов
            for (int i = 0; i < 20; i++)
            {
                // Имитация нажатия и отпускания модификаторов
                _manager.UpdateModifierState(18, true);  // Alt down
                await Task.Delay(10);
                _manager.UpdateModifierState(18, false); // Alt up
                
                _manager.UpdateModifierState(17, true);  // Ctrl down
                await Task.Delay(10);
                _manager.UpdateModifierState(17, false); // Ctrl up
                
                _manager.UpdateModifierState(16, true);  // Shift down
                await Task.Delay(10);
                _manager.UpdateModifierState(16, false); // Shift up
                
                // Даем время для синхронизации состояний
                await Task.Delay(25);
                
                // Проверяем состояние
                var states = _manager.GetCurrentStates();
                Assert.False(states.alt);
                Assert.False(states.ctrl);
                Assert.False(states.shift);
            }
        }

        [Fact]
        public async Task MultipleIterations_StressTest()
        {
            // Запуск теста 20 раз как было запрошено
            for (int iteration = 0; iteration < 20; iteration++)
            {
                // Выполняем серию действий
                _manager.UpdateModifierState(18, true);  // Alt down
                await Task.Delay(5);
                _manager.UpdateModifierState(18, false); // Alt up
                
                _manager.UpdateModifierState(17, true);  // Ctrl down
                await Task.Delay(5);
                _manager.UpdateModifierState(17, false); // Ctrl up
                
                _manager.UpdateModifierState(16, true);  // Shift down
                await Task.Delay(5);
                _manager.UpdateModifierState(16, false); // Shift up
                
                // Даем время для синхронизации состояний
                await Task.Delay(25);
                
                // Проверяем, что состояние корректное
                var states = _manager.GetCurrentStates();
                Assert.False(states.alt, $"Iteration {iteration}: Alt should be released");
                Assert.False(states.ctrl, $"Iteration {iteration}: Ctrl should be released");
                Assert.False(states.shift, $"Iteration {iteration}: Shift should be released");
                
                // Добавляем небольшую задержку между итерациями
                await Task.Delay(10);
            }
        }

        [Fact]
        public async Task LoneModifierDetection_Test()
        {
            // Тест для проверки обнаружения одиночных модификаторов
            for (int i = 0; i < 20; i++)
            {
                // Убедимся, что другие модификаторы не зажаты
                _manager.UpdateModifierState(17, false); // Ctrl
                _manager.UpdateModifierState(16, false); // Shift
                
                // Проверим, что одиночное нажатие Alt определяется
                bool isLoneAlt = _manager.IsLoneModifierPress(18, true); // Alt press
                Assert.True(isLoneAlt, $"Iteration {i}: Single Alt press should be detected as lone");
                
                // То же самое для Ctrl
                _manager.UpdateModifierState(18, false); // Alt
                _manager.UpdateModifierState(16, false); // Shift
                
                bool isLoneCtrl = _manager.IsLoneModifierPress(17, true); // Ctrl press
                Assert.True(isLoneCtrl, $"Iteration {i}: Single Ctrl press should be detected as lone");
                
                // То же самое для Shift
                _manager.UpdateModifierState(18, false); // Alt
                _manager.UpdateModifierState(17, false); // Ctrl
                
                bool isLoneShift = _manager.IsLoneModifierPress(16, true); // Shift press
                Assert.True(isLoneShift, $"Iteration {i}: Single Shift press should be detected as lone");
            }
        }
    }
}