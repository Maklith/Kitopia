using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.ViewModel.Pages;
using Kitopia.Desktop.Features.ViewModel.Pages.plugin;
using Kitopia.Desktop.Features.PluginHost.Services;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Features.PluginHost;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopPluginHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPluginManger, PluginMangerService>();
        services.AddTransient<IPluginToolService, PluginToolService>();
        services.AddSingleton<ICustomScenarioPluginIntegration, CustomScenarioPluginIntegration>();
        services.AddSingleton<IStartupMessageBroker, MqttStartupMessageBroker>();
        services.AddTransient<PluginManagerPageViewModel>(provider =>
            new PluginManagerPageViewModel { IsActive = true });
        services.AddSingleton<PluginSettingViewModel>(provider =>
            new PluginSettingViewModel { IsActive = true });
        services.AddTransient<MarketPageViewModel>();

        return services;
    }
}
