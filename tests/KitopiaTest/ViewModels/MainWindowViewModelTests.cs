using System;
using System.Collections.Generic;
using System.Linq;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.ViewModel.Main;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KitopiaTest.ViewModels;

[TestClass]
public sealed class MainWindowViewModelTests
{
    private sealed class FakeNavigationService : INavigationService
    {
        public string? CurrentPageRoute { get; private set; } = "home";
        public bool CanGoBack => false;
        public event Action<string>? PageNavigated;

        public NavigationResult Navigate(string route, IReadOnlyDictionary<string, object?>? parameters = null)
        {
            CurrentPageRoute = route;
            PageNavigated?.Invoke(route);
            return NavigationResult.Ok();
        }

        public NavigationResult Open(string route, IReadOnlyDictionary<string, object?>? parameters = null) => NavigationResult.Ok();
        public NavigationResult GoBack() => NavigationResult.Ok();
    }

    [TestMethod]
    public void InitialState_SelectsHomeMenuItem()
    {
        var nav = new FakeNavigationService();
        var vm = new MainWindowViewModel(nav);

        var homeItem = vm.MenuItems.FirstOrDefault(x => x.Key == "home");
        Assert.IsNotNull(homeItem);
        Assert.IsTrue(homeItem.IsSelected);
        Assert.IsFalse(vm.SettingPage);

        var nonHomeItems = vm.MenuItems.Where(x => x.Key != "home");
        foreach (var item in nonHomeItems)
        {
            Assert.IsFalse(item.IsSelected, $"Item {item.Key} should not be selected initially.");
        }
    }

    [TestMethod]
    public void NavigateToPlugin_SelectsPluginAndUnselectsHome()
    {
        var nav = new FakeNavigationService();
        var vm = new MainWindowViewModel(nav);

        nav.Navigate("plugin");

        var pluginItem = vm.MenuItems.FirstOrDefault(x => x.Key == "plugin");
        var homeItem = vm.MenuItems.FirstOrDefault(x => x.Key == "home");

        Assert.IsNotNull(pluginItem);
        Assert.IsTrue(pluginItem.IsSelected);
        Assert.IsFalse(homeItem!.IsSelected);
        Assert.IsFalse(vm.SettingPage);
    }

    [TestMethod]
    public void NavigateToSettings_UnselectsAllMenuItems_AndSetsSettingPageTrue()
    {
        var nav = new FakeNavigationService();
        var vm = new MainWindowViewModel(nav);

        // First navigate to plugin
        nav.Navigate("plugin");
        var pluginItem = vm.MenuItems.FirstOrDefault(x => x.Key == "plugin");
        Assert.IsTrue(pluginItem!.IsSelected);

        // Now activate setting page
        vm.ActivateSettingPage();

        Assert.IsTrue(vm.SettingPage);
        Assert.AreEqual("settings", vm.Content);

        // Verify ALL menu items are deselected!
        foreach (var item in vm.MenuItems)
        {
            Assert.IsFalse(item.IsSelected, $"Menu item '{item.Key}' should not be selected when Settings is open.");
        }
    }

    [TestMethod]
    public void NavigateFromSettingsBackToPage_RestoresSelectionAndClearsSettingPage()
    {
        var nav = new FakeNavigationService();
        var vm = new MainWindowViewModel(nav);

        vm.ActivateSettingPage();
        Assert.IsTrue(vm.SettingPage);

        nav.Navigate("market");

        Assert.IsFalse(vm.SettingPage);
        var marketItem = vm.MenuItems.FirstOrDefault(x => x.Key == "market");
        Assert.IsNotNull(marketItem);
        Assert.IsTrue(marketItem.IsSelected);

        foreach (var item in vm.MenuItems.Where(x => x.Key != "market"))
        {
            Assert.IsFalse(item.IsSelected);
        }
    }
}
