using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;

namespace KitopiaAvalonia.Controls;

public partial class DownloadButton : TemplatedControl
{
    public static readonly AvaloniaProperty<bool> NeedDownloadProperty =
        AvaloniaProperty.Register<DownloadButton, bool>(nameof(NeedDownload), true);
    public static readonly AvaloniaProperty<bool> CanDownloadProperty =
        AvaloniaProperty.Register<DownloadButton, bool>(nameof(CanDownload), true);
    public static readonly AvaloniaProperty<ICommand> DownloadCommandProperty =
        AvaloniaProperty.Register<DownloadButton, ICommand>(nameof(DownloadCommand));
    public static readonly AvaloniaProperty<ICommand> CancelCommandProperty =
        AvaloniaProperty.Register<DownloadButton, ICommand>(nameof(CancelCommand));
    public static readonly AvaloniaProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<DownloadButton, bool>(nameof(IsIndeterminate), true);
    public static readonly AvaloniaProperty<bool> IsDownloadingProperty =
        AvaloniaProperty.Register<DownloadButton, bool>(nameof(IsDownloading), true);
    public static readonly AvaloniaProperty<double> ProgressProperty =
        AvaloniaProperty.Register<DownloadButton, double>(nameof(Progress), 0.0);
    
    
    public bool NeedDownload
    {
        get => (bool)GetValue(NeedDownloadProperty);
        set => SetValue(NeedDownloadProperty, value);
    }
    public bool CanDownload
    {
        get => (bool)GetValue(CanDownloadProperty);
        set => SetValue(CanDownloadProperty, value);
    }
    public ICommand DownloadCommand
    {
        get => (ICommand)GetValue(DownloadCommandProperty);
        set => SetValue(DownloadCommandProperty, value);
    }
    public ICommand CancelCommand
    {
        get => (ICommand)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }
    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }
    public bool IsDownloading
    {
        get => (bool)GetValue(IsDownloadingProperty);
        set => SetValue(IsDownloadingProperty, value);
    }
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }
    
}