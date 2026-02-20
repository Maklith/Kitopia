#region

using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Core.Services;
using Core.Services.Interfaces;
using Core.Utils;
using KitopiaAvalonia.Controls;
using Ursa.Controls;
using DialogWindow = KitopiaAvalonia.Windows.DialogWindow;

#endregion

namespace KitopiaAvalonia.Services;

public class ContentDialogService : IContentDialog
{
    public async Task ShowDialogAsync(object? contentPresenter, DialogContent dialogContent, bool canDismiss = false)
    {
        if (contentPresenter is null)
        {
            var tcs = new TaskCompletionSource();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var dialog = new DialogWindow(dialogContent);
                dialog.Show();
                dialog.Closed += (sender, args) => { tcs.SetResult(); };
                dialog.Show();
            });
            await tcs.Task;
            
            return;
        }

        await Task.Run(async () =>
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (contentPresenter is ContentPresenter control)
                    control.Content = new DialogOvercover(dialogContent, canDismiss);
            });
        });
    }

    public void ShowDialog(object? contentPresenter, DialogContent dialogContent)
    {
        if (contentPresenter is null)
        {
            var tcs = new TaskCompletionSource();

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var dialog = new DialogWindow(dialogContent);
                dialog.Show();
                var frame = new DispatcherFrame();
                dialog.Closed += (sender, args) => { tcs.SetResult(); };
                dialog.Show();
            }).GetAwaiter().GetResult();
            tcs.Task.GetAwaiter().GetResult();
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var button = DialogButton.None;
                if (dialogContent.CloseButtonText is null && dialogContent.PrimaryButtonText is null &&
                    dialogContent.SecondaryButtonText is null)
                    button = DialogButton.None;
                else if (dialogContent.PrimaryButtonText is null && dialogContent.SecondaryButtonText is null &&
                         dialogContent.CloseButtonText is not null)
                    button = DialogButton.None;
                else if (dialogContent.CloseButtonText is null && dialogContent.PrimaryButtonText is null &&
                         dialogContent.SecondaryButtonText is not null)
                    button = DialogButton.OKCancel;
                else if (dialogContent.CloseButtonText is null && dialogContent.PrimaryButtonText is not null &&
                         dialogContent.SecondaryButtonText is null)
                    button = DialogButton.OK;
                else if (dialogContent.CloseButtonText is null && dialogContent.PrimaryButtonText is not null &&
                         dialogContent.SecondaryButtonText is not null)
                    button = DialogButton.YesNo;
                else if (dialogContent.CloseButtonText is not null && dialogContent.PrimaryButtonText is not null &&
                         dialogContent.SecondaryButtonText is not null)
                    button = DialogButton.YesNoCancel;

                var dialog = new DefaultDialogWindow
                {
                    Title = dialogContent.Title,
                    Content = dialogContent.Content,
                    Buttons = button
                };
                dialog.Resources.Add("STRING_MENU_DIALOG_NO", dialogContent.CloseButtonText);
                var result = Dialog.ShowModal<TextDialog, TextDialogViewModel>(
                    new TextDialogViewModel { Text = dialogContent.Content }, (Window)contentPresenter,
                    new DialogOptions
                    {
                        Title = dialogContent.Title,
                        Button = button
                    }).GetAwaiter().GetResult();

                switch (result)
                {
                    case DialogResult.Yes:
                    {
                        dialogContent.PrimaryAction?.Invoke();
                        break;
                    }
                    case DialogResult.No:
                    {
                        dialogContent.SecondaryAction?.Invoke();
                        break;
                    }
                    case DialogResult.Cancel:
                    {
                        dialogContent.CloseAction?.Invoke();
                        break;
                    }
                }
            }).GetAwaiter().GetResult();
        }
    }
}