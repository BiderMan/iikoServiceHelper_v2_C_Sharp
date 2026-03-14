using System;
using System.Threading.Tasks;
using iikoServiceHelper.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Moq;

namespace iikoServiceHelper.Tests
{
    /// <summary>
    /// Приложение для запуска стресс-тестов 20 раз подряд
    /// </summary>
    public class StressTestRunner
    {
        private readonly ITestOutputHelper _output;

        public StressTestRunner(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task RunModifierKeyManagerStressTest_100Times()
        {
            int testRuns = 100; // Увеличено до 100 повторений
            int failures = 0;
            
            _output.WriteLine($"Запускаем стресс-тест для ModifierKeyManager {testRuns} раз подряд...");
            
            for (int i = 0; i < testRuns; i++)
            {
                _output.WriteLine($"Запуск теста #{i + 1} из {testRuns}");
                
                try
                {
                    // Создаем новый экземпляр менеджера для каждого теста
                    var mockLogger = new Mock<Action<string>>();
                    mockLogger.Setup(x => x(It.IsAny<string>())).Callback<string>(msg => _output.WriteLine(msg));
                    
                    using var manager = new ModifierKeyManager(mockLogger.Object);
                    
                    // Выполняем серию операций с модификаторами
                    await PerformModifierOperations(manager, i + 1);
                    
                    _output.WriteLine($"Тест #{i + 1} завершен успешно");
                }
                catch (Exception ex)
                {
                    failures++;
                    _output.WriteLine($"Тест #{i + 1} завершился с ошибкой: {ex.Message}");
                }
            }
            
            _output.WriteLine($"Стресс-тест завершен. Успешно: {testRuns - failures}, Ошибок: {failures}");
            
            Assert.Equal(0, failures);
        }

        private async Task PerformModifierOperations(ModifierKeyManager manager, int testNumber)
        {
            // Выполняем серию действий с модификаторами
            for (int j = 0; j < 10; j++)
            {
                // В новой реализации UpdateModifierState не влияет на внутреннее состояние
                // Мы полагаемся на физическое состояние клавиш
                // Имитация нажатия и отпускания модификаторов через нативные методы
                NativeMethods.PressAltDown(); // Нажимаем Alt
                await Task.Delay(5);
                NativeMethods.ReleaseModifiers(0x12); // Отпускаем Alt (VK_MENU)
                
                NativeMethods.ReleaseModifiers(0x11); // Убедимся, что Ctrl отпущен
                NativeMethods.SendKey(0x11); // Нажмем и отпустим Ctrl
                await Task.Delay(5);
                
                NativeMethods.ReleaseModifiers(0x10); // Убедимся, что Shift отпущен
                NativeMethods.SendKey(0x10); // Нажмем и отпустим Shift
                await Task.Delay(5);
                
                // Даем время для синхронизации состояний
                await Task.Delay(25);
                
                // Проверяем, что состояние корректное
                var states = manager.GetCurrentStates();
                
                if (states.alt || states.ctrl || states.shift)
                {
                    throw new InvalidOperationException(
                        $"Тест {testNumber}.{j}: Состояние модификаторов некорректно - Alt:{states.alt}, Ctrl:{states.ctrl}, Shift:{states.shift}");
                }
                
                // Проверяем обнаружение одиночных модификаторов
                bool isLoneAlt = manager.IsLoneModifierPress(18, true);
                bool isLoneCtrl = manager.IsLoneModifierPress(17, true);
                bool isLoneShift = manager.IsLoneModifierPress(16, true);
                
                await Task.Delay(10);
            }
            
            // Вызываем методы освобождения для проверки их работы
            manager.ForceReleaseAllModifiers();
            // Даем время для синхронизации после освобождения
            await Task.Delay(50);
            manager.ForceReleaseStuckKeys();
            // Даем время для синхронизации после проверки залипших клавиш
            await Task.Delay(50);
        }
        
         /// <summary>
         /// Дополнительный тест для проверки долгосрочной стабильности
         /// </summary>
         [Fact]
         public async Task LongTermStabilityTest()
         {
             _output.WriteLine("Запускаем долгосрочный тест стабильности...");
             
             var mockLogger = new Mock<Action<string>>();
             mockLogger.Setup(x => x(It.IsAny<string>())).Callback<string>(msg => _output.WriteLine(msg));
             
             using var manager = new ModifierKeyManager(mockLogger.Object);
             
             // Выполняем интенсивные операции в течение 30 секунд
             var startTime = DateTime.Now;
             var duration = TimeSpan.FromSeconds(30);
             
             int operationCount = 0;
             while (DateTime.Now - startTime < duration)
             {
                 // Интенсивное использование модификаторов через нативные методы
                 NativeMethods.PressAltDown(); // Нажимаем Alt
                 await Task.Delay(1);
                 NativeMethods.ReleaseModifiers(0x12); // Отпускаем Alt (VK_MENU)
                 
                 NativeMethods.ReleaseModifiers(0x11); // Убедимся, что Ctrl отпущен
                 NativeMethods.SendKey(0x11); // Нажмем и отпустим Ctrl
                 await Task.Delay(1);
                 
                 NativeMethods.ReleaseModifiers(0x10); // Убедимся, что Shift отпущен
                 NativeMethods.SendKey(0x10); // Нажмем и отпустим Shift
                 await Task.Delay(1);
                 
                 operationCount++;
                 
                 // Периодически проверяем состояние
                 if (operationCount % 100 == 0)
                 {
                     var states = manager.GetCurrentStates();
                     _output.WriteLine($"Операция #{operationCount}: Состояние - Alt:{states.alt}, Ctrl:{states.ctrl}, Shift:{states.shift}");
                 }
             }
             
             _output.WriteLine($"Долгосрочный тест завершен. Выполнено операций: {operationCount}");
             
             // Проверяем финальное состояние
             var finalStates = manager.GetCurrentStates();
             Assert.False(finalStates.alt, "Финальное состояние: Alt залип");
             Assert.False(finalStates.ctrl, "Финальное состояние: Ctrl залип");
             Assert.False(finalStates.shift, "Финальное состояние: Shift залип");
         }
    }
}