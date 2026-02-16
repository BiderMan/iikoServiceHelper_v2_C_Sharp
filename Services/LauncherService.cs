using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace iikoServiceHelper.Services
{
    /// <summary>
    /// Сервис для запуска внешних приложений и процессов
    /// </summary>
    public class LauncherService
    {
        private readonly ILogger<LauncherService>? _logger;

        public LauncherService(ILogger<LauncherService>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Запустить приложение по пути к исполняемому файлу
        /// </summary>
        /// <param name="exePath">Путь к exe файлу</param>
        /// <param name="useShellExecute">Использовать ShellExecute (для открытия файлов)</param>
        /// <returns>Успешность запуска</returns>
        public bool LaunchApplication(string exePath, bool useShellExecute = true)
        {
            try
            {
                if (!File.Exists(exePath))
                {
                    _logger?.LogWarning("File not found: {Path}", exePath);
                    return false;
                }

                var startInfo = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = useShellExecute
                };
                
                Process.Start(startInfo);
                _logger?.LogInformation("Launched application: {Path}", exePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to launch application: {Path}", exePath);
                return false;
            }
        }

        /// <summary>
        /// Запустить приложение с аргументами
        /// </summary>
        public bool LaunchApplication(string exePath, string arguments, bool useShellExecute = true)
        {
            try
            {
                var startInfo = new ProcessStartInfo(exePath, arguments)
                {
                    UseShellExecute = useShellExecute
                };
                
                Process.Start(startInfo);
                _logger?.LogInformation("Launched application with args: {Path} {Args}", exePath, arguments);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to launch application with args: {Path}", exePath);
                return false;
            }
        }

        /// <summary>
        /// Открыть папку в проводнике
        /// </summary>
        public bool OpenFolder(string path)
        {
            try
            {
                Process.Start("explorer.exe", path);
                _logger?.LogInformation("Opened folder: {Path}", path);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to open folder: {Path}", path);
                return false;
            }
        }

        /// <summary>
        /// Открыть FTP/сетевую папку
        /// </summary>
        public async Task<bool> OpenNetworkFolderAsync(string networkPath, string? username = null, string? password = null)
        {
            try
            {
                // Сначала отключаем существующее подключение
                await RunCommandAsync("net", $@"use \\{networkPath} /delete /y");
                
                // Затем подключаем с новыми учетными данными
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    await RunCommandAsync("net", $@"use \\{networkPath} /user:{username} {password} /persistent:yes");
                }
                
                // Открываем папку
                return OpenFolder(networkPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to open network folder: {Path}", networkPath);
                return false;
            }
        }

        /// <summary>
        /// Запустить команду и дождаться завершения
        /// </summary>
        public async Task<bool> RunCommandAsync(string fileName, string arguments, bool createNoWindow = true)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo(fileName, arguments)
                    {
                        CreateNoWindow = createNoWindow,
                        UseShellExecute = false
                    }
                };
                
                process.Start();
                await process.WaitForExitAsync();
                
                _logger?.LogInformation("Command completed: {FileName} {Args}", fileName, arguments);
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to run command: {FileName} {Args}", fileName, arguments);
                return false;
            }
        }

        /// <summary>
        /// Проверить существование файла
        /// </summary>
        public bool FileExists(string path) => File.Exists(path);

        /// <summary>
        /// Проверить существование директории
        /// </summary>
        public bool DirectoryExists(string path) => Directory.Exists(path);
    }
}
