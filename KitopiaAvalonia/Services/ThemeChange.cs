#region

using System;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Core.SDKs.Services;
using Serilog;

#endregion

namespace Kitopia.Services;

public class ThemeChange : IThemeChange
{
    private static ILogger Log =   LogManager.Logger.ForContext<ThemeChange>();

    public void changeTo(string name)
    {
        Log.Debug(nameof(ThemeChange) + "的接口" + nameof(changeTo) + "被调用");

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
        Log.Debug(nameof(ThemeChange) + "的接口" + nameof(changeAnother) + "被调用");
        throw new NotImplementedException();
    }

    public void followSys(bool follow)
    {
        Log.Debug(nameof(ThemeChange) + "的接口" + nameof(follow) + "被调用");
    }

    public bool isDark()
    {
        Log.Debug(nameof(ThemeChange) + "的接口" + nameof(isDark) + "被调用");

        return Application.Current.RequestedThemeVariant == ThemeVariant.Dark;
    }
}