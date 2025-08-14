#region

#endregion

using Avalonia.Controls;
using Avalonia.Interactivity;
using Core.CustomScenario;

namespace KitopiaAvalonia.Pages;

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