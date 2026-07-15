#region

#endregion

using Avalonia.Controls;
using Avalonia.Interactivity;
using Kitopia.Desktop.Features.CustomScenario;

namespace Kitopia.Desktop.Pages;

public partial class CustomScenariosManagerPage : UserControl
{
    public CustomScenariosManagerPage()
    {
        InitializeComponent();
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup.RemoveMethodsByPluginName("Kitopia_KitopiaEx");
    }
}