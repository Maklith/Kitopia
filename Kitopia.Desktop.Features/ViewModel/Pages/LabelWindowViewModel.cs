using CommunityToolkit.Mvvm.ComponentModel;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public partial class LabelWindowViewModel : ObservableObject
{
    [ObservableProperty] private int _fontSize = 16;
}