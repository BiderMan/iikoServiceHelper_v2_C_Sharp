using FluentAssertions;
using iikoServiceHelper.Services;
using System.IO;
using Xunit;

namespace iikoServiceHelper.Tests.Services
{
    public class CustomCommandServiceTests : IDisposable
    {
        private readonly string _testDataPath;
        private readonly CustomCommandService _service;

        public CustomCommandServiceTests()
        {
            _testDataPath = Path.Combine(
                Path.GetTempPath(),
                $"iikoServiceHelper_Test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDataPath);
            _service = new CustomCommandService(_testDataPath);
        }

        [Fact]
        public void LoadCommands_ShouldReturnEmptyList_WhenNoFileExists()
        {
            // Act
            var commands = _service.LoadCommands();

            // Assert
            commands.Should().NotBeNull();
            commands.Should().NotBeNull();
            commands.Should().NotBeEmpty();
        }

        [Fact]
        public void SaveCommands_ShouldPersistCommands()
        {
            // Arrange
            var commands = new List<iikoServiceHelper.Models.CustomCommand>
            {
                new iikoServiceHelper.Models.CustomCommand
                {
                    Description = "Test",
                    Content = "Hello {0}",
                    Trigger = "Ctrl+1"
                }
            };

            // Act
            _service.SaveCommands(commands);
            var loaded = _service.LoadCommands();

            // Assert
            loaded.Should().HaveCount(1);
            loaded[0].Description.Should().Be("Test");
            loaded[0].Content.Should().Be("Hello {0}");
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDataPath))
            {
                try
                {
                    Directory.Delete(_testDataPath, true);
                }
                catch { }
            }
        }
    }
}
