using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Interfaces;
using Serilog;

namespace Kitopia.Desktop.Services;

public sealed class NavigationService : INavigationService
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<NavigationService>();
    private readonly Stack<string> _history = new();

    public string? CurrentPageRoute { get; private set; } = "home";

    public bool CanGoBack => _history.Count > 0;

    public event Action<string>? PageNavigated;

    public NavigationResult Navigate(string route, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return NavigationResult.Fail("empty_route", "Route cannot be empty.");
        }

        var resolvedRoute = ResolveRoute(route, parameters);
        if (resolvedRoute is null)
        {
            Logger.Warning("Navigation route not found. Route={Route}", route);
            return NavigationResult.Fail("route_not_found", $"Route '{route}' is not registered.");
        }

        if (string.Equals(CurrentPageRoute, resolvedRoute, StringComparison.Ordinal))
        {
            return NavigationResult.Ok();
        }

        if (!string.IsNullOrWhiteSpace(CurrentPageRoute))
        {
            _history.Push(CurrentPageRoute);
        }

        CurrentPageRoute = resolvedRoute;
        PageNavigated?.Invoke(resolvedRoute);
        return NavigationResult.Ok();
    }

    public NavigationResult Open(string route, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        if (string.Equals(route, "window/main", StringComparison.Ordinal))
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow?.Show();
                desktop.MainWindow!.WindowState = Avalonia.Controls.WindowState.Normal;
                return NavigationResult.Ok();
            }

            return NavigationResult.Fail("window_unavailable", "Main window is unavailable.");
        }

        Logger.Warning("Window route not found. Route={Route}", route);
        return NavigationResult.Fail("route_not_found", $"Window route '{route}' is not registered.");
    }

    public NavigationResult GoBack()
    {
        if (_history.Count == 0)
        {
            return NavigationResult.Fail("history_empty", "No page in history.");
        }

        CurrentPageRoute = _history.Pop();
        PageNavigated?.Invoke(CurrentPageRoute);
        return NavigationResult.Ok();
    }

    private string? ResolveRoute(string route, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (string.Equals(route, "plugin/settings/select", StringComparison.Ordinal))
        {
            if (parameters is null || !parameters.TryGetValue("pluginInfo", out var value) || value is not string pluginInfo || string.IsNullOrWhiteSpace(pluginInfo))
            {
                return null;
            }

            return $"plugin/settings/select/{pluginInfo}";
        }

        if (string.Equals(route, "plugin/settings/detail", StringComparison.Ordinal))
        {
            if (parameters is null || !parameters.TryGetValue("configKey", out var value) || value is not string configKey || string.IsNullOrWhiteSpace(configKey))
            {
                return null;
            }

            return $"plugin/settings/detail/{configKey}";
        }

        if (route.StartsWith("plugin/settings/select/", StringComparison.Ordinal) ||
            route.StartsWith("plugin/settings/detail/", StringComparison.Ordinal))
        {
            return route;
        }

        if (route.StartsWith("settings/field/", StringComparison.Ordinal)
            && route.Length > "settings/field/".Length)
        {
            return route;
        }

        return route switch
        {
            "home" or "market" or "plugin" or "scenario" or "hotkey" or "onnx/model-manager" or "index/status" or "device/chat" or "settings" => route,
            _ => null
        };
    }
}
