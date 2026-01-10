using Core.Utils;

namespace Core.Services.Interfaces;

public interface IContentDialog
{
    Task ShowDialogAsync(object? contentPresenter, DialogContent dialogContent, bool canDismiss = false);

    void ShowDialog(object? contentPresenter, DialogContent dialogContent);
}