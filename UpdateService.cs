using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

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

        public UpdateService(Func<string, string, bool> showUpdateDialog, Action<string, string, bool> showCustomMessage)
        {
            _showUpdateDialog = showUpdateDialog;
            _showCustomMessage = showCustomMessage;
        }

        public async Task CheckForUpdates(bool isSilent, DateTime lastUpdateCheck, Action<DateTime> updateLastCheckTime)
        {
            try
            {
                if (isSilent && (DateTime.Now - lastUpdateCheck).TotalHours < 24)
                {
                    return;
                }

                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion == null) return;

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("iikoServiceHelper");
                var json = await client.GetStringAsync("https://api.github.com/repos/BiderMan/iikoServiceHelper_v2_C_Sharp/releases/latest");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.GetProperty("tag_name").GetString() ?? "0.0.0";
                string versionStr = tagName.TrimStart('v');

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
                var errorMessage = $"Ошибка при проверке обновлений: {ex.Message}";
                Debug.WriteLine($"[UpdateService] {errorMessage}");
                if (!isSilent)
                {
                    UpdateFailed?.Invoke("Ошибка сети", errorMessage);
                }
            }
        }

        private async Task PerformUpdate(string url, string version)
        {
            try
            {
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string newFileName = $"iikoServiceHelper_v{version}.exe";
                string savePath = Path.Combine(currentDir, newFileName);

                StatusChanged?.Invoke("Скачивание...");

                using var client = new HttpClient();
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

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