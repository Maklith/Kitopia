using System;
using System.Diagnostics;
using System.Reactive.Concurrency;
using Core.Services;
using Avalonia.Controls.Notifications;
using PluginCore;
using ReactiveUI;
using Serilog;

namespace KitopiaAvalonia.Services;

public class MyCoolObservableExceptionHandler : IObserver<Exception>
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<MyCoolObservableExceptionHandler>();

    public void OnNext(Exception value)
    {
        if (Debugger.IsAttached) Debugger.Break();
        Logger.Error(value, "");
        if (ServiceManager.Services.GetService(typeof(IToastService)) is IToastService toastService)
        {
            _ = toastService.Show("错误", value.ToString(), NotificationType.Error);
        }
        RxSchedulers.MainThreadScheduler.Schedule(() => { throw value; });
    }

    public void OnError(Exception error)
    {
        if (Debugger.IsAttached) Debugger.Break();
        Logger.Error(error, "");
        if (ServiceManager.Services.GetService(typeof(IToastService)) is IToastService toastService)
        {
            _ = toastService.Show("错误", error.ToString(), NotificationType.Error);
        }
        RxSchedulers.MainThreadScheduler.Schedule(() => { throw error; });
    }

    public void OnCompleted()
    {
        if (Debugger.IsAttached) Debugger.Break();
        RxSchedulers.MainThreadScheduler.Schedule(() => { throw new NotImplementedException(); });
    }
}
