---
description: Улучшение безопасности приложения
---

# Улучшение безопасности

## Описание

Workflow для повышения уровня безопасности приложения, включая улучшенное управление паролями, валидацию входных данных и защиту от атак.

## Приоритет: Высокий

## Шаги выполнения

### 1. Использовать SecureString для паролей

#### Проблема

Пароли хранятся в памяти как обычные строки, что небезопасно.

#### Решение

Создайте файл `Utils/SecureStringHelper.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace iikoServiceHelper.Utils
{
    public static class SecureStringHelper
    {
        /// <summary>
        /// Конвертировать SecureString в зашифрованный byte array
        /// </summary>
        public static byte[] SecureStringToEncryptedBytes(SecureString secureString)
        {
            if (secureString == null || secureString.Length == 0)
                return Array.Empty<byte>();

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToBSTR(secureString);
                var length = secureString.Length * 2;
                var bytes = new byte[length];
                Marshal.Copy(ptr, bytes, 0, length);
                
                // Шифруем с помощью DPAPI
                return ProtectedData.Protect(
                    bytes, 
                    null, 
                    DataProtectionScope.CurrentUser);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    Marshal.ZeroFreeBSTR(ptr);
            }
        }

        /// <summary>
        /// Конвертировать зашифрованный byte array в SecureString
        /// </summary>
        public static SecureString EncryptedBytesToSecureString(byte[] encryptedBytes)
        {
            if (encryptedBytes == null || encryptedBytes.Length == 0)
                return new SecureString();

            byte[] decryptedBytes = null;
            try
            {
                decryptedBytes = ProtectedData.Unprotect(
                    encryptedBytes, 
                    null, 
                    DataProtectionScope.CurrentUser);

                var secureString = new SecureString();
                for (int i = 0; i < decryptedBytes.Length; i += 2)
                {
                    if (i + 1 < decryptedBytes.Length)
                    {
                        char c = (char)(decryptedBytes[i] | (decryptedBytes[i + 1] << 8));
                        secureString.AppendChar(c);
                    }
                }
                secureString.MakeReadOnly();
                return secureString;
            }
            finally
            {
                // Очистить чувствительные данные из памяти
                if (decryptedBytes != null)
                    Array.Clear(decryptedBytes, 0, decryptedBytes.Length);
            }
        }

        /// <summary>
        /// Безопасно сравнить два SecureString
        /// </summary>
        public static bool AreEqual(SecureString ss1, SecureString ss2)
        {
            if (ss1 == null || ss2 == null)
                return ss1 == ss2;

            if (ss1.Length != ss2.Length)
                return false;

            IntPtr bstr1 = IntPtr.Zero;
            IntPtr bstr2 = IntPtr.Zero;

            try
            {
                bstr1 = Marshal.SecureStringToBSTR(ss1);
                bstr2 = Marshal.SecureStringToBSTR(ss2);

                unsafe
                {
                    for (char* ptr1 = (char*)bstr1, ptr2 = (char*)bstr2;
                         *ptr1 != 0 && *ptr2 != 0;
                         ++ptr1, ++ptr2)
                    {
                        if (*ptr1 != *ptr2)
                            return false;
                    }
                }
                return true;
            }
            finally
            {
                if (bstr1 != IntPtr.Zero)
                    Marshal.ZeroFreeBSTR(bstr1);
                if (bstr2 != IntPtr.Zero)
                    Marshal.ZeroFreeBSTR(bstr2);
            }
        }
    }
}
```

#### Обновите AppSettings.cs

```csharp
public class AppSettings
{
    [JsonIgnore]
    public SecureString CrmPasswordSecure { get; set; } = new SecureString();

    // Для сериализации
    public byte[] CrmPasswordEncryptedBytes 
    { 
        get => SecureStringHelper.SecureStringToEncryptedBytes(CrmPasswordSecure);
        set => CrmPasswordSecure = SecureStringHelper.EncryptedBytesToSecureString(value);
    }

    // Устаревшее свойство для обратной совместимости
    [Obsolete("Use CrmPasswordSecure instead")]
    [JsonIgnore]
    public string CrmPassword
    {
        get => string.Empty;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                CrmPasswordSecure = new SecureString();
                foreach (char c in value)
                    CrmPasswordSecure.AppendChar(c);
                CrmPasswordSecure.MakeReadOnly();
            }
        }
    }
}
```

### 2. Добавить валидацию входных данных

Создайте файл `Validation/InputValidator.cs`:

```csharp
using System.Text.RegularExpressions;

namespace iikoServiceHelper.Validation
{
    public static partial class InputValidator
    {
        [GeneratedRegex(@"^[a-zA-Z0-9_.+-]+@[a-zA-Z0-9-]+\.[a-zA-Z0-9-.]+$")]
        private static partial Regex EmailRegex();

        [GeneratedRegex(@"^[a-zA-Z0-9_-]+$")]
        private static partial Regex UsernameRegex();

        [GeneratedRegex(@"^https?://")]
        private static partial Regex UrlRegex();

        /// <summary>
        /// Валидация email адреса
        /// </summary>
        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            return EmailRegex().IsMatch(email);
        }

        /// <summary>
        /// Валидация имени пользователя (только буквы, цифры, _, -)
        /// </summary>
        public static bool IsValidUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            if (username.Length < 3 || username.Length > 50)
                return false;

            return UsernameRegex().IsMatch(username);
        }

        /// <summary>
        /// Валидация URL
        /// </summary>
        public static bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   UrlRegex().IsMatch(url);
        }

        /// <summary>
        /// Валидация пути к файлу
        /// </summary>
        public static bool IsValidFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Санитизация имени файла (удаление недопустимых символов)
        /// </summary>
        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalidChars));
        }
    }
}
```

#### Использование в CrmViewModel

```csharp
[RelayCommand]
private async Task LoginAsync()
{
    if (!InputValidator.IsValidUsername(CrmLogin))
    {
        ShowError("Некорректное имя пользователя");
        return;
    }

    // ... логика авторизации
}
```

### 3. Добавить Rate Limiting для CRM авторизации

Создайте файл `Services/RateLimiter.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace iikoServiceHelper.Services
{
    /// <summary>
    /// Rate limiter для предотвращения слишком частых запросов
    /// </summary>
    public class RateLimiter
    {
        private readonly ConcurrentDictionary<string, DateTime> _lastAttempts = new();
        private readonly TimeSpan _minimumInterval;
        private readonly int _maxAttemptsPerMinute;
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _attemptHistory = new();

        public RateLimiter(
            TimeSpan minimumInterval, 
            int maxAttemptsPerMinute = 5)
        {
            _minimumInterval = minimumInterval;
            _maxAttemptsPerMinute = maxAttemptsPerMinute;
        }

        /// <summary>
        /// Проверить, можно ли выполнить действие
        /// </summary>
        public bool CanExecute(string key)
        {
            var now = DateTime.Now;

            // Проверка минимального интервала
            if (_lastAttempts.TryGetValue(key, out var lastAttempt))
            {
                if (now - lastAttempt < _minimumInterval)
                    return false;
            }

            // Проверка количества попыток в минуту
            if (!_attemptHistory.TryGetValue(key, out var history))
            {
                history = new Queue<DateTime>();
                _attemptHistory[key] = history;
            }

            // Удалить старые попытки (старше 1 минуты)
            while (history.Count > 0 && now - history.Peek() > TimeSpan.FromMinutes(1))
            {
                history.Dequeue();
            }

            if (history.Count >= _maxAttemptsPerMinute)
                return false;

            return true;
        }

        /// <summary>
        /// Зарегистрировать попытку выполнения
        /// </summary>
        public void RecordAttempt(string key)
        {
            var now = DateTime.Now;
            _lastAttempts[key] = now;

            if (_attemptHistory.TryGetValue(key, out var history))
            {
                history.Enqueue(now);
            }
        }

        /// <summary>
        /// Выполнить действие с rate limiting
        /// </summary>
        public async Task<T> ExecuteAsync<T>(
            string key, 
            Func<Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            if (!CanExecute(key))
            {
                throw new InvalidOperationException(
                    $"Rate limit exceeded for '{key}'. " +
                    $"Please wait {_minimumInterval.TotalSeconds} seconds.");
            }

            RecordAttempt(key);
            return await action();
        }
    }
}
```

#### Использование в CrmAutoLoginService

```csharp
public class CrmAutoLoginService
{
    private readonly RateLimiter _rateLimiter = new(
        minimumInterval: TimeSpan.FromSeconds(5),
        maxAttemptsPerMinute: 3);

    public async Task RunBackgroundLogin()
    {
        try
        {
            await _rateLimiter.ExecuteAsync(
                "crm-login",
                async () =>
                {
                    // Логика авторизации
                    await PerformLoginAsync();
                    return true;
                });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex.Message);
            StatusUpdated?.Invoke("Rate limit превышен");
        }
    }
}
```

### 4. Добавить защиту от Path Traversal

Создайте метод для безопасной работы с путями:

```csharp
// Utils/PathHelper.cs
public static class PathHelper
{
    /// <summary>
    /// Безопасно объединить пути, предотвращая path traversal
    /// </summary>
    public static string SafeCombine(string basePath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new ArgumentException("Base path cannot be empty", nameof(basePath));

        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Relative path cannot be empty", nameof(relativePath));

        // Убрать потенциально опасные символы
        relativePath = relativePath.Replace("..", string.Empty)
                                   .Replace(":", string.Empty)
                                   .TrimStart('/', '\\');

        var fullPath = Path.GetFullPath(Path.Combine(basePath, relativePath));

        // Проверить, что результирующий путь находится внутри базового
        if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "Attempted path traversal attack detected");
        }

        return fullPath;
    }

    /// <summary>
    /// Получить безопасный путь к файлу настроек
    /// </summary>
    public static string GetSettingsFilePath(string fileName)
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "iikoServiceHelper_v2");

        return SafeCombine(appDataPath, InputValidator.SanitizeFileName(fileName));
    }
}
```

### 5. Добавить защищенное хранение конфиденциальных настроек

Создайте `Services/SecureSettingsService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace iikoServiceHelper.Services
{
    /// <summary>
    /// Сервис для безопасного хранения конфиденциальных настроек
    /// </summary>
    public class SecureSettingsService
    {
        private readonly string _settingsPath;

        public SecureSettingsService(string settingsPath)
        {
            _settingsPath = settingsPath;
        }

        /// <summary>
        /// Сохранить настройки с шифрованием
        /// </summary>
        public async Task SaveAsync<T>(T settings) where T : class
        {
            var json = JsonSerializer.Serialize(settings);
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            // Шифрование с помощью DPAPI
            var encryptedBytes = ProtectedData.Protect(
                jsonBytes,
                null,
                DataProtectionScope.CurrentUser);

            await File.WriteAllBytesAsync(_settingsPath, encryptedBytes);
        }

        /// <summary>
        /// Загрузить настройки с расшифровкой
        /// </summary>
        public async Task<T?> LoadAsync<T>() where T : class
        {
            if (!File.Exists(_settingsPath))
                return null;

            try
            {
                var encryptedBytes = await File.ReadAllBytesAsync(_settingsPath);
                var jsonBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser);

                var json = Encoding.UTF8.GetString(jsonBytes);
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (CryptographicException ex)
            {
                // Файл поврежден или создан другим пользователем
                System.Diagnostics.Debug.WriteLine(
                    $"Failed to decrypt settings: {ex.Message}");
                return null;
            }
        }
    }
}
```

### 6. Добавить аудит безопасности

Создайте `Services/SecurityAuditService.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace iikoServiceHelper.Services
{
    /// <summary>
    /// Сервис для аудита событий безопасности
    /// </summary>
    public class SecurityAuditService
    {
        private readonly ILogger<SecurityAuditService> _logger;
        private readonly string _auditLogPath;

        public SecurityAuditService(
            ILogger<SecurityAuditService> logger,
            string appDataPath)
        {
            _logger = logger;
            _auditLogPath = Path.Combine(appDataPath, "security_audit.log");
        }

        /// <summary>
        /// Записать событие авторизации
        /// </summary>
        public async Task LogAuthenticationAttemptAsync(
            string username, 
            bool success,
            string? failureReason = null)
        {
            var message = success
                ? $"Successful login for user: {username}"
                : $"Failed login attempt for user: {username}. Reason: {failureReason}";

            _logger.LogInformation(message);
            await AppendToAuditLogAsync("AUTH", message);
        }

        /// <summary>
        /// Записать подозрительную активность
        /// </summary>
        public async Task LogSuspiciousActivityAsync(
            string activityType, 
            string details)
        {
            var message = $"Suspicious activity detected: {activityType}. Details: {details}";
            _logger.LogWarning(message);
            await AppendToAuditLogAsync("SUSPICIOUS", message);
        }

        /// <summary>
        /// Записать изменение настроек безопасности
        /// </summary>
        public async Task LogSecuritySettingChangeAsync(
            string settingName, 
            string? oldValue, 
            string? newValue)
        {
            var message = $"Security setting changed: {settingName}. " +
                         $"Old: {oldValue ?? "null"}, New: {newValue ?? "null"}";
            _logger.LogInformation(message);
            await AppendToAuditLogAsync("CONFIG", message);
        }

        private async Task AppendToAuditLogAsync(string category, string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] [{category}] {message}{Environment.NewLine}";

            try
            {
                await File.AppendAllTextAsync(_auditLogPath, logEntry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write to audit log");
            }
        }
    }
}
```

### 7. Проверка безопасности

Создайте контрольный список:

```markdown
## Чеклист безопасности

- [ ] Пароли хранятся как SecureString
- [ ] Все входные данные валидируются
- [ ] Rate limiting настроен для критических операций
- [ ] Защита от path traversal реализована
- [ ] Конфиденциальные настройки шифруются
- [ ] События безопасности логируются
- [ ] Нет hardcoded credentials
- [ ] HTTPS используется для всех внешних запросов
- [ ] Сертификаты проверяются
- [ ] Timeout настроены для сетевых операций
```

## Критерии завершения

- [ ] SecureString используется для всех паролей
- [ ] InputValidator применяется ко всем пользовательским вводам
- [ ] RateLimiter защищает критические операции
- [ ] PathHelper предотвращает path traversal
- [ ] SecureSettingsService шифрует конфиденциальные данные
- [ ] SecurityAuditService логирует события безопасности
- [ ] Чеклист безопасности проверен

## Дополнительные рекомендации

1. **Регулярные обновления зависимостей**

   ```powershell
   dotnet list package --outdated
   dotnet add package [PackageName] --version [NewVersion]
   ```

2. **Security scan**

   ```powershell
   dotnet tool install -g security-scan
   security-scan iikoServiceHelper.csproj
   ```

3. **Code analysis**
   Добавьте в .csproj:

   ```xml
   <PropertyGroup>
     <EnableNETAnalyzers>true</EnableNETAnalyzers>
     <AnalysisLevel>latest</AnalysisLevel>
     <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
   </PropertyGroup>
   ```
