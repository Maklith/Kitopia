using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KitopiaEx.BatchRename;
using PluginCore;
using PluginCore.Localization;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace KitopiaEx.SearchWindow.InputDataAnalyzer;

public sealed class BatchRenameAnalyzer : IInputDataAnalyzer
{
    public InputDataAnalyzeTimeFlags AnalyzeTimeFlags => InputDataAnalyzeTimeFlags.InputEmpty |
        InputDataAnalyzeTimeFlags.WindowShow | InputDataAnalyzeTimeFlags.InputChanged;

    public IEnumerable<SearchViewItem> AnalyzeInputData(IEnumerable<InputData> inputDatas)
    {
        var paths = inputDatas.Where(input => input.InputType == InputType.文件)
            .Select(input => input.Data).OfType<string>()
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        if (paths.Length > 0)
            yield return new SearchViewItem
            {
                ItemDisplayName = Lang.Get("lang.kitopiaex.batch_rename.title"),
                FileType = FileType.自定义,
                IconSymbol = 0xf66a,
                IsVisible = true,
                ShowAsMiniApp = true,
                Action = (_, _) => _ = BatchRenameWindow.OpenFilesAsync(paths)
            };
    }
}
