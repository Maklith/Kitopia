using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
    private string _typeLabel = string.Empty;
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
        Detach();

        if (DataContext is not SearchViewItem item) return;

        _typeLabel = item.FileType.ToString();
        if (ProcessUsageService.TryResolveProcessName(item, out var processName)) _processName = processName;

        _usage = ServiceManager.Services?.GetService<IProcessUsageService>();
        if (_usage is null)
        {
            Text = _typeLabel;
            return;
        }

        _usage.Updated += OnUsageUpdated;
        UpdateSubtitle();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // ListBox virtualization recycles containers; stop listening as soon as the row leaves the tree.
        Detach();
    }

    private void Detach()
    {
        if (_usage is not null) _usage.Updated -= OnUsageUpdated;
        _usage = null;
        _processName = string.Empty;
    }

    private void OnUsageUpdated() => UpdateSubtitle();

    private void UpdateSubtitle()
    {
        if (ConfigManger.Config?.showProcessUsageInSearch is not true
            || _processName.Length == 0
            || _usage is null
            || !_usage.TryGet(_processName, out var snapshot))
        {
            Text = _typeLabel;
            return;
        }

        var parts = new List<string>(3) { _typeLabel };
        if (ConfigManger.Config.showProcessCpuUsage)
            parts.Add(Lang.Format("lang.kitopia.process_cpu_usage",
                ProcessUsageService.FormatCpu(snapshot.CpuPercent)));
        if (ConfigManger.Config.showProcessMemoryUsage)
            parts.Add(Lang.Format("lang.kitopia.process_memory_usage",
                ProcessUsageService.FormatMemory(snapshot.WorkingSetBytes)));

        Text = string.Join(" · ", parts);
    }
}
