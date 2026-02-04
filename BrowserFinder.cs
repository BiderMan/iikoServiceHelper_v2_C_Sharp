using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using iikoServiceHelper.Models;
using System.Linq;

namespace iikoServiceHelper.Services
{
    public static class BrowserFinder
    {
        public static List<BrowserItem> FindAll()
        {
            var targetBrowsers = new[] { "msedge", "chrome", "browser", "vivaldi", "opera", "brave", "chromium" };
            var foundBrowsers = new List<BrowserItem>();

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            // 1. Ищем по стандартным путям (даже если не запущены)
            var commonPaths = new List<(string Name, string Path)>
            {
                ("Edge", Path.Combine(programFilesX86, @"Microsoft\Edge\Application\msedge.exe")),
                ("Edge", Path.Combine(programFiles, @"Microsoft\Edge\Application\msedge.exe")),
                ("Chrome", Path.Combine(programFiles, @"Google\Chrome\Application\chrome.exe")),
                ("Chrome", Path.Combine(programFilesX86, @"Google\Chrome\Application\chrome.exe")),
                ("Yandex", Path.Combine(localAppData, @"Yandex\YandexBrowser\Application\browser.exe")),
                ("Vivaldi", Path.Combine(localAppData, @"Vivaldi\Application\vivaldi.exe")),
                ("Brave", Path.Combine(programFiles, @"BraveSoftware\Brave-Browser\Application\brave.exe")),
                ("Opera", Path.Combine(localAppData, @"Programs\Opera\launcher.exe")),
                ("Opera GX", Path.Combine(localAppData, @"Programs\Opera GX\launcher.exe")),
                ("Chromium", Path.Combine(localAppData, @"Chromium\Application\chrome.exe"))
            };

            foreach (var item in commonPaths)
            {
                try
                {
                    // Проверяем, что путь находится внутри разрешенных каталогов для предотвращения path traversal атак
                    if (IsPathSafe(item.Path) && File.Exists(item.Path))
                    {
                        if (!foundBrowsers.Any(b => b.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)))
                        {
                            foundBrowsers.Add(new BrowserItem { Name = item.Name, Path = item.Path });
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Логируем ошибку доступа к файлу
                    Debug.WriteLine($"Error accessing path {item.Path}: {ex.Message}");
                }
            }

            // 2. Ищем запущенные процессы (для нестандартных путей)
            foreach (var procName in targetBrowsers)
            {
                var processes = Process.GetProcessesByName(procName);
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.MainModule != null)
                        {
                            string? path = p.MainModule.FileName;
                            if (string.IsNullOrEmpty(path)) continue;

                            string name = procName.ToLower() switch
                            {
                                "msedge" => "Edge",
                                "chrome" => "Chrome",
                                "browser" => "Yandex",
                                "vivaldi" => "Vivaldi",
                                "opera" => "Opera",
                                "brave" => "Brave",
                                "chromium" => "Chromium",
                                _ => procName
                            };

                            if (!foundBrowsers.Any(b => b.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                            {
                                foundBrowsers.Add(new BrowserItem { Name = name, Path = path });
                            }
                            break; // Достаточно одного процесса для получения пути
                        }
                    }
                    catch { /* Игнорируем ошибки доступа к системным процессам */ }
                }
            }
            return foundBrowsers;
        }
        
        // Добавляем вспомогательный метод для проверки безопасности пути
        private static bool IsPathSafe(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return false;
            
            try
            {
                var rootPath = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var validRoots = new[]
                {
                    Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
                    Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
                    Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
                };
                
                return validRoots.Any(validRoot =>
                    string.Equals(rootPath, validRoot, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
    }
}