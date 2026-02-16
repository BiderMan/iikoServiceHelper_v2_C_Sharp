using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iikoServiceHelper.Models;
using iikoServiceHelper.Services;
using iikoServiceHelper.Utils;

namespace iikoServiceHelper.ViewModels
{
    public partial class CrmViewModel : ViewModelBase
    {
        private readonly AppSettings _settings;
        private readonly CrmAutoLoginService _crmService;

        [ObservableProperty]
        private string _crmLogin = string.Empty;

        [ObservableProperty]
        private string _crmPassword = string.Empty;

        [ObservableProperty]
        private string _crmStatus = "Статус: Отключено";

        [ObservableProperty]
        private string _lastRun = string.Empty;

        [ObservableProperty]
        private string _log = string.Empty;

        [ObservableProperty]
        private bool _isAutoLoginActive;

        [ObservableProperty]
        private ObservableCollection<BrowserItem> _browsers = new();

        [ObservableProperty]
        private BrowserItem? _selectedBrowser;

        public CrmViewModel(AppSettings settings, CrmAutoLoginService crmService)
        {
            _settings = settings;
            _crmService = crmService;

            CrmLogin = settings.CrmLogin;
            CrmPassword = settings.CrmPassword;
            IsAutoLoginActive = crmService.IsActive;

            _crmService.LogMessage += msg => Log += msg + "\n";
            _crmService.StatusUpdated += status => CrmStatus = status;
            _crmService.LastRunUpdated += lastRun => LastRun = lastRun;

            RefreshBrowsers();
            SelectedBrowser = Browsers.FirstOrDefault(b => b.Path == settings.SelectedBrowser);
        }

        [RelayCommand]
        private void ToggleAutoLogin()
        {
            if (IsAutoLoginActive)
            {
                _crmService.Stop();
                IsAutoLoginActive = false;
            }
            else
            {
                if (SelectedBrowser != null)
                {
                    _crmService.Start(CrmLogin, CrmPassword, SelectedBrowser);
                    IsAutoLoginActive = true;
                }
            }
        }

        [RelayCommand]
        private void RefreshBrowsers()
        {
            var found = BrowserFinder.FindAll();
            Browsers.Clear();
            foreach (var b in found) Browsers.Add(b);

            if (SelectedBrowser == null && Browsers.Any())
                SelectedBrowser = Browsers.First();
        }

        [RelayCommand]
        private async Task CheckPortAsync()
        {
            const int port = 9222;
            const string host = "localhost";
            
            CrmStatus = $"Проверка порта {port}...";
            Log += $"[{DateTime.Now:HH:mm:ss}] Проверка порта {port}...\n";
            
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                
                // Ждем максимум 3 секунды
                var timeoutTask = Task.Delay(3000);
                
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                
                if (completedTask == connectTask && client.Connected)
                {
                    CrmStatus = $"Порт {port} ОТКРЫТ";
                    Log += $"[{DateTime.Now:HH:mm:ss}] Порт {port} открыт!\n";
                }
                else
                {
                    CrmStatus = $"Порт {port} ЗАКРЫТ";
                    Log += $"[{DateTime.Now:HH:mm:ss}] Порт {port} закрыт или недоступен.\n";
                }
            }
            catch (SocketException ex)
            {
                CrmStatus = $"Порт {port} ЗАКРЫТ";
                Log += $"[{DateTime.Now:HH:mm:ss}] Ошибка проверки порта: {ex.Message}\n";
            }
            catch (Exception ex)
            {
                CrmStatus = $"Ошибка: {ex.Message}";
                Log += $"[{DateTime.Now:HH:mm:ss}] Ошибка: {ex.Message}\n";
            }
        }

        partial void OnCrmLoginChanged(string value) => _settings.CrmLogin = value;
        partial void OnCrmPasswordChanged(string value) => _settings.CrmPassword = value;
        partial void OnSelectedBrowserChanged(BrowserItem? value)
        {
            if (value != null) _settings.SelectedBrowser = value.Path;
        }
    }
}
