// Author: liaom
// SolutionName: Kitopia
// ProjectName: Kitopia.Desktop.Platform.Windows
// FileName:ControlPanelTools.cs
// Date: 2025/09/12 09:09
// FileEffect:

using System.Text;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Indexing;
using Microsoft.Win32;
using PluginCore;
using Vanara.PInvoke;

namespace Kitopia.Desktop.Platform.Windows.AppTools;

public class ControlPanelTools
{
    internal static void GetAll(IIndexService index)
    {
        var reg = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace");
        if (reg is null) return;
        foreach (var subKeyName in reg.GetSubKeyNames())
        {
            if (subKeyName == "DelegateFolders")
            {
                continue;
            }

            try
            {
                var subKey = reg.OpenSubKey(subKeyName);
                if (subKey is null) continue;
                var reg2 = Registry.ClassesRoot.OpenSubKey($"CLSID\\{subKeyName}");
                if (reg2 is null) continue;
                var localizedString = (string?)reg2.GetValue("LocalizedString");

                StringBuilder appContainer = new StringBuilder(100);
                ShlwApi.SHLoadIndirectString(localizedString ?? (string?)reg2.GetValue("") ?? subKeyName, appContainer,
                    (uint)appContainer.Capacity, IntPtr.Zero);
                var onlyKey = $"""shell:::{subKeyName}""";
                var entry = new SearchEntry
                {
                    DisplayName = appContainer.ToString(),
                    OnlyKey = onlyKey,
                    FileType = FileType.控制面板,
                    IconPath = Environment.ExpandEnvironmentVariables(
                        (string?)reg2.OpenSubKey("DefaultIcon")?.GetValue("") ?? "")
                };
                index.TryAdd(entry);
            }
            catch (Exception e)
            {
                LogManager.Logger.ForContext<ControlPanelTools>().Error(e, "获取控制面板项时出现错误");
            }
        }
    }
}
