#region

using Avalonia.Controls;
using Kitopia.Desktop.Features.ViewModel.Pages;

#endregion

namespace Kitopia.Desktop.Pages;

public partial class OnnxModelManagerPage : UserControl
{
    public OnnxModelManagerPage()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is not OnnxModelManagerPageViewModel viewModel || viewModel.RefreshCommand.IsRunning) return;
            await viewModel.RefreshCommand.ExecuteAsync(null);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (DataContext is OnnxModelManagerPageViewModel viewModel) viewModel.RefreshCommand.Cancel();
        };
    }
}
