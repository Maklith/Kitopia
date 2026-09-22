using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using PluginCore.CustomScenario.Attribute.ConfigField;

namespace PluginLifecycle;

public sealed class FixtureConfig : ConfigBase
{
    public string FailureStage = "";
    public string EventsFile = "";
    [ConfigField("Fixture", "", fieldType: ConfigFieldType.快捷键)]
    public HotKeyModel Hotkey = new() { Type = HotKeyType.Mouse, MouseButton = 1, IsEnabled = true };
    [System.Text.Json.Serialization.JsonIgnore]
    public Action<HotKeyModel> HotkeyAction => _ => Record("hotkey");
    public void Record(string value) => File.AppendAllLines(EventsFile, [value]);
    public override void AfterLoad()
    {
        if (FailureStage == "config") throw new InvalidOperationException("config failure");
    }
}

public sealed class LifecyclePlugin(FixtureConfig config) : IPlugin
{
    public static IServiceProvider GetServiceProvider()
    {
        var config = Kitopia.ServiceProvider.GetRequiredService<IConfigProvider>().Get<FixtureConfig>();
        if (config.FailureStage == "factory") throw new InvalidOperationException("factory failure");
        return new ServiceCollection().AddSingleton(config).AddSingleton<LifecyclePlugin>()
            .AddSingleton<AsyncResource>().BuildServiceProvider();
    }

    public void OnEnabled(IServiceProvider provider, Dictionary<string, IServiceProvider> dependencies)
    {
        provider.GetRequiredService<AsyncResource>();
        config.Record("enabled");
        if (config.FailureStage == "enable") throw new InvalidOperationException("enable failure");
    }

    public void OnDisabled() => throw new InvalidOperationException("The host must await OnDisabledAsync.");
    public async ValueTask OnDisabledAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        config.Record("stopped");
        if (config.FailureStage == "disable") throw new InvalidOperationException("disable failure");
    }
}

public sealed class AsyncResource(FixtureConfig config) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        config.Record("disposed");
    }
}
