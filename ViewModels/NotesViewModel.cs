using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using iikoServiceHelper.Services;
using iikoServiceHelper.Utils;
using iikoServiceHelper.Constants;

namespace iikoServiceHelper.ViewModels
{
    public partial class NotesViewModel : ViewModelBase
    {
        private readonly FileService _fileService;
        private readonly DebounceDispatcher _notesDebouncer = new();

        [ObservableProperty]
        private string _content = string.Empty;

        public NotesViewModel(FileService fileService)
        {
            _fileService = fileService;
            LoadNotes();
        }

        private async void LoadNotes()
        {
            try
            {
                Content = await _fileService.LoadNotesAsync();
            }
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"Failed to load notes: {ex.Message}");
            }
        }

        partial void OnContentChanged(string value)
        {
            _notesDebouncer.Debounce(DelayConstants.AutoSaveDebounce, async () => await SaveNotesAsync());
        }

        public async Task SaveNotesAsync()
        {
            try
            {
                await _fileService.SaveNotesAsync(Content);
            }
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"Failed to save notes: {ex.Message}");
            }
        }
    }
}
