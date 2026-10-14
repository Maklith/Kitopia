using System;
using CommunityToolkit.Mvvm.Input;

namespace Kitopia.Desktop.Services;

internal sealed class ToastActionViewModel
{
    public ToastActionViewModel(string text, bool isPrimary, Action execute)
    {
        Text = text;
        IsPrimary = isPrimary;
        Command = new RelayCommand(execute);
    }

    public string Text { get; }

    public bool IsPrimary { get; }

    public IRelayCommand Command { get; }
}
