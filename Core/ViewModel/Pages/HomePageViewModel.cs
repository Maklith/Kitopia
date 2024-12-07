#region

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.SDKs.Services;

#endregion

namespace Core.ViewModel.Pages;

public partial class HomePageViewModel : ObservableRecipient
{
    [RelayCommand]
    public void Click()
    {
        ((INavigationPageService)ServiceManager.Services!.GetService(typeof(INavigationPageService)))
            .Navigate("设置");
    }
}