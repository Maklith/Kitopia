using System;
using System.Collections.Generic;
using Core.Services.Interfaces;

namespace Kitopia.Mobile.Services;

public sealed class MobileNavigationService : INavigationService
{
    public string? CurrentPageRoute => "device/chat";
    public bool CanGoBack => false;
    public event Action<string>? PageNavigated { add { } remove { } }

    public NavigationResult Navigate(string route, IReadOnlyDictionary<string, object?>? parameters = null)
        => NavigationResult.Ok();

    public NavigationResult Open(string route, IReadOnlyDictionary<string, object?>? parameters = null)
        => NavigationResult.Ok();

    public NavigationResult GoBack() => NavigationResult.Fail("not_supported");
}
