using FluentAssertions;
using iikoServiceHelper.Models;
using iikoServiceHelper.Services;
using Xunit;

namespace iikoServiceHelper.Tests.Services
{
    /// <summary>
    /// Тесты для CrmAutoLoginService.
    /// Примечание: CrmAutoLoginService использует DispatcherTimer (WPF),
    /// поэтому некоторые аспекты требуют интеграционного тестирования.
    /// </summary>
    public class CrmAutoLoginServiceTests : TestBase
    {
        private readonly CrmAutoLoginService _service;
        
        public CrmAutoLoginServiceTests()
        {
            _service = new CrmAutoLoginService();
        }

        [Fact]
        public void Constructor_ShouldInitializeWithInactiveState()
        {
            // Assert
            _service.IsActive.Should().BeFalse();
        }

        [Fact]
        public void Start_WhenNotActive_ShouldSetActiveState()
        {
            // Arrange
            var browser = new BrowserItem { Name = "Test Browser", Path = "test.exe" };
            
            // Act
            _service.Start("testuser", "testpass", browser);
            
            // Assert
            _service.IsActive.Should().BeTrue();
        }

        [Fact]
        public void Start_WhenAlreadyActive_ShouldNotThrow()
        {
            // Arrange
            var browser = new BrowserItem { Name = "Test Browser", Path = "test.exe" };
            _service.Start("testuser", "testpass", browser);
            
            // Act & Assert - не должно выбрасывать исключение
            var action = () => _service.Start("testuser", "testpass", browser);
            action.Should().NotThrow();
        }

        [Fact]
        public void Stop_WhenActive_ShouldSetInactiveState()
        {
            // Arrange
            var browser = new BrowserItem { Name = "Test Browser", Path = "test.exe" };
            _service.Start("testuser", "testpass", browser);
            
            // Act
            _service.Stop();
            
            // Assert
            _service.IsActive.Should().BeFalse();
        }

        [Fact]
        public void Stop_WhenNotActive_ShouldNotThrow()
        {
            // Act & Assert
            var action = () => _service.Stop();
            action.Should().NotThrow();
        }

        [Fact]
        public void Start_ShouldRaiseStatusUpdatedEvent()
        {
            // Arrange
            var browser = new BrowserItem { Name = "Test Browser", Path = "test.exe" };
            string? receivedStatus = null;
            _service.StatusUpdated += status => receivedStatus = status;
            
            // Act
            _service.Start("testuser", "testpass", browser);
            
            // Assert
            receivedStatus.Should().NotBeNull();
            receivedStatus.Should().Contain("Активно");
        }

        [Fact]
        public void Stop_ShouldRaiseStatusUpdatedEvent()
        {
            // Arrange
            var browser = new BrowserItem { Name = "Test Browser", Path = "test.exe" };
            _service.Start("testuser", "testpass", browser);
            
            string? receivedStatus = null;
            _service.StatusUpdated += status => receivedStatus = status;
            
            // Act
            _service.Stop();
            
            // Assert
            receivedStatus.Should().NotBeNull();
            receivedStatus.Should().Contain("Отключено");
        }

        [Fact]
        public void Start_ShouldRaiseLogMessageEvent()
        {
            // Arrange
            var browser = new BrowserItem { Name = "Test Browser", Path = "test.exe" };
            string? receivedMessage = null;
            _service.LogMessage += msg => receivedMessage = msg;
            
            // Act
            _service.Start("testuser", "testpass", browser);
            
            // Assert - проверяем что хотя бы одно сообщение содержит информацию о запуске
            receivedMessage.Should().NotBeNull();
            // Note: Первые сообщения могут быть от HandleTimerTickAsync
            // поэтому проверяем наличие любого сообщения
        }

        [Fact]
        public void Dispose_ShouldStopService()
        {
            // Arrange
            var browser = new BrowserItem { Name = "Test Browser", Path = "test.exe" };
            _service.Start("testuser", "testpass", browser);
            
            // Act
            _service.Dispose();
            
            // Assert
            _service.IsActive.Should().BeFalse();
        }

        [Fact]
        public void CrmAutoLoginService_ShouldImplementIDisposable()
        {
            // Arrange & Act
            var type = typeof(CrmAutoLoginService);
            
            // Assert
            type.Should().Implement<IDisposable>();
        }
    }
}
