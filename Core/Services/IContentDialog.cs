namespace Core.SDKs.Services;

public interface IContentDialog
{
    Task ShowDialogAsync(object? contentPresenter, DialogContent dialogContent, bool canDismiss = false);

    void ShowDialog(object? contentPresenter, DialogContent dialogContent);
}