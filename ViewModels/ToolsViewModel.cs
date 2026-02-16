using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace iikoServiceHelper.ViewModels
{
    public partial class ToolsViewModel : ViewModelBase
    {
        [RelayCommand]
        private void OpenOrderCheck()
        {
            throw new NotImplementedException("Метод OpenOrderCheck находится в разработке. см. workflow: Этап 2");
        }

        [RelayCommand]
        private void OpenFtp()
        {
            throw new NotImplementedException("Метод OpenFtp находится в разработке. см. workflow: Этап 2");
        }

        [RelayCommand]
        private void CopyPosLink()
        {
            throw new NotImplementedException("Метод CopyPosLink находится в разработке. см. workflow: Этап 2");
        }
    }
}
