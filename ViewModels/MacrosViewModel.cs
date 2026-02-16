using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iikoServiceHelper.Models;
using System.Threading.Tasks;

namespace iikoServiceHelper.ViewModels
{
    public partial class MacrosViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ObservableCollection<CustomCommand> _commands = new();

        [ObservableProperty]
        private CustomCommand? _selectedCommand;

        [RelayCommand]
        private async Task ExecuteSelectedCommandAsync()
        {
            if (SelectedCommand != null)
            {
                throw new NotImplementedException("Метод ExecuteSelectedCommandAsync находится в разработке. см. workflow: Этап 2");
            }
        }
    }
}
