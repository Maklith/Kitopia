// Author: liaom
// SolutionName: Kitopia
// ProjectName: Core.Window
// FileName:ControlPanelTools.cs
// Date: 2025/09/12 09:09
// FileEffect:

using System.Collections.Concurrent;
using System.Text;
using Core.Services;
using Microsoft.Win32;
using PluginCore;
using Vanara.PInvoke;

namespace Core.Window;

public class ControlPanelTools
{
    internal static void GetAll(ConcurrentDictionary<string, SearchViewItem> items)
    {
        //HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\
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
                //计算机\HKEY_CLASSES_ROOT\CLSID\{025A5937-A6BE-4686-A844-36FE4BEC8B6D}
                var reg2 = Registry.ClassesRoot.OpenSubKey($"CLSID\\{subKeyName}");
                if (reg2 is null) continue;
                var localizedString = (string?)reg2.GetValue("LocalizedString");
                //获取默认

                StringBuilder appContainer = new StringBuilder(100);
                ShlwApi.SHLoadIndirectString(localizedString ?? (string?)reg2.GetValue("") ?? subKeyName, appContainer,
                    (uint)appContainer.Capacity, IntPtr.Zero);
                var onlyKey = $"""shell:::{subKeyName}""";
                var item = new SearchViewItem
                {
                    ItemDisplayName = appContainer.ToString(),
                    OnlyKey = onlyKey,
                    FileType = FileType.控制面板,
                    PinyinItem = AppTools.NameSolver(appContainer.ToString()),
                    IconPath = Environment.ExpandEnvironmentVariables(
                        (string?)reg2.OpenSubKey("DefaultIcon")?.GetValue("") ?? ""),
                    IsVisible = true
                };
                items.TryAdd(onlyKey, item);
            }
            catch (Exception e)
            {
                LogManager.Logger.ForContext<ControlPanelTools>().Error(e, "获取控制面板项时出现错误");
            }
        }
    }
}