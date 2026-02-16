using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iikoServiceHelper.Services;

namespace iikoServiceHelper.ViewModels
{
    public partial class ToolsViewModel : ViewModelBase
    {
        private readonly LauncherService _launcherService;
        private readonly DownloadService _downloadService;
        private readonly CrmViewModel _crmViewModel;

        /// <summary>
        /// Путь к директории приложения
        /// </summary>
        public string AppDir { get; set; } = string.Empty;

        /// <summary>
        /// Событие для показа MessageBox
        /// </summary>
        public event Action<string, string, bool>? ShowMessageRequested;

        /// <summary>
        /// Событие для показа временного уведомления
        /// </summary>
        public event Action<string>? NotificationRequested;

        /// <summary>
        /// Событие для изменения текста кнопки (скачивание/готово)
        /// </summary>
        public event Action<string, bool>? ButtonStateChanged;

        public ToolsViewModel(
            CrmViewModel crmViewModel,
            LauncherService launcherService,
            DownloadService downloadService)
        {
            _crmViewModel = crmViewModel;
            _launcherService = launcherService;
            _downloadService = downloadService;
        }

        /// <summary>
        /// Открыть OrderCheck
        /// </summary>
        [RelayCommand]
        private async Task OpenOrderCheckAsync()
        {
            string exePath = Path.Combine(AppDir, "OrderCheck.exe");
            
            if (File.Exists(exePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    ShowMessageRequested?.Invoke("Ошибка", ex.Message, true);
                }
            }
            else
            {
                ButtonStateChanged?.Invoke("СКАЧИВАНИЕ...", false);
                try
                {
                    var success = await _downloadService.DownloadOrderCheckAsync(AppDir);
                    if (success)
                    {
                        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                    }
                    else
                    {
                        ShowMessageRequested?.Invoke("Ошибка", "Не удалось скачать OrderCheck.exe", true);
                    }
                }
                catch (Exception ex)
                {
                    ShowMessageRequested?.Invoke("Ошибка", ex.Message, true);
                }
                finally
                {
                    ButtonStateChanged?.Invoke("ORDERCHECK", true);
                }
            }
        }

        /// <summary>
        /// Открыть FTP (сетевую папку)
        /// </summary>
        [RelayCommand]
        private async Task OpenFtpAsync()
        {
            const string networkPath = "files.resto.lan";
            string user = _crmViewModel.CrmLogin;
            string pass = _crmViewModel.CrmPassword;

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            {
                ShowMessageRequested?.Invoke("Ошибка", "Для доступа к FTP заполните Логин и Пароль от CRM.", true);
                return;
            }

            try
            {
                await _launcherService.OpenNetworkFolderAsync(networkPath, user, pass);
            }
            catch (Exception ex)
            {
                ShowMessageRequested?.Invoke("Ошибка", ex.Message, true);
            }
        }

        /// <summary>
        /// Копировать ссылку на POS M
        /// </summary>
        [RelayCommand]
        private void CopyPosLink()
        {
            try
            {
                Clipboard.SetText("https://iiko.cards/ru-RU/About/DownloadPosInstaller?useRc=False");
                NotificationRequested?.Invoke("URL POS-ник M скопирован!");
            }
            catch (Exception ex)
            {
                ShowMessageRequested?.Invoke("Ошибка", ex.Message, true);
            }
        }

        /// <summary>
        /// Копировать ссылку на POS M1
        /// </summary>
        [RelayCommand]
        private void CopyPosM1Link()
        {
            try
            {
                Clipboard.SetText("https://m1.iiko.cards/ru-RU/About/DownloadPosInstaller?useRc=False");
                NotificationRequested?.Invoke("URL POS-ник M1 скопирован!");
            }
            catch (Exception ex)
            {
                ShowMessageRequested?.Invoke("Ошибка", ex.Message, true);
            }
        }

        /// <summary>
        /// Открыть папку с логами
        /// </summary>
        [RelayCommand]
        private void OpenLogFolder()
        {
            try
            {
                _launcherService.OpenFolder(AppDir);
            }
            catch (Exception ex)
            {
                ShowMessageRequested?.Invoke("Ошибка", ex.Message, true);
            }
        }

        /// <summary>
        /// Открыть CLEAR.bat
        /// </summary>
        [RelayCommand]
        private async Task OpenClearBatAsync()
        {
            string exePath = Path.Combine(AppDir, "CLEAR.bat.exe");
            
            if (File.Exists(exePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    ShowMessageRequested?.Invoke("Ошибка", ex.Message, true);
                }
            }
            else
            {
                ButtonStateChanged?.Invoke("СКАЧИВАНИЕ...", false);
                try
                {
                    var success = await _downloadService.DownloadClearBatAsync(AppDir);
                    if (success)
                    {
                        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                    }
                    else
                    {
                        ShowMessageRequested?.Invoke("Ошибка", "Не удалось скачать CLEAR.bat.exe", true);
                    }
                }
                catch (Exception ex)
                {
                    ShowMessageRequested?.Invoke("Ошибка", ex.Message, true);
                }
                finally
                {
                    ButtonStateChanged?.Invoke("CLEAR.bat", true);
                }
            }
        }
    }
}
