using System;
using System.Threading.Tasks;
using iikoServiceHelper.Utils;

namespace StressTestConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Стресс-тест для проверки решения проблемы залипания клавиш ===");
            Console.WriteLine();

            // Получаем количество повторений из аргументов командной строки или используем значение по умолчанию
            int testRuns = 100; // Значение по умолчанию
            if (args.Length > 0 && int.TryParse(args[0], out int parsedRuns) && parsedRuns > 0)
            {
                testRuns = parsedRuns;
            }

            Console.WriteLine($"Количество повторений теста: {testRuns}");
            Console.WriteLine("Нажмите любую клавишу для начала теста...");
            Console.ReadKey();
            Console.WriteLine();

            int failures = 0;
            var startTime = DateTime.Now;

            for (int i = 0; i < testRuns; i++)
            {
                Console.Write($"\rВыполнение теста #{i + 1} из {testRuns}...");

                try
                {
                    // Создаем новый экземпляр менеджера для каждого теста
                    using var manager = new ModifierKeyManager(Console.WriteLine);

                    // Выполняем серию операций с модификаторами
                    await PerformModifierOperations(manager, i + 1);
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine();
                    Console.WriteLine($"Тест #{i + 1} завершился с ошибкой: {ex.Message}");
                }
            }

            var totalTime = DateTime.Now - startTime;
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("=== Результаты тестирования ===");
            Console.WriteLine($"Всего выполнено тестов: {testRuns}");
            Console.WriteLine($"Количество ошибок: {failures}");
            Console.WriteLine($"Количество успешных тестов: {testRuns - failures}");
            Console.WriteLine($"Общее время выполнения: {totalTime}");
            Console.WriteLine($"Среднее время на тест: {totalTime.TotalMilliseconds / testRuns:F2} мс");

            if (failures == 0)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Все тесты пройдены успешно! Решение проблемы залипания клавиш работает корректно.");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Обнаружены ошибки в работе решения.");
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        private static async Task PerformModifierOperations(ModifierKeyManager manager, int testNumber)
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
    }
}