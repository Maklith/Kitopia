#region

using System;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Core.Services;
using Serilog;

#endregion

namespace KitopiaAvalonia.Services;

public class ThemeChange : IThemeChange
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<ThemeChange>();

    public void changeTo(string name)
    {
        Logger.Debug(nameof(ThemeChange) + "的接口" + nameof(changeTo) + "被调用");

        Dispatcher.UIThread.Post(() =>
        {
            switch (name)
            {
                case "theme_light":
                    Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                    break;

                default:
                    Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                    break;
            }
        });
    }

    public void changeAnother()
    {
        Logger.Debug(nameof(ThemeChange) + "的接口" + nameof(changeAnother) + "被调用");
        throw new NotImplementedException();
    }

    public void followSys(bool follow)
    {
        Logger.Debug(nameof(ThemeChange) + "的接口" + nameof(follow) + "被调用");
    }

    public bool isDark()
    {
        Logger.Debug(nameof(ThemeChange) + "的接口" + nameof(isDark) + "被调用");

        return Application.Current.RequestedThemeVariant == ThemeVariant.Dark;
    }
}