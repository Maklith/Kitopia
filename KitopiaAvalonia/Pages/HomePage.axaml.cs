using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitopiaAvalonia.Controls;
using PluginCore;

namespace KitopiaAvalonia.Pages;

public partial class DownloadButtonViewModel : ObservableObject, IDownloadButtonViewModel
{
    [ObservableProperty] private bool _needDownload=true;
    [ObservableProperty] private bool _canDownload= true;
    public ICommand DownloadCommand { get; set; }
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _progress;

    public DownloadButtonViewModel()
    {
        DownloadCommand = new RelayCommand(Download);
    }

    private void Download()
    {
        IsDownloading = true;
        IsIndeterminate = true;
    }
}
public partial class HomePage : UserControl
{
    IDownloadButtonViewModel _downloadButtonViewModel= new DownloadButtonViewModel();
    public HomePage()
    {
        InitializeComponent();
        DownloadButton.DataContext = _downloadButtonViewModel;
    }
}