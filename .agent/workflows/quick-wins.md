---
description: Быстрые улучшения для немедленного эффекта
---

# Quick Wins - Быстрые улучшения

## Описание

Этот workflow содержит набор небольших улучшений, каждое из которых можно реализовать за 30-60 минут, но которые дадут заметный эффект.

## 1. Добавить XML документацию

### Время: 30 минут

### Приоритет: Средний

Добавьте XML комментарии к публичным классам и методам.

**Пример для CommandExecutionService.cs:**

```csharp
/// <summary>
/// Сервис для последовательного выполнения команд и макросов.
/// </summary>
/// <remarks>
/// Команды добавляются в очередь и выполняются последовательно
/// для предотвращения конфликтов при работе с буфером обмена.
/// Поддерживает отмену выполнения через CancellationToken.
/// </remarks>
public class CommandExecutionService : ICommandExecutionService
{
    /// <summary>
    /// Добавляет команду в очередь выполнения.
    /// </summary>
    /// <param name="command">Текстовая команда или макрос для выполнения</param>
    /// <returns>Task, представляющий асинхронную операцию</returns>
    /// <exception cref="ArgumentNullException">
    /// Выбрасывается, если command равен null
    /// </exception>
    public async Task Enqueue(string command)
    {
        // ...
    }
}
```

**Включить генерацию XML документации:**

Добавьте в `iikoServiceHelper.csproj`:

```xml
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);1591</NoWarn> <!-- Отключить warning для недокументированных членов -->
</PropertyGroup>
```

---

## 2. Создать константы вместо magic numbers

### Время: 30 минут

### Приоритет: Высокий

Создайте файл `Constants/AppConstants.cs`:

```csharp
namespace iikoServiceHelper.Constants
{
    /// <summary>
    /// Константы приложения
    /// </summary>
    public static class AppConstants
    {
        /// <summary>
        /// Порт для Chrome DevTools Protocol
        /// </summary>
        public const int ChromeDebugPort = 9222;

        /// <summary>
        /// Минимальный размер шрифта заметок
        /// </summary>
        public const int MinFontSize = 8;

        /// <summary>
        /// Максимальный размер шрифта заметок
        /// </summary>
        public const int MaxFontSize = 72;

        /// <summary>
        /// Размер шрифта по умолчанию
        /// </summary>
        public const double DefaultFontSize = 14.0;

        /// <summary>
        /// Длительность уведомлений по умолчанию (секунды)
        /// </summary>
        public const int DefaultNotificationDuration = 3;

        /// <summary>
        /// Интервал автоматического входа в CRM (минуты)
        /// </summary>
        public const int CrmAutoLoginInterval = 30;

        /// <summary>
        /// Имя файла настроек
        /// </summary>
        public const string SettingsFileName = "settings.json";

        /// <summary>
        /// Имя файла заметок
        /// </summary>
        public const string NotesFileName = "notes.txt";

        /// <summary>
        /// Имя файла пользовательских команд
        /// </summary>
        public const string CustomCommandsFileName = "custom_commands.json";

        /// <summary>
        /// Имя файла настроек темы
        /// </summary>
        public const string ThemeSettingsFileName = "theme_colors.json";

        /// <summary>
        /// Имя файла детального лога
        /// </summary>
        public const string DetailedLogFileName = "detailed_log.txt";

        /// <summary>
        /// Имя файла crash log
        /// </summary>
        public const string CrashLogFileName = "crash_log.txt";
    }

    /// <summary>
    /// Константы задержек
    /// </summary>
    public static class DelayConstants
    {
        /// <summary>
        /// Задержка между нажатиями клавиш (мс)
        /// </summary>
        public const int DefaultKeyPress = 50;

        /// <summary>
        /// Пауза между действиями (мс)
        /// </summary>
        public const int DefaultActionPause = 100;

        /// <summary>
        /// Ожидание фокуса ввода (мс)
        /// </summary>
        public const int DefaultFocusWait = 500;
    }

    /// <summary>
    /// URL константы
    /// </summary>
    public static class UrlConstants
    {
        public const string FtpServer = "ftp://files.resto.lan";
        public const string UpdateCheckUrl = "https://api.github.com/repos/YOUR_REPO/releases/latest";
        public const string PosInstallerBaseUrl = "https://pos.iiko.ru/installers/";
    }
}
```

**Замените magic numbers на константы:**

```csharp
// Было:
_timer.Interval = TimeSpan.FromMinutes(30);

// Стало:
_timer.Interval = TimeSpan.FromMinutes(AppConstants.CrmAutoLoginInterval);
```

---

## 3. Использовать ConfigureAwait(false)

### Время: 20 минут

### Приоритет: Средний

В методах, которые не работают с UI, добавьте `ConfigureAwait(false)` для оптимизации:

```csharp
// В CommandExecutionService.cs и других сервисах
public async Task Enqueue(string command)
{
    await _queueSemaphore.WaitAsync().ConfigureAwait(false);
    try
    {
        _commandQueue.Enqueue(command);
    }
    finally
    {
        _queueSemaphore.Release();
    }
}

private async Task ProcessQueue()
{
    while (!_cancellationToken.IsCancellationRequested)
    {
        await Task.Delay(100).ConfigureAwait(false);
        // ...
    }
}
```

**Правило:** Используйте `ConfigureAwait(false)` везде, кроме ViewModels и code-behind файлов.

---

## 4. Добавить .editorconfig

### Время: 15 минут

### Приоритет: Средний

Создайте файл `.editorconfig` в корне проекта:

```ini
root = true

[*]
charset = utf-8
end_of_line = crlf
trim_trailing_whitespace = true
insert_final_newline = true

[*.cs]
indent_style = space
indent_size = 4

# Naming conventions
dotnet_naming_rule.private_fields_should_be_camelcase.severity = warning
dotnet_naming_rule.private_fields_should_be_camelcase.symbols = private_fields
dotnet_naming_rule.private_fields_should_be_camelcase.style = camelcase_underscore_style

dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private

dotnet_naming_style.camelcase_underscore_style.capitalization = camel_case
dotnet_naming_style.camelcase_underscore_style.required_prefix = _

# Code style
csharp_prefer_braces = true:warning
csharp_prefer_static_local_function = true:suggestion
dotnet_code_quality_unused_parameters = all:warning

# Null checking
csharp_style_conditional_delegate_call = true:suggestion
dotnet_style_coalesce_expression = true:suggestion
dotnet_style_null_propagation = true:suggestion

[*.xaml]
indent_size = 2

[*.{json,xml}]
indent_size = 2
```

---

## 5. Улучшить логирование

### Время: 45 минут

### Приоритет: Высокий

Замените `Debug.WriteLine` на структурированное логирование.

#### a. Настройте Serilog

```powershell
dotnet add package Serilog.Extensions.Logging
dotnet add package Serilog.Sinks.File
dotnet add package Serilog.Sinks.Console
```

#### b. Обновите App.xaml.cs

```csharp
using Serilog;

private void ConfigureServices(IServiceCollection services)
{
    // Настройка Serilog
    var appDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
        "iikoServiceHelper_v2");
    
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(appDataPath, "logs", "app-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    services.AddLogging(configure => 
    {
        configure.ClearProviders();
        configure.AddSerilog(dispose: true);
    });
    
    // ... остальная конфигурация
}
```

#### c. Используйте в сервисах

```csharp
// В CommandExecutionService.cs
_logger.LogInformation(
    "Command enqueued: {Command}, Queue size: {QueueSize}",
    command,
    _commandQueue.Count);

_logger.LogWarning(
    "Failed to execute command: {Command}. Reason: {Reason}",
    command,
    exception.Message);

_logger.LogError(
    exception,
    "Critical error in command execution");
```

---

## 6. Добавить debouncing для автосохранения

### Время: 30 минут

### Приоритет: Высокий

Создайте утилиту для debouncing:

```csharp
// Utils/DebouncedAction.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace iikoServiceHelper.Utils
{
    /// <summary>
    /// Утилита для "отложенного" выполнения действия
    /// </summary>
    public class DebouncedAction
    {
        private readonly TimeSpan _delay;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public DebouncedAction(TimeSpan delay)
        {
            _delay = delay;
        }

        /// <summary>
        /// Запланировать выполнение действия.
        /// Если вызвано повторно до истечения задержки, предыдущее выполнение отменяется.
        /// </summary>
        public async Task InvokeAsync(Func<Task> action)
        {
            await _semaphore.WaitAsync();
            try
            {
                // Отменить предыдущее действие
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                var token = _cts.Token;

                // Запустить отложенное выполнение
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(_delay, token);
                        if (!token.IsCancellationRequested)
                        {
                            await action();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Ожидаемое исключение при отмене
                    }
                }, token);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
```

**Использование в MainWindow или ViewModel:**

```csharp
private readonly DebouncedAction _saveSettingsDebouncer = 
    new(TimeSpan.FromSeconds(1));

private async void OnSettingsChanged(object sender, EventArgs e)
{
    await _saveSettingsDebouncer.InvokeAsync(SaveSettingsAsync);
}

private async Task SaveSettingsAsync()
{
    // Сохранение настроек
    _logger.LogDebug("Auto-saving settings");
    var json = JsonSerializer.Serialize(_settings);
    await File.WriteAllTextAsync(_settingsPath, json);
}
```

---

## 7. Добавить валидацию настроек

### Время: 30 минут

### Приоритет: Высокий

Обновите `AppSettings.cs` с валидацией:

```csharp
using AppConstants = iikoServiceHelper.Constants.AppConstants;

public class AppSettings
{
    private double _notesFontSize = AppConstants.DefaultFontSize;
    private int _notificationDurationSeconds = AppConstants.DefaultNotificationDuration;

    /// <summary>
    /// Размер шрифта заметок (8-72)
    /// </summary>
    public double NotesFontSize
    {
        get => _notesFontSize;
        set => _notesFontSize = Math.Clamp(
            value, 
            AppConstants.MinFontSize, 
            AppConstants.MaxFontSize);
    }

    /// <summary>
    /// Длительность уведомлений в секундах (1-10)
    /// </summary>
    public int NotificationDurationSeconds
    {
        get => _notificationDurationSeconds;
        set => _notificationDurationSeconds = Math.Clamp(value, 1, 10);
    }

    /// <summary>
    /// Проверить корректность настроек
    /// </summary>
    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        if (Delays.KeyPress < 0 || Delays.KeyPress > 1000)
            errors.Add("KeyPress delay должен быть в диапазоне 0-1000 мс");

        if (Delays.ActionPause < 0 || Delays.ActionPause > 5000)
            errors.Add("ActionPause должен быть в диапазоне 0-5000 мс");

        if (Delays.FocusWait < 0 || Delays.FocusWait > 10000)
            errors.Add("FocusWait должен быть в диапазоне 0-10000 мс");

        return errors.Count == 0;
    }
}
```

---

## 8. Добавить отписку от событий

### Время: 30 минут

### Приоритет: Критический (предотвращение memory leaks)

В `MainWindow.xaml.cs` добавьте:

```csharp
protected override void OnClosed(EventArgs e)
{
    // Отписаться от событий
    if (_commandExecutionService != null)
    {
        _commandExecutionService.CommandStarted -= OnCommandStarted;
        _commandExecutionService.CommandCompleted -= OnCommandCompleted;
    }

    if (_crmAutoLoginService != null)
    {
        _crmAutoLoginService.StatusUpdated -= OnCrmStatusUpdated;
        _crmAutoLoginService.LastRunUpdated -= OnCrmLastRunUpdated;
        _crmAutoLoginService.Dispose();
    }

    // Dispose сервисов
    _hotkeyManager?.Dispose();
    _trayIconService?.Dispose();
    _altBlockerService?.Dispose();

    // Сохранить настройки перед закрытием
    SaveSettings();

    base.OnClosed(e);
}
```

---

## 9. Создать скрипты для сборки

### Время: 20 минут

### Приоритет: Средний

Создайте файл `build.ps1`:

```powershell
param(
    [ValidateSet('Portable', 'Compact', 'Both')]
    [string]$BuildType = 'Both',
    
    [switch]$Publish,
    [switch]$Clean
)

if ($Clean) {
    Write-Host "Cleaning..." -ForegroundColor Yellow
    dotnet clean
    Remove-Item -Path "bin", "obj" -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore

Write-Host "Building..." -ForegroundColor Cyan
dotnet build -c Release

if ($Publish) {
    if ($BuildType -eq 'Portable' -or $BuildType -eq 'Both') {
        Write-Host "Publishing Portable version..." -ForegroundColor Green
        dotnet publish iikoServiceHelper.csproj -c Release `
            -p:EnableCompressionInSingleFile=true
    }

    if ($BuildType -eq 'Compact' -or $BuildType -eq 'Both') {
        Write-Host "Publishing Compact version..." -ForegroundColor Green
        dotnet publish iikoServiceHelper.csproj -c Release `
            -p:SelfContained=false
    }

    Write-Host "Build completed!" -ForegroundColor Green
    Write-Host "Output: bin\Release\net8.0-windows10.0.19041.0\publish\" -ForegroundColor Cyan
}
```

**Использование:**

// turbo

```powershell
# Только сборка
.\build.ps1

# Публикация обеих версий
.\build.ps1 -Publish -BuildType Both

# Чистая сборка + публикация Portable
.\build.ps1 -Clean -Publish -BuildType Portable
```

---

## 10. Добавить CHANGELOG.md

### Время: 15 минут

### Приоритет: Низкий

Создайте файл `CHANGELOG.md`:

```markdown
# Changelog

Все значимые изменения в проекте будут документированы в этом файле.

Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.0.0/),
и этот проект придерживается [Semantic Versioning](https://semver.org/lang/ru/).

## [Unreleased]

### Added
- XML документация для публичных классов
- Константы вместо magic numbers
- Structured logging с Serilog
- Debouncing для автосохранения
- Валидация настроек

### Changed
- Использование ConfigureAwait(false) в асинхронных методах
- Улучшена обработка ошибок

### Fixed
- Memory leaks от неотписанных событий

## [2.5.0] - 2024-XX-XX

### Added
- Начальная версия с WPF
- Горячие клавиши
- CRM авто-вход
- Макросы
- Темизация

[Unreleased]: https://github.com/YOUR_REPO/compare/v2.5.0...HEAD
[2.5.0]: https://github.com/YOUR_REPO/releases/tag/v2.5.0
```

---

## Чеклист Quick Wins

- [ ] XML документация добавлена
- [ ] Константы созданы и используются
- [ ] ConfigureAwait(false) применён
- [ ] .editorconfig создан
- [ ] Serilog настроен
- [ ] Debouncing реализован
- [ ] Валидация настроек добавлена
- [ ] Отписка от событий реализована
- [ ] Скрипты сборки созданы
- [ ] CHANGELOG.md создан

## Порядок выполнения

Рекомендуется выполнять в следующем порядке:

1. Константы (высокая важность, быстро)
2. Отписка от событий (критично для стабильности)
3. Валидация настроек (безопасность)
4. Serilog (улучшает отладку)
5. Debouncing (производительность)
6. ConfigureAwait (оптимизация)
7. XML документация (поддерживаемость)
8. .editorconfig (code quality)
9. Скрипты сборки (автоматизация)
10. CHANGELOG (документация)

Общее время на все Quick Wins: **4-5 часов**.
