using System;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace KitopiaAvalonia.Services;

internal sealed class ToastActionViewModel
{
    private static readonly IBrush PrimaryBackground = GetThemeBrush("SemiColorPrimary", Brushes.CornflowerBlue);
    private static readonly IBrush SecondaryBackground = GetThemeBrush("SemiColorFill1", Brushes.Transparent);
    private static readonly IBrush PrimaryBorder = GetThemeBrush("SemiColorPrimary", Brushes.CornflowerBlue);
    private static readonly IBrush SecondaryBorder = GetThemeBrush("SemiColorBorder", Brushes.Gray);
    private static readonly IBrush PrimaryForeground = Brushes.White;
    private static readonly IBrush SecondaryForeground = GetThemeBrush("SemiColorText0", Brushes.Black);

    public ToastActionViewModel(string text, bool isPrimary, Action execute)
    {
        Text = text;
        IsPrimary = isPrimary;
        Command = new RelayCommand(execute);
    }

    public string Text { get; }

    public bool IsPrimary { get; }

    public IRelayCommand Command { get; }

    public IBrush Background => IsPrimary ? PrimaryBackground : SecondaryBackground;

    public IBrush BorderBrush => IsPrimary ? PrimaryBorder : SecondaryBorder;

    public IBrush Foreground => IsPrimary ? PrimaryForeground : SecondaryForeground;

    private static IBrush GetThemeBrush(string key, IBrush fallback)
    {
        return Application.Current?.TryGetResource(key, null, out var brushObj) is true && brushObj is IBrush brush
            ? brush
            : fallback;
    }
}
