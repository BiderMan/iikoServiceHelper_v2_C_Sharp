using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using iikoServiceHelper.Services;

namespace iikoServiceHelper.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly IHotkeyManager? _hotkeyManager;

        [ObservableProperty]
        private string _title = "iikoServiceHelper v2";

        [ObservableProperty]
        private string _statusText = "Готов";

        [ObservableProperty]
        private bool _isExecuting = false;

        [ObservableProperty]
        private bool _isAltBlockerEnabled;

        [ObservableProperty]
        private int _commandCount;

        public NotesViewModel NotesViewModel { get; }
        public CrmViewModel CrmViewModel { get; }
        public SettingsViewModel SettingsViewModel { get; }
        public ToolsViewModel ToolsViewModel { get; }

        public MainWindowViewModel(
            NotesViewModel notesViewModel,
            CrmViewModel crmViewModel,
            SettingsViewModel settingsViewModel,
            ToolsViewModel toolsViewModel,
            IHotkeyManager? hotkeyManager = null)
        {
            NotesViewModel = notesViewModel;
            CrmViewModel = crmViewModel;
            SettingsViewModel = settingsViewModel;
            ToolsViewModel = toolsViewModel;
            _hotkeyManager = hotkeyManager;
        }

        public void IncrementCommandCount() => CommandCount++;

        [RelayCommand]
        private void ExecuteCommand()
        {
            StatusText = "Выполняется команда...";
        }

        [RelayCommand]
        private void StopExecution()
        {
            StatusText = "Остановлено";
        }

        [RelayCommand]
        private void ResetCommandCount() => CommandCount = 0;

        [RelayCommand]
        private void ToggleAltBlocker()
        {
            IsAltBlockerEnabled = !IsAltBlockerEnabled;
            // Actual enabling/disabling is handled in the View via event
        }

        [RelayCommand]
        private void ToggleHooks()
        {
            // Logic for toggling hooks - handled in View
        }
    }
}
