#region

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Interfaces;
using Serilog;

#endregion

namespace Kitopia.Desktop.Services;

public class ThemeChange : IThemeChange, IDisposable
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<ThemeChange>();
    private IPlatformSettings? _accentPlatformSettings;
    private readonly List<(SolidColorBrush Brush, IDisposable Binding)> _accentBindings = [];

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

        Dispatcher.UIThread.Post(() =>
        {
            var application = Application.Current!;
            application.RequestedThemeVariant = follow ? ThemeVariant.Default : application.ActualThemeVariant;
        });
    }

    public bool isDark()
    {
        Logger.Debug(nameof(ThemeChange) + "的接口" + nameof(isDark) + "被调用");

        return Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
    }

    public void SetAccentColor(bool followSystem, string color)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_accentPlatformSettings is not null)
                _accentPlatformSettings.ColorValuesChanged -= OnSystemColorsChanged;
            _accentPlatformSettings = followSystem ? Application.Current!.PlatformSettings : null;
            if (_accentPlatformSettings is not null)
            {
                _accentPlatformSettings.ColorValuesChanged += OnSystemColorsChanged;
                UpdateAccentResources(_accentPlatformSettings.GetColorValues().AccentColor1);
            }
            else
            {
                if (!Color.TryParse(color, out var accent))
                {
                    Logger.Warning("主题色 {Color} 无效，使用默认主题色", color);
                    accent = Color.FromRgb(0, 100, 250);
                }
                UpdateAccentResources(accent);
            }
        });
    }

    private void OnSystemColorsChanged(object? sender, PlatformColorValues args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_accentPlatformSettings is not null)
                UpdateAccentResources(_accentPlatformSettings.GetColorValues().AccentColor1);
        });
    }

    private void UpdateAccentResources(Color color)
    {
        var application = Application.Current!;
        color = Color.FromRgb(color.R, color.G, color.B);
        var hsl = color.ToHsl();
        foreach (var variant in new[] { ThemeVariant.Default, ThemeVariant.Dark })
        {
            var dark = variant == ThemeVariant.Dark;
            var primary = dark
                ? new HslColor(1, hsl.H, hsl.S, Math.Max(hsl.L, 0.65)).ToRgb()
                : color;
            var primaryHsl = primary.ToHsl();
            var hover = new HslColor(1, primaryHsl.H, primaryHsl.S, Math.Max(0, primaryHsl.L - 0.06)).ToRgb();
            var active = new HslColor(1, primaryHsl.H, primaryHsl.S, Math.Max(0, primaryHsl.L - 0.12)).ToRgb();
            var resources = (IResourceDictionary)application.Resources.ThemeDictionaries[variant];
            resources["KitopiaAccentColor"] = primary;
            resources["KitopiaAccentHoverColor"] = hover;
            resources["KitopiaAccentActiveColor"] = active;
            if (!dark)
            {
                resources["KitopiaAccentLightColor"] = new HslColor(1, hsl.H, hsl.S, 0.96).ToRgb();
                resources["KitopiaAccentLightHoverColor"] = new HslColor(1, hsl.H, hsl.S, 0.90).ToRgb();
            }
        }

        if (_accentBindings.Count == 0)
        {
            // Semi control tokens use static aliases, so bind their shared brushes once.
            var semiTheme = application.Styles.OfType<Semi.Avalonia.SemiTheme>().Single();
            foreach (var variant in new[] { ThemeVariant.Default, ThemeVariant.Light, ThemeVariant.Dark })
            {
                var dark = variant == ThemeVariant.Dark;
                foreach (var (key, colorKey, opacity) in new[]
                {
                    ("SemiColorPrimary", "KitopiaAccentColor", 1.0),
                    ("SemiColorPrimaryPointerover", "KitopiaAccentHoverColor", 1.0),
                    ("SemiColorPrimaryActive", "KitopiaAccentActiveColor", 1.0),
                    ("SemiColorPrimaryLight", dark ? "KitopiaAccentColor" : "KitopiaAccentLightColor", dark ? 0.15 : 1.0),
                    ("SemiColorPrimaryLightPointerover", dark ? "KitopiaAccentColor" : "KitopiaAccentLightHoverColor", dark ? 0.22 : 1.0),
                    ("SemiColorPrimaryLightActive", dark ? "KitopiaAccentColor" : "KitopiaAccentLightHoverColor", dark ? 0.22 : 1.0),
                    ("SemiColorPrimaryDisabled", "KitopiaAccentColor", 0.4)
                })
                {
                    if (semiTheme.TryGetResource(key, variant, out var resource) && resource is SolidColorBrush brush)
                    {
                        brush.Opacity = opacity;
                        _accentBindings.Add((brush, brush.Bind(SolidColorBrush.ColorProperty,
                            application.Resources.GetResourceObservable(colorKey, variant))));
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_accentPlatformSettings is not null)
            _accentPlatformSettings.ColorValuesChanged -= OnSystemColorsChanged;
        _accentPlatformSettings = null;
        foreach (var (brush, binding) in _accentBindings)
        {
            var color = brush.Color;
            binding.Dispose();
            brush.Color = color;
        }
        _accentBindings.Clear();
    }
}
