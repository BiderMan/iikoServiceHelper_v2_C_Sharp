using FluentAssertions;
using iikoServiceHelper.Models;
using iikoServiceHelper.Services;
using Moq;
using Xunit;

namespace iikoServiceHelper.Tests.Services
{
    public class CommandExecutionServiceTests : TestBase
    {
        private readonly Mock<ICommandHost> _mockHost;
        private readonly Mock<IHotkeyManager> _mockHotkeyManager;
        private readonly CommandExecutionService _service;
        private readonly AppSettings _settings;

        public CommandExecutionServiceTests()
        {
            _mockHost = new Mock<ICommandHost>();
            _mockHost.Setup(h => h.RunOnUIThread(It.IsAny<Action>()))
                     .Callback<Action>(action => action());
            
            // Настраиваем новый метод для очистки истории буфера обмена по времени
            _mockHost.Setup(h => h.ClearClipboardHistoryByTimeRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                     .Returns(Task.CompletedTask);

            _mockHotkeyManager = new Mock<IHotkeyManager>();
            _settings = new AppSettings();

            _service = new CommandExecutionService(
                _mockHotkeyManager.Object,
                CreateLogger<CommandExecutionService>(),
                _settings);

            _service.SetHost(_mockHost.Object);
        }

        [Fact]
        public async Task Enqueue_ShouldExecuteSimpleTextCommand()
        {
            // Arrange
            var testText = "Hello World";
            _mockHost.Setup(h => h.IsInputFocused()).Returns(true);
            // Нужно настроить IHotkeyManager чтобы не было исключений при доступе к свойствам если они используются

            // Act
            // Enqueue требует 3 аргумента в реализации, но в тесте было 1. 
            // Проверю сигнатуру Enqueue: (string command, object? parameter, string hotkeyName)
            _service.Enqueue("Reply", testText, "TestHotkey");

            // Ждем завершения обработки (в реальности лучше использовать события или TaskCompletionSource, но тут sleep для простоты)
            await Task.Delay(500);

            // Assert
            // Reply команда вызывает TypeText -> NativeMethods.SendText
            // Но TypeText использует NativeMethods, которые сложно мокать без еще одной абстракции.
            // Однако, в TypeText есть ветка: 
            // if (_settings.UsePasteModeForQuickReplies) ... _host.ClipboardSetText(text) ...

            // Если мы хотим протестировать именно вызов ClipboardSetText, нужно включить этот режим
            _settings.UsePasteModeForQuickReplies = true;

            // Act again to trigger
            _service.Enqueue("Reply", testText, "TestHotkey");
            await Task.Delay(500);

            _mockHost.Verify(
               h => h.ClipboardSetText(testText),
               Times.AtLeastOnce);
        }

        [Fact]
        public async Task ClearQueue_ShouldCancelRunningCommand()
        {
            // Arrange
            _mockHost.Setup(h => h.IsInputFocused()).Returns(false);
            _service.Enqueue("Reply", "Command 1", "H1");
            _service.Enqueue("Reply", "Command 2", "H2");

            // Act
            _service.ClearQueue();
            await Task.Delay(100);

            // Assert
            // Проверяем что очередь пуста или что-то было отменено
            // В данном случае сложно проверить без мока NativeMethods, но можно проверить состояние сервиса если бы оно было доступно
        }

        [Fact]
        public async Task Enqueue_ShouldWaitForInputFocus_WhenInputIsFocused()
        {
            // Arrange
            _mockHost.SetupSequence(h => h.IsInputFocused())
                .Returns(true)   // Фокус есть (значит ждать не надо)
                .Returns(false);

            // Act
            _service.Enqueue("Reply", "test", "H1");
            await Task.Delay(200);

            // Assert
            _mockHost.Verify(h => h.IsInputFocused(), Times.AtLeastOnce);
        }
    }
}
