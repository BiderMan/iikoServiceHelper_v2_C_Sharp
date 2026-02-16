---
description: Оптимизация производительности приложения
---

# Оптимизация производительности

## Описание

Workflow для улучшения производительности, оптимизации потребления памяти и уменьшения времени отклика приложения.

## Приоритет: Средний

## Шаги выполнения

### 1. Профилирование приложения

#### Установить инструменты

```powershell
# dotMemory и dotTrace (платные, но есть trial)
# Или использовать встроенные инструменты Visual Studio
```

#### Найти узкие места

1. Запустите приложение в режиме Release
2. Выполните типичные операции
3. Снимите memory dump
4. Проанализируйте:
   - Memory allocations
   - CPU usage
   - GC collections
   - Thread usage

### 2. Оптимизация загрузки настроек

#### Проблема

Синхронная загрузка файлов блокирует UI поток.

#### Решение

Создайте асинхронный сервис загрузки:

```csharp
// Services/AsyncSettingsLoader.cs
public class AsyncSettingsLoader
{
    private readonly string _settingsPath;
    private readonly ILogger<AsyncSettingsLoader> _logger;

    public AsyncSettingsLoader(
        string settingsPath,
        ILogger<AsyncSettingsLoader> logger)
    {
        _settingsPath = settingsPath;
        _logger = logger;
    }

    /// <summary>
    /// Асинхронная загрузка настроек с кэшированием
    /// </summary>
    public async Task<AppSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            using var fileStream = new FileStream(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            return await JsonSerializer.DeserializeAsync<AppSettings>(
                fileStream,
                cancellationToken: cancellationToken) 
                ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
            return new AppSettings();
        }
    }

    /// <summary>
    /// Асинхронное сохранение с batching
    /// </summary>
    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tempPath = _settingsPath + ".tmp";

            // Записываем во временный файл
            using (var fileStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    fileStream,
                    settings,
                    new JsonSerializerOptions { WriteIndented = true },
                    cancellationToken);

                await fileStream.FlushAsync(cancellationToken);
            }

            // Атомарная замена
            File.Replace(tempPath, _settingsPath, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            throw;
        }
    }
}
```

### 3. Оптимизация работы с буфером обмена

#### Проблема

Частые обращения к буферу обмена создают задержки.

#### Решение

Создайте кэширующий wrapper:

```csharp
// Services/ClipboardCache.cs
public class ClipboardCache
{
    private string? _cachedText;
    private DateTime _lastUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMilliseconds(100);
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Получить текст из буфера с кэшированием
    /// </summary>
    public async Task<string?> GetTextAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (DateTime.Now - _lastUpdate < _cacheLifetime)
                return _cachedText;

            // Обновить кэш
            _cachedText = await Task.Run(() => 
            {
                if (Clipboard.ContainsText())
                    return Clipboard.GetText();
                return null;
            });

            _lastUpdate = DateTime.Now;
            return _cachedText;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Установить текст с инвалидацией кэша
    /// </summary>
    public async Task SetTextAsync(string text)
    {
        await _semaphore.WaitAsync();
        try
        {
            await Task.Run(() => Clipboard.SetText(text));
            _cachedText = text;
            _lastUpdate = DateTime.Now;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Инвалидировать кэш
    /// </summary>
    public void Invalidate()
    {
        _cachedText = null;
        _lastUpdate = DateTime.MinValue;
    }
}
```

### 4. Оптимизация CommandExecutionService

#### Использовать ArrayPool для больших буферов

```csharp
using System.Buffers;

public class CommandExecutionService
{
    private readonly ArrayPool<char> _charPool = ArrayPool<char>.Shared;

    private async Task TypeTextOptimized(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // Аренда буфера из пула
        var buffer = _charPool.Rent(text.Length);
        try
        {
            text.CopyTo(0, buffer, 0, text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                SendKeys.SendWait(buffer[i].ToString());
                await Task.Delay(_settings.Delays.KeyPress)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            // Вернуть буфер в пул
            _charPool.Return(buffer, clearArray: true);
        }
    }
}
```

### 5. Оптимизация Collections

#### Использовать правильные коллекции

```csharp
// Вместо List для поиска
private readonly HashSet<string> _processedCommands = new();

// Вместо Dictionary для синхронизации
private readonly ConcurrentDictionary<string, string> _cache = new();

// Для очередей
private readonly Channel<Command> _commandChannel = 
    Channel.CreateUnbounded<Command>();

// Для небольших коллекций
private readonly ImmutableArray<string> _defaultCommands = 
    ImmutableArray.Create("cmd1", "cmd2", "cmd3");
```

#### Пример с Channel для очереди команд

```csharp
public class OptimizedCommandQueue
{
    private readonly Channel<Command> _channel;
    private readonly ChannelReader<Command> _reader;
    private readonly ChannelWriter<Command> _writer;

    public OptimizedCommandQueue()
    {
        _channel = Channel.CreateUnbounded<Command>(
            new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true
            });

        _reader = _channel.Reader;
        _writer = _channel.Writer;
    }

    public async ValueTask EnqueueAsync(Command command)
    {
        await _writer.WriteAsync(command);
    }

    public async IAsyncEnumerable<Command> DequeueAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var command in _reader.ReadAllAsync(cancellationToken))
        {
            yield return command;
        }
    }
}
```

### 6. Lazy Loading для тяжелых ресурсов

```csharp
// Services/ResourceManager.cs
public class ResourceManager
{
    private readonly Lazy<BrowserFinder> _browserFinder;
    private readonly Lazy<DefaultCommandsProvider> _commandsProvider;

    public ResourceManager()
    {
        _browserFinder = new Lazy<BrowserFinder>(
            () => new BrowserFinder(),
            LazyThreadSafetyMode.ExecutionAndPublication);

        _commandsProvider = new Lazy<DefaultCommandsProvider>(
            () => new DefaultCommandsProvider(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public BrowserFinder BrowserFinder => _browserFinder.Value;
    public DefaultCommandsProvider CommandsProvider => _commandsProvider.Value;
}
```

### 7. Оптимизация UI Virtualization

Для списков команд используйте виртуализацию:

```xml
<!-- MainWindow.xaml -->
<ListBox ItemsSource="{Binding Commands}"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"
         VirtualizingPanel.CacheLength="20,20"
         VirtualizingPanel.CacheLengthUnit="Item">
    <ListBox.ItemsPanel>
        <ItemsPanelTemplate>
            <VirtualizingStackPanel/>
        </ItemsPanelTemplate>
    </ListBox.ItemsPanel>
</ListBox>
```

### 8. Debouncing и Throttling

Создайте универсальные утилиты:

```csharp
// Utils/PerformanceUtils.cs
public static class PerformanceUtils
{
    /// <summary>
    /// Throttle - выполнять не чаще, чем раз в interval
    /// </summary>
    public static Action<T> Throttle<T>(
        Action<T> action, 
        TimeSpan interval)
    {
        var lastRun = DateTime.MinValue;
        var syncLock = new object();

        return arg =>
        {
            lock (syncLock)
            {
                var now = DateTime.Now;
                if (now - lastRun < interval)
                    return;

                lastRun = now;
                action(arg);
            }
        };
    }

    /// <summary>
    /// Debounce - выполнить через delay после последнего вызова
    /// </summary>
    public static Action<T> Debounce<T>(
        Action<T> action, 
        TimeSpan delay)
    {
        CancellationTokenSource? cts = null;

        return arg =>
        {
            cts?.Cancel();
            cts = new CancellationTokenSource();
            var token = cts.Token;

            Task.Run(async () =>
            {
                await Task.Delay(delay, token);
                if (!token.IsCancellationRequested)
                    action(arg);
            }, token);
        };
    }
}
```

**Использование:**

```csharp
// Throttle для частых событий
private readonly Action<string> _throttledLog;

public MainWindowViewModel()
{
    _throttledLog = PerformanceUtils.Throttle<string>(
        msg => _logger.LogDebug(msg),
        TimeSpan.FromMilliseconds(100));
}

// Debounce для автосохранения
private readonly Action<AppSettings> _debouncedSave;

public SettingsViewModel()
{
    _debouncedSave = PerformanceUtils.Debounce<AppSettings>(
        SaveSettings,
        TimeSpan.FromSeconds(1));
}
```

### 9. Оптимизация строк

```csharp
// Использовать StringComparison
if (text.Equals("value", StringComparison.OrdinalIgnoreCase))

// Использовать StringBuilder для конкатенации
var sb = new StringBuilder(capacity: 256);
foreach (var item in items)
    sb.Append(item).Append(", ");

// Использовать Span<T> для работы с частями строк
ReadOnlySpan<char> span = text.AsSpan();
var firstPart = span.Slice(0, 10);

// String interpolation для форматирования
var message = $"User {username} logged in at {timestamp:yyyy-MM-dd}";
```

### 10. Async Patterns

```csharp
// Используйте ValueTask для hot path
public ValueTask<int> GetCountAsync()
{
    if (_cache.TryGetValue("count", out var count))
        return new ValueTask<int>(count); // Синхронное завершение

    return new ValueTask<int>(LoadCountAsync()); // Асинхронное
}

// Используйте IAsyncEnumerable для streaming
public async IAsyncEnumerable<Command> GetCommandsAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var command in _repository.GetAllAsync(ct))
    {
        yield return command;
    }
}

// Cancellation tokens везде
public async Task ProcessAsync(CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    
    await Task.Delay(1000, cancellationToken);
    
    // Периодически проверяйте
    if (cancellationToken.IsCancellationRequested)
        return;
}
```

### 11. Memory Management

```csharp
// Dispose ненужных ресурсов
public class MyService : IDisposable
{
    private readonly Timer _timer;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;

        _timer?.Dispose();
        _disposed = true;
    }
}

// Используйте using для автоматического Dispose
await using var stream = File.OpenRead(path);
using var reader = new StreamReader(stream);

// WeakReference для кэша
private readonly WeakReference<Bitmap> _cachedImage = new(null);

public Bitmap GetImage()
{
    if (_cachedImage.TryGetTarget(out var image))
        return image;

    image = LoadImage();
    _cachedImage.SetTarget(image);
    return image;
}
```

### 12. Benchmarking

Создайте benchmark проект:

```powershell
dotnet new console -n iikoServiceHelper.Benchmarks
cd iikoServiceHelper.Benchmarks
dotnet add package BenchmarkDotNet
```

```csharp
// LayoutConversionBenchmark.cs
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
public class LayoutConversionBenchmark
{
    private const string TestString = "ghbdtn rfr ltkf";

    [Benchmark]
    public string ConvertLayout_Original()
    {
        return StringUtils.ConvertLayout(TestString);
    }

    [Benchmark]
    public string ConvertLayout_Optimized()
    {
        return StringUtilsOptimized.ConvertLayout(TestString);
    }
}

class Program
{
    static void Main(string[] args)
    {
        BenchmarkRunner.Run<LayoutConversionBenchmark>();
    }
}
```

Запуск:

```powershell
dotnet run -c Release
```

## Метрики производительности

### Целевые показатели

- **Startup time**: < 1 секунда
- **Command execution**: < 50ms
- **Settings save**: < 100ms
- **Memory usage**: < 100MB в idle
- **GC Gen2 collections**: < 5 в минуту

### Мониторинг

```csharp
// Services/PerformanceMonitor.cs
public class PerformanceMonitor : IDisposable
{
    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter _ramCounter;
    private readonly ILogger<PerformanceMonitor> _logger;

    public PerformanceMonitor(ILogger<PerformanceMonitor> logger)
    {
        _logger = logger;
        _cpuCounter = new PerformanceCounter(
            "Processor", 
            "% Processor Time", 
            "_Total");
        _ramCounter = new PerformanceCounter(
            "Memory", 
            "Available MBytes");
    }

    public void LogMetrics()
    {
        var cpu = _cpuCounter.NextValue();
        var ram = _ramCounter.NextValue();
        var gcGen0 = GC.CollectionCount(0);
        var gcGen1 = GC.CollectionCount(1);
        var gcGen2 = GC.CollectionCount(2);

        _logger.LogInformation(
            "Performance: CPU={Cpu:F1}%, RAM={Ram}MB, GC(0/1/2)={Gen0}/{Gen1}/{Gen2}",
            cpu, ram, gcGen0, gcGen1, gcGen2);
    }

    public void Dispose()
    {
        _cpuCounter?.Dispose();
        _ramCounter?.Dispose();
    }
}
```

## Критерии завершения

- [ ] Профилирование выполнено, узкие места найдены
- [ ] Асинхронная загрузка настроек реализована
- [ ] Кэширование буфера обмена внедрено
- [ ] Коллекции оптимизированы
- [ ] Lazy loading применен
- [ ] UI virtualization настроена
- [ ] Debouncing/Throttling реализованы
- [ ] Memory management улучшен
- [ ] Benchmarks созданы и запущены
- [ ] Целевые метрики достигнуты

## Полезные команды

```powershell
# Проверка размера сборки
dotnet publish -c Release
Get-ChildItem bin\Release\net8.0-windows10.0.19041.0\publish\ -Recurse | Measure-Object -Property Length -Sum

# Анализ зависимостей
dotnet list package --include-transitive

# IL Spy для анализа compiled code
ilspy bin\Release\net8.0-windows10.0.19041.0\iikoServiceHelper.dll
```
