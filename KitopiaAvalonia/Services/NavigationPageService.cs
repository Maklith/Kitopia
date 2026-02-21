using System;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services.Interfaces;
using Core.ViewModel.Main;

namespace KitopiaAvalonia.Services;

public class NavigationPageService : INavigationPageService
{
    public bool Navigate(Type pageType)
    {
        throw new NotImplementedException();
    }

    public bool Navigate(string pageIdOrTargetTag)
    {
        WeakReferenceMessenger.Default.Send<PageChangeEventArgs>(new PageChangeEventArgs(pageIdOrTargetTag));

        return true;
    }
}