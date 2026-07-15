using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.CustomScenario.Services;
using Kitopia.Desktop.Features.CustomScenario.ViewModels;
using Kitopia.Desktop.Features.CustomScenario.ViewModels.TaskEditor;
using Kitopia.Desktop.Features.PluginHost.Services;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.MQTT;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.UI.UiControls.Plugin;
using Kitopia.Desktop.Features.ViewModel.Pages;
using Kitopia.Desktop.Features.ViewModel.Pages.plugin;
using Kitopia.Desktop.Platform.Linux;
using Kitopia.Desktop.Platform.Windows;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.InputProcessing;
using Kitopia.Desktop.Features.Search.Services;
using Kitopia.Desktop.Features.Search.ViewModels;
using SearchMath = Kitopia.Desktop.Features.Search.Utilities.Math;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class DesktopFeatureOwnershipTests
{
    [TestMethod]
    public void CustomScenarioEditorTypes_AreOwnedByDesktopFeatureAssembly()
    {
        var featureAssembly = typeof(TaskEditorViewModel).Assembly;

        Assert.AreEqual("Kitopia.Desktop.Features", featureAssembly.GetName().Name);
        Assert.AreSame(featureAssembly, typeof(PendingConnectionViewModel).Assembly);
        Assert.AreSame(featureAssembly, typeof(TaskNodeSearchViewModel).Assembly);
        Assert.AreSame(featureAssembly, typeof(CustomScenariosManagerPageViewModel).Assembly);
        Assert.AreSame(featureAssembly, typeof(ITaskEditorOpenService).Assembly);
        Assert.AreSame(
            typeof(CustomScenarioValueTuple).Assembly,
            typeof(Kitopia.Desktop.Features.CustomScenario.CustomScenario).Assembly);
    }

    [TestMethod]
    public void CoreAssembly_DoesNotContainMovedCustomScenarioEditorTypes()
    {
        var coreAssembly = typeof(Kitopia.Desktop.Features.CustomScenario.CustomScenario).Assembly;

        Assert.IsNull(coreAssembly.GetType("Core.ViewModel.TaskEditor.TaskEditorViewModel"));
        Assert.IsNull(coreAssembly.GetType("Core.ViewModel.TaskEditor.PendingConnectionViewModel"));
        Assert.IsNull(coreAssembly.GetType("Core.ViewModel.TaskEditor.TaskNodeSearchViewModel"));
        Assert.IsNull(coreAssembly.GetType("Core.ViewModel.Pages.customScenario.CustomScenariosManagerPageViewModel"));
        Assert.IsNull(coreAssembly.GetType("Core.Services.Interfaces.ITaskEditorOpenService"));
    }

    [TestMethod]
    public void SearchTypes_AreOwnedByDesktopFeatureAssembly()
    {
        var featureAssembly = typeof(SearchWindowViewModel).Assembly;

        Assert.AreEqual("Kitopia.Desktop.Features", featureAssembly.GetName().Name);
        Assert.AreSame(featureAssembly, typeof(MouseQuickWindowViewModel).Assembly);
        Assert.AreSame(featureAssembly, typeof(SearchIndex).Assembly);
        Assert.AreSame(featureAssembly, typeof(SearchItemTool).Assembly);
        Assert.AreSame(featureAssembly, typeof(IAppToolService).Assembly);
        Assert.AreSame(featureAssembly, typeof(IEverythingService).Assembly);
        Assert.AreSame(featureAssembly, typeof(MathIdentifier).Assembly);
        Assert.AreSame(featureAssembly, typeof(SearchMath).Assembly);
    }

    [TestMethod]
    public void CoreAssembly_DoesNotContainMovedSearchTypes()
    {
        var coreAssembly = typeof(Kitopia.Desktop.Features.CustomScenario.CustomScenario).Assembly;

        Assert.IsNull(coreAssembly.GetType("Core.SearchIndex"));
        Assert.IsNull(coreAssembly.GetType("Core.SearchEntry"));
        Assert.IsNull(coreAssembly.GetType("Core.ViewModel.Windows.SearchWindowViewModel"));
        Assert.IsNull(coreAssembly.GetType("Core.ViewModel.Windows.MouseQuickWindowViewModel"));
        Assert.IsNull(coreAssembly.GetType("Core.Services.SearchItemTool"));
        Assert.IsNull(coreAssembly.GetType("Core.Services.Interfaces.IAppToolService"));
        Assert.IsNull(coreAssembly.GetType("Core.Services.Interfaces.IEverythingService"));
        Assert.IsNull(coreAssembly.GetType("Core.UI.SearchWindow.InputData.MathIdentifier"));
        Assert.IsNull(coreAssembly.GetType("Core.Utils.Math"));
    }

    [TestMethod]
    public void PluginHostTypes_AreOwnedByDesktopFeatureAssembly()
    {
        var featureAssembly = typeof(PluginManager).Assembly;

        Assert.AreEqual("Kitopia.Desktop.Features", featureAssembly.GetName().Name);
        Assert.AreSame(featureAssembly, typeof(Plugin).Assembly);
        Assert.AreSame(featureAssembly, typeof(PluginNetworkService).Assembly);
        Assert.AreSame(featureAssembly, typeof(PluginDependencyService).Assembly);
        Assert.AreSame(featureAssembly, typeof(PluginManagerPageViewModel).Assembly);
        Assert.AreSame(featureAssembly, typeof(MarketPageViewModel).Assembly);
        Assert.AreSame(featureAssembly, typeof(PluginDetail).Assembly);
        Assert.AreSame(featureAssembly, typeof(IPluginToolService).Assembly);
        Assert.AreSame(featureAssembly, typeof(PluginMangerService).Assembly);
        Assert.AreSame(featureAssembly, typeof(MqttManager).Assembly);
    }

    [TestMethod]
    public void CoreAssembly_ContainsOnlyPluginTransitionContracts()
    {
        var coreAssembly = typeof(PluginLocalInfo).Assembly;

        Assert.AreSame(coreAssembly, typeof(PluginOverall).Assembly);
        Assert.AreSame(coreAssembly, typeof(ICustomScenarioPluginIntegration).Assembly);
        Assert.IsNull(coreAssembly.GetType("Core.Services.Plugin.PluginManager"));
        Assert.IsNull(coreAssembly.GetType("Core.Services.Plugin.Plugin"));
        Assert.IsNull(coreAssembly.GetType("Core.Services.Plugin.PluginNetworkService"));
        Assert.IsNull(coreAssembly.GetType("Core.Services.Plugin.PluginDependencyService"));
        Assert.IsNull(coreAssembly.GetType("Core.ViewModel.Pages.plugin.PluginManagerPageViewModel"));
        Assert.IsNull(coreAssembly.GetType("Core.ViewModel.Pages.MarketPageViewModel"));
        Assert.IsNull(coreAssembly.GetType("Core.UI.UiControls.Plugin.PluginDetail"));
        Assert.IsNull(coreAssembly.GetType("Core.Services.Interfaces.IPluginToolService"));
        Assert.IsNull(coreAssembly.GetType("Core.Services.MQTT.MqttManager"));
    }

    [TestMethod]
    public void PlatformTypes_AreOwnedByRenamedAssemblies()
    {
        Assert.AreEqual(
            "Kitopia.Desktop.Platform.Windows",
            typeof(ApplicationService).Assembly.GetName().Name);
        Assert.AreEqual(
            "Kitopia.Desktop.Platform.Linux",
            typeof(AppToolLinuxService).Assembly.GetName().Name);
    }
}
