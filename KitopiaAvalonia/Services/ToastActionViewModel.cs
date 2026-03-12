using System;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace KitopiaAvalonia.Services;

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

    public IBrush Background => IsPrimary
        ? GetThemeBrush("SemiColorPrimary", Brushes.CornflowerBlue)
        : GetThemeBrush("SemiColorFill1", Brushes.Transparent);

    public IBrush BorderBrush => IsPrimary
        ? GetThemeBrush("SemiColorPrimary", Brushes.CornflowerBlue)
        : GetThemeBrush("SemiColorBorder", Brushes.Gray);

    public IBrush Foreground => IsPrimary
        ? Brushes.White
        : GetThemeBrush("SemiColorText0", Brushes.Black);

    private static IBrush GetThemeBrush(string key, IBrush fallback)
    {
        return Application.Current?.TryGetResource(key, null, out var brushObj) is true && brushObj is IBrush brush
            ? brush
            : fallback;
    }
}
