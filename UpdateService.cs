using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using iikoServiceHelper.Services;

namespace iikoServiceHelper.Services
{
    public class UpdateService
    {
        public event Action<string>? StatusChanged;
        public event Action<double>? ProgressChanged;
        public event Action<string, string>? DownloadCompleted;
        public event Action<string, string>? UpdateFailed;

        private readonly Func<string, string, bool> _showUpdateDialog;
        private readonly Action<string, string, bool> _showCustomMessage;
        private readonly ILogger<UpdateService>? _logger;
        private readonly FileService? _fileService;

        public UpdateService(Func<string, string, bool> showUpdateDialog, Action<string, string, bool> showCustomMessage, ILogger<UpdateService>? logger = null, FileService? fileService = null)
        {
            _showUpdateDialog = showUpdateDialog;
            _showCustomMessage = showCustomMessage;
            _logger = logger;
            _fileService = fileService;
        }

        public async Task CheckForUpdates(bool isSilent, DateTime lastUpdateCheck, Action<DateTime> updateLastCheckTime)
        {
            try
            {
                _logger?.LogInformation("Начало проверки обновлений. Silent: {IsSilent}, LastCheck: {LastCheck}", isSilent, lastUpdateCheck);
                _fileService?.WriteDetailedLog($"[ОБНОВЛЕНИЕ] Начало проверки обновлений. Silent: {isSilent}, LastCheck: {lastUpdateCheck}");
                
                // Включаем поддержку TLS 1.2 для GitHub API
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
                
                if (isSilent && (DateTime.Now - lastUpdateCheck).TotalHours < 24)
                {
                    _logger?.LogDebug("Проверка обновлений пропущена (тихий режим, прошло менее 24ч)");
                    return;
                }

                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion == null) {
                    _logger?.LogWarning("Не удалось получить версию текущей сборки");
                    _fileService?.WriteDetailedLog("[ОБНОВЛЕНИЕ] Не удалось получить версию текущей сборки");
                    return;
                }

                _logger?.LogInformation("Текущая версия: {CurrentVersion}, запрашиваю информацию о последнем релизе...", currentVersion);
                _fileService?.WriteDetailedLog($"[ОБНОВЛЕНИЕ] Текущая версия: {currentVersion}, запрашиваю информацию о последнем релизе...");

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("iikoServiceHelper/1.0");
                var json = await client.GetStringAsync("https://api.github.com/repos/BiderMan/iikoServiceHelper_v2_C_Sharp/releases/latest");
                _logger?.LogDebug("Получен ответ от GitHub API, парсинг...");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.GetProperty("tag_name").GetString() ?? "0.0.0";
                string versionStr = tagName.TrimStart('v');
                _logger?.LogInformation("Доступная версия на GitHub: {RemoteVersion}", versionStr);
                _fileService?.WriteDetailedLog($"[ОБНОВЛЕНИЕ] Доступная версия на GitHub: {versionStr}");

                if (Version.TryParse(versionStr, out var remoteVersion))
                {
                    if (remoteVersion > currentVersion)
                    {
                        updateLastCheckTime(DateTime.Now);

                        if (_showUpdateDialog(tagName, currentVersion.ToString()))
                        {
                            string currentExeName = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName ?? "iikoServiceHelper.exe");
                            string downloadUrl = "";

                            if (root.TryGetProperty("assets", out var assets))
                            {
                                foreach (var asset in assets.EnumerateArray())
                                {
                                    string name = asset.GetProperty("name").GetString() ?? "";
                                    bool isCompact = currentExeName.Contains("Compact", StringComparison.OrdinalIgnoreCase);

                                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (name.Equals(currentExeName, StringComparison.OrdinalIgnoreCase)) {
                                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                            break;
                                        }
                                        if (string.IsNullOrEmpty(downloadUrl) && (isCompact == name.Contains("Compact", StringComparison.OrdinalIgnoreCase))) {
                                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                        }
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(downloadUrl))
                            {
                                await PerformUpdate(downloadUrl, tagName);
                            }
                            else
                            {
                                UpdateFailed?.Invoke("Ошибка релиза", "Не удалось найти подходящий .exe файл для вашей версии в последнем релизе.");
                            }
                        }
                    }
                    else if (!isSilent)
                    {
                        _showCustomMessage("Обновление", "У вас установлена последняя версия.", false);
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMessage;
                string errorTitle = "Ошибка сети";
                
                // Определяем тип ошибки для более понятного сообщения
                if (ex.Message.Contains("SSL") || ex.Message.Contains("ssl"))
                {
                    errorMessage = "Не удалось установить защищённое соединение.\n\nВозможные причины:\n• Нет подключения к интернету\n• Блокировка антивирусом или firewall\n• Проблемы с SSL-сертификатами\n\nПроверьте подключение к интернету и повторите попытку.";
                }
                else if (ex.Message.Contains("404") || ex.Message.Contains("Not Found"))
                {
                    errorMessage = "Не удалось найти сервер обновлений.\nПопробуйте позже.";
                }
                else if (ex.Message.Contains("timeout") || ex.Message.Contains("Timeout"))
                {
                    errorMessage = "Время ожидания ответа истекло.\nПроверьте подключение к интернету.";
                }
                else
                {
                    errorMessage = $"Произошла ошибка при проверке обновлений: {ex.Message}";
                }
                
                _logger?.LogError(ex, "Ошибка при проверке обновлений. Title: {ErrorTitle}, Message: {ErrorMessage}", errorTitle, errorMessage);
                _fileService?.WriteDetailedLog($"[ОБНОВЛЕНИЕ] ОШИБКА: {errorTitle} - {ex.Message}");
                Debug.WriteLine($"[UpdateService] {errorTitle}: {ex.Message}");
                if (!isSilent)
                {
                    UpdateFailed?.Invoke(errorTitle, errorMessage);
                }
            }
        }

        private async Task PerformUpdate(string url, string version)
        {
            try
            {
                _logger?.LogInformation("Начало скачивания обновления версии {Version}", version);
                _logger?.LogDebug("URL для скачивания: {Url}", url);

                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string newFileName = $"iikoServiceHelper_v{version}.exe";
                string savePath = Path.Combine(currentDir, newFileName);
                _logger?.LogDebug("Путь сохранения: {SavePath}", savePath);
                _fileService?.WriteDetailedLog($"[ОБНОВЛЕНИЕ] Начало скачивания версии {version}. Путь: {savePath}");

                StatusChanged?.Invoke("Скачивание...");

                using var client = new HttpClient();
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                _logger?.LogDebug("Статус ответа: {StatusCode}", response.StatusCode);
                _fileService?.WriteDetailedLog($"[ОБНОВЛЕНИЕ] Статус ответа: {response.StatusCode}");
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;
                _logger?.LogInformation("Размер файла: {TotalBytes} байт", totalBytes);
                _fileService?.WriteDetailedLog($"[ОБНОВЛЕНИЕ] Размер файла: {totalBytes} байт");

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    if (canReportProgress) ProgressChanged?.Invoke((double)totalRead / totalBytes * 100);
                }

                _logger?.LogInformation("Скачивание завершено. Загружено: {TotalRead} байт", totalRead);
                _fileService?.WriteDetailedLog($"[ОБНОВЛЕНИЕ] Скачивание завершено. Загружено: {totalRead} байт");

                // Проверка целостности файла (простая проверка - файл не пустой)
                if (new FileInfo(savePath).Length == 0)
                {
                    throw new IOException("Downloaded file is empty");
                }

                DownloadCompleted?.Invoke(newFileName, savePath);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Ошибка при скачивании обновления: {ex.Message}";
                Debug.WriteLine($"[UpdateService] {errorMessage}");
                UpdateFailed?.Invoke("Ошибка скачивания", errorMessage);
                
                // Reset UI state
                StatusChanged?.Invoke("Обновить");
                ProgressChanged?.Invoke(0);
            }
        }
    }
}