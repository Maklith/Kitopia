using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Kitopia.Desktop.Features.CustomScenario.Services;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.ViewModel.Pages;

namespace Kitopia.Desktop.Pages;

public partial class MarketPage : UserControl
{
    public MarketPage()
    {
        InitializeComponent();
    }

    private void PluginCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: PluginInfoUiHelper plugin } ||
            e.Source is not Visual source || source.FindAncestorOfType<Button>() is not null)
            return;

        if (DataContext is MarketPageViewModel viewModel && viewModel.ShowPluginDetailCommand.CanExecute(plugin))
        {
            viewModel.ShowPluginDetailCommand.Execute(plugin);
            e.Handled = true;
        }
    }

    private void ScenarioCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: ScenarioMarketItem scenario } ||
            e.Source is not Visual source || source.FindAncestorOfType<Button>() is not null)
            return;

        if (DataContext is MarketPageViewModel viewModel && viewModel.ShowScenarioDetailCommand.CanExecute(scenario))
        {
            viewModel.ShowScenarioDetailCommand.Execute(scenario);
            e.Handled = true;
        }
    }
}
