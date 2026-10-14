using CommunityToolkit.Mvvm.ComponentModel;

namespace Kitopia.Desktop.Controls;

public partial class TextDialogViewModel : ObservableObject
{
    [ObservableProperty] private object _text;
}