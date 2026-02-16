using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iikoServiceHelper.Models;
using iikoServiceHelper.Constants;

namespace iikoServiceHelper.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly AppSettings _settings;

        [ObservableProperty]
        private bool _isLightTheme;

        [ObservableProperty]
        private double _notesFontSize;

        [ObservableProperty]
        private bool _isAltBlockerEnabled;

        [ObservableProperty]
        private bool _usePasteModeForQuickReplies;

        public SettingsViewModel(AppSettings settings)
        {
            _settings = settings;
            _notesFontSize = settings.NotesFontSize;
            _isLightTheme = settings.IsLightTheme;
            _isAltBlockerEnabled = settings.IsAltBlockerEnabled;
            _usePasteModeForQuickReplies = settings.UsePasteModeForQuickReplies;
        }

        [RelayCommand]
        private void ZoomIn()
        {
            if (NotesFontSize < AppConstants.MaxFontSize)
            {
                NotesFontSize += AppConstants.FontSizeStep;
                _settings.NotesFontSize = NotesFontSize;
            }
        }

        [RelayCommand]
        private void ZoomOut()
        {
            if (NotesFontSize > AppConstants.MinFontSize)
            {
                NotesFontSize -= AppConstants.FontSizeStep;
                _settings.NotesFontSize = NotesFontSize;
            }
        }

        partial void OnIsLightThemeChanged(bool value) => _settings.IsLightTheme = value;
        partial void OnIsAltBlockerEnabledChanged(bool value) => _settings.IsAltBlockerEnabled = value;
        partial void OnUsePasteModeForQuickRepliesChanged(bool value) => _settings.UsePasteModeForQuickReplies = value;
        partial void OnNotesFontSizeChanged(double value) => _settings.NotesFontSize = value;
    }
}
