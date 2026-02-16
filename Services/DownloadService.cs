using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using iikoServiceHelper.Constants;

namespace iikoServiceHelper.Services
{
    /// <summary>
    /// Сервис для загрузки файлов из интернета
    /// </summary>
    public class DownloadService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DownloadService>? _logger;
        
        // URL для загрузки файлов
        public const string OrderCheckUrl = "https://clearbat.iiko.online/downloads/OrderCheck.exe";
        public const string ClearBatUrl = "https://clearbat.iiko.online/downloads/CLEAR.bat.exe";

        public DownloadService(ILogger<DownloadService>? logger = null)
        {
            _logger = logger;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        /// <summary>
        /// Скачать файл по URL
        /// </summary>
        /// <param name="url">URL для загрузки</param>
        /// <param name="destinationPath">Путь для сохранения</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Успешность загрузки</returns>
        public async Task<bool> DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger?.LogInformation("Starting download from {Url} to {Path}", url, destinationPath);
                
                var bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);
                
                // Создаем директорию, если не существует
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);
                
                _logger?.LogInformation("Download completed: {Path}", destinationPath);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Download cancelled: {Url}", url);
                // Удаляем частично загруженный файл
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to download from {Url}", url);
                return false;
            }
        }

        /// <summary>
        /// Скачать OrderCheck.exe
        /// </summary>
        public async Task<bool> DownloadOrderCheckAsync(string appDir, CancellationToken cancellationToken = default)
        {
            var exePath = Path.Combine(appDir, "OrderCheck.exe");
            return await DownloadFileAsync(OrderCheckUrl, exePath, cancellationToken);
        }

        /// <summary>
        /// Скачать CLEAR.bat.exe
        /// </summary>
        public async Task<bool> DownloadClearBatAsync(string appDir, CancellationToken cancellationToken = default)
        {
            var exePath = Path.Combine(appDir, "CLEAR.bat.exe");
            return await DownloadFileAsync(ClearBatUrl, exePath, cancellationToken);
        }

        /// <summary>
        /// Проверить доступность URL
        /// </summary>
        public async Task<bool> IsUrlAvailableAsync(string url)
        {
            try
            {
                using var response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
