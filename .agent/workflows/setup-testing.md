---
description: Настройка unit-тестирования для проекта
---

# Настройка Unit-тестирования

## Описание

Этот workflow описывает процесс создания тестового проекта и написания первых unit-тестов для iikoServiceHelper.

## Цели

- Создать тестовый проект
- Написать тесты для критических компонентов
- Настроить автоматический запуск тестов

## Шаги выполнения

### 1. Создать тестовый проект

```powershell
dotnet new xunit -n iikoServiceHelper.Tests -o Tests/iikoServiceHelper.Tests
```

### 2. Добавить ссылку на основной проект

```powershell
cd Tests/iikoServiceHelper.Tests
dotnet add reference "..\..\iikoServiceHelper.csproj"
```

### 3. Установить необходимые пакеты

```powershell
dotnet add package Moq
dotnet add package FluentAssertions
dotnet add package Microsoft.Extensions.Logging.Abstractions
```

### 4. Добавить проект в solution

```powershell
cd ..\..
dotnet sln add Tests/iikoServiceHelper.Tests/iikoServiceHelper.Tests.csproj
```

### 5. Создать базовый класс для тестов

Создайте файл `Tests/iikoServiceHelper.Tests/TestBase.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace iikoServiceHelper.Tests
{
    public abstract class TestBase
    {
        protected ILogger<T> CreateLogger<T>()
        {
            return NullLogger<T>.Instance;
        }

        protected Mock<ILogger<T>> CreateMockLogger<T>()
        {
            return new Mock<ILogger<T>>();
        }
    }
}
```

### 6. Создать тесты для StringUtils

Создайте файл `Tests/iikoServiceHelper.Tests/Utils/StringUtilsTests.cs`:

```csharp
using FluentAssertions;
using iikoServiceHelper.Utils;
using Xunit;

namespace iikoServiceHelper.Tests.Utils
{
    public class StringUtilsTests
    {
        [Theory]
        [InlineData("ghbdtn", "привет")]
        [InlineData("rfr ltkf?", "как дела?")]
        [InlineData("", "")]
        public void ConvertLayout_ShouldConvertEnglishToRussian(
            string input, 
            string expected)
        {
            // Act
            var result = StringUtils.ConvertLayout(input);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("привет", "ghbdtn")]
        [InlineData("как дела?", "rfr ltkf?")]
        public void ConvertLayout_ShouldConvertRussianToEnglish(
            string input, 
            string expected)
        {
            // Act
            var result = StringUtils.ConvertLayout(input);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void ConvertLayout_ShouldHandleNullInput()
        {
            // Act
            var result = StringUtils.ConvertLayout(null);

            // Assert
            result.Should().BeNullOrEmpty();
        }
    }
}
```

### 7. Создать тесты для CommandExecutionService

Создайте файл `Tests/iikoServiceHelper.Tests/Services/CommandExecutionServiceTests.cs`:

```csharp
using FluentAssertions;
using iikoServiceHelper.Services;
using Moq;
using Xunit;

namespace iikoServiceHelper.Tests.Services
{
    public class CommandExecutionServiceTests : TestBase
    {
        private readonly Mock<ICommandHost> _mockHost;
        private readonly CommandExecutionService _service;

        public CommandExecutionServiceTests()
        {
            _mockHost = new Mock<ICommandHost>();
            _service = new CommandExecutionService(
                _mockHost.Object,
                CreateLogger<CommandExecutionService>());
        }

        [Fact]
        public async Task Enqueue_ShouldExecuteSimpleTextCommand()
        {
            // Arrange
            var testText = "Hello World";
            _mockHost.Setup(h => h.IsInputFocused()).Returns(false);

            // Act
            await _service.Enqueue(testText);
            await Task.Delay(500); // Дать время на обработку очереди

            // Assert
            _mockHost.Verify(
                h => h.ClipboardSetText(testText), 
                Times.Once);
        }

        [Fact]
        public async Task ClearQueue_ShouldCancelRunningCommand()
        {
            // Arrange
            _mockHost.Setup(h => h.IsInputFocused()).Returns(false);
            await _service.Enqueue("Command 1");
            await _service.Enqueue("Command 2");

            // Act
            _service.ClearQueue();
            await Task.Delay(100);

            // Assert - вторая команда не должна выполниться
            _mockHost.Verify(
                h => h.ClipboardSetText(It.IsAny<string>()), 
                Times.AtMostOnce);
        }

        [Fact]
        public async Task Enqueue_ShouldWaitForInputFocus_WhenInputIsFocused()
        {
            // Arrange
            _mockHost.SetupSequence(h => h.IsInputFocused())
                .Returns(true)   // Первая проверка - фокус есть
                .Returns(false); // Вторая проверка - фокус ушел

            // Act
            var task = _service.Enqueue("test");
            await Task.Delay(200);
            await task;

            // Assert
            _mockHost.Verify(h => h.IsInputFocused(), Times.AtLeast(2));
        }
    }
}
```

### 8. Создать тесты для CustomCommandService

Создайте файл `Tests/iikoServiceHelper.Tests/Services/CustomCommandServiceTests.cs`:

```csharp
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
            commands.Should().BeEmpty();
        }

        [Fact]
        public void SaveCommands_ShouldPersistCommands()
        {
            // Arrange
            var commands = new List<CustomCommand>
            {
                new CustomCommand 
                { 
                    Name = "Test", 
                    Template = "Hello {0}", 
                    Hotkey = "Ctrl+1" 
                }
            };

            // Act
            _service.SaveCommands(commands);
            var loaded = _service.LoadCommands();

            // Assert
            loaded.Should().HaveCount(1);
            loaded[0].Name.Should().Be("Test");
            loaded[0].Template.Should().Be("Hello {0}");
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDataPath))
            {
                Directory.Delete(_testDataPath, true);
            }
        }
    }
}
```

### 9. Создать тесты для AppSettings

Создайте файл `Tests/iikoServiceHelper.Tests/Models/AppSettingsTests.cs`:

```csharp
using FluentAssertions;
using iikoServiceHelper.Models;
using Xunit;

namespace iikoServiceHelper.Tests.Models
{
    public class AppSettingsTests
    {
        [Fact]
        public void CrmPassword_ShouldEncryptAndDecrypt()
        {
            // Arrange
            var settings = new AppSettings();
            var originalPassword = "MySecretPassword123!";

            // Act
            settings.CrmPassword = originalPassword;
            var decrypted = settings.CrmPassword;

            // Assert
            decrypted.Should().Be(originalPassword);
            settings.CrmPasswordEncrypted.Should().NotBe(originalPassword);
            settings.CrmPasswordEncrypted.Should().NotBeEmpty();
        }

        [Fact]
        public void CrmPassword_ShouldHandleEmptyPassword()
        {
            // Arrange
            var settings = new AppSettings();

            // Act
            settings.CrmPassword = "";
            var decrypted = settings.CrmPassword;

            // Assert
            decrypted.Should().BeEmpty();
            settings.CrmPasswordEncrypted.Should().BeEmpty();
        }

        [Fact]
        public void DelaySettings_ShouldHaveDefaultValues()
        {
            // Arrange
            var settings = new AppSettings();

            // Assert
            settings.Delays.Should().NotBeNull();
            settings.Delays.KeyPress.Should().Be(50);
            settings.Delays.ActionPause.Should().Be(100);
            settings.Delays.FocusWait.Should().Be(500);
        }
    }
}
```

### 10. Запустить тесты

// turbo

```powershell
dotnet test
```

### 11. Создать файл настройки coverage

Создайте файл `Tests/iikoServiceHelper.Tests/coverlet.runsettings`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat Code Coverage">
        <Configuration>
          <Format>opencover,cobertura</Format>
          <Exclude>[*.Tests]*</Exclude>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

### 12. Запустить тесты с coverage

```powershell
dotnet test --collect:"XPlat Code Coverage" --settings Tests/iikoServiceHelper.Tests/coverlet.runsettings
```

### 13. Установить ReportGenerator для визуализации coverage

```powershell
dotnet tool install -g dotnet-reportgenerator-globaltool
```

### 14. Создать отчет о покрытии

```powershell
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report -reporttypes:Html
```

### 15. Создать скрипт для запуска тестов

Создайте файл `run-tests.ps1`:

```powershell
# Запуск тестов с coverage
Write-Host "Running tests..." -ForegroundColor Green
dotnet test --collect:"XPlat Code Coverage" --settings Tests/iikoServiceHelper.Tests/coverlet.runsettings

# Генерация отчета
Write-Host "Generating coverage report..." -ForegroundColor Green
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report -reporttypes:Html

# Открыть отчет
Write-Host "Opening coverage report..." -ForegroundColor Green
Start-Process "coverage-report/index.html"
```

### 16. Добавить .gitignore для тестов

Добавьте в `.gitignore`:

```
# Test coverage
**/TestResults/
**/coverage-report/
**/*.coverage
```

## Критерии завершения

- [ ] Тестовый проект создан и добавлен в solution
- [ ] Установлены пакеты xUnit, Moq, FluentAssertions
- [ ] Созданы тесты для StringUtils
- [ ] Созданы тесты для CommandExecutionService
- [ ] Созданы тесты для CustomCommandService
- [ ] Созданы тесты для AppSettings
- [ ] Все тесты проходят успешно
- [ ] Настроен code coverage
- [ ] Создан скрипт для запуска тестов

## Минимальное покрытие

Целевое покрытие кода тестами:

- **Utilities**: 80%+
- **Services**: 60%+
- **Models**: 70%+
- **ViewModels** (после рефакторинга): 50%+

## Полезные ссылки

- [xUnit Documentation](https://xunit.net/)
- [Moq Documentation](https://github.com/moq/moq4)
- [FluentAssertions](https://fluentassertions.com/)
