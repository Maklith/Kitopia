using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kitopia.Desktop.Services;

internal sealed class SuppressedNotificationItemViewModel : ObservableObject
{
    public SuppressedNotificationItemViewModel(string header, string text, DateTimeOffset createdAt, Action openAction)
    {
        Header = header;
        Text = text;
        CreatedAt = createdAt;
        OpenCommand = new RelayCommand(openAction);
    }

    public string Header { get; }
    public string Text { get; }
    public DateTimeOffset CreatedAt { get; }
    public string CreatedAtText => CreatedAt.ToLocalTime().ToString("HH:mm:ss");
    public IRelayCommand OpenCommand { get; }
}
