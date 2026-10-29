using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Search.ProcessUsage;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Feature.Localization;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Controls;

/// <summary>
/// Subtitle of a search result row. Shows the result type and, while the application behind the
/// row is running, its live CPU and memory usage.
/// </summary>
public class SearchItemSubtitle : TextBlock
{
    private static readonly FontFeatureCollection TabularFigures =
        new([FontFeature.Parse("tnum")]);

    private IProcessUsageService? _usage;
    private string _processName = string.Empty;

    public SearchItemSubtitle()
    {
        FontFeatures = TabularFigures;
        IsHitTestVisible = false;
        Text = string.Empty;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _processName = DataContext is SearchViewItem item
            && ProcessUsageService.TryResolveProcessName(item, out var processName) ? processName : string.Empty;
        UpdateSubtitle();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Lang.Current.PropertyChanged += OnLanguageChanged;
        _usage = ServiceManager.Services?.GetService<IProcessUsageService>();
        if (_usage is not null) _usage.Updated += OnUsageUpdated;
        UpdateSubtitle();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // ListBox virtualization recycles containers; stop listening as soon as the row leaves the tree.
        Lang.Current.PropertyChanged -= OnLanguageChanged;
        if (_usage is not null) _usage.Updated -= OnUsageUpdated;
        _usage = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Lang.Keys)) return;
        if (Dispatcher.UIThread.CheckAccess()) UpdateSubtitle();
        else Dispatcher.UIThread.Post(UpdateSubtitle);
    }

    private void OnUsageUpdated() => UpdateSubtitle();

    private void UpdateSubtitle()
    {
        var typeLabel = DataContext is SearchViewItem item ? Lang.Get(item.FileType) : string.Empty;
        if (ConfigManger.Config?.showProcessUsageInSearch is not true
            || _processName.Length == 0
            || _usage is null
            || !_usage.TryGet(_processName, out var snapshot))
        {
            Text = typeLabel;
            return;
        }

        var parts = new List<string>(3) { typeLabel };
        if (ConfigManger.Config.showProcessCpuUsage)
            parts.Add(Lang.Format("lang.kitopia.process_cpu_usage",
                ProcessUsageService.FormatCpu(snapshot.CpuPercent)));
        if (ConfigManger.Config.showProcessMemoryUsage)
            parts.Add(Lang.Format("lang.kitopia.process_memory_usage",
                ProcessUsageService.FormatMemory(snapshot.WorkingSetBytes)));

        Text = string.Join(" · ", parts);
    }
}
