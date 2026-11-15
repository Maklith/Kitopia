using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;

namespace PluginUiUnload;

public sealed class UiConfig : ConfigBase
{
    public int Value = 42;
}

public sealed class UiPlugin : IPlugin
{
    private Window? _window;

    public static IServiceProvider GetServiceProvider() => new ServiceCollection().AddSingleton<UiPlugin>().BuildServiceProvider();

    public void OnEnabled(IServiceProvider serviceProvider, Dictionary<string, IServiceProvider> dependencies)
    {
        _window = new Window { Content = new PluginControl() };
        _window.Show();
    }

    public void OnDisabled()
    {
        _window?.Close();
        _window = null;
    }
}

internal sealed class PluginControl : Control
{
    public static readonly StyledProperty<int> ValueProperty = AvaloniaProperty.Register<PluginControl, int>("Value");
    public static readonly AttachedProperty<int> AttachedValueProperty =
        AvaloniaProperty.RegisterAttached<PluginControl, Control, int>("AttachedValue");

    static PluginControl()
    {
        AffectsRender<PluginControl>(ValueProperty);
        MaxWidthProperty.OverrideDefaultValue<PluginControl>(400);
    }
}
