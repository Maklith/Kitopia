using System.IO;
using Avalonia;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Core.Services;
using Microsoft.Win32;
using PluginCore;
using Serilog;
using Vanara.PInvoke;

namespace Core.Window;

public class ApplicationService : IApplicationService
{
    
    private static ILogger Log = LogManager.Logger.ForContext<ApplicationService>();
    public void Init()
    {
        InitUrlProtocol();
    }

    public void Restart()
    {
        Shell32.ShellExecute(IntPtr.Zero, "open", AppDomain.CurrentDomain.FriendlyName + ".exe", "",
            AppDomain.CurrentDomain.BaseDirectory,
            ShowWindowCommand.SW_NORMAL);
        Environment.Exit(0);
    }

    public void Stop()
    {
        ConfigManger.Save();
        Environment.Exit(0);
    }

    public void InitUrlProtocol()
    {
        var protocolName = "kitopiaurl";

        try
        {
            // 创建或打开HKEY_CLASSES_ROOT下的URL Protocol键
            using (var key = Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + protocolName))
            {
                // 设置默认值为描述你的协议的字符串
                key.SetValue(null, "URL: Kitopia");
                key.SetValue("URL Protocol", "");

                // 创建一个子键用于处理打开协议的操作
                using (var commandKey = key.CreateSubKey("shell\\open\\command"))
                {
                    // 设置默认值为你的应用程序可执行文件的路径，包括 "%1" 用于参数
                    var appPath = $"{AppDomain.CurrentDomain.BaseDirectory}KitopiaAvalonia.exe \"%1\"";
                    commandKey.SetValue(null, appPath);
                    commandKey.Flush();
                }

                key.Flush();
            }

            Log.Debug("定义URL Protocol成功");
        }
        catch (Exception ex)
        {
            Log.Error("定义URL Protocol失败", ex);
        }
    }

    public bool ChangeAutoStart(bool autoStart)
    {
        try
        {
            if (autoStart)
            {
                var strName = AppDomain.CurrentDomain.BaseDirectory + "KitopiaAvalonia.exe"; //获取要自动运行的应用程序名
                if (!File.Exists(strName)) //判断要自动运行的应用程序文件是否存在
                    return false;

                var registry =
                    Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                        true); //检索指定的子项
                if (registry == null) //若指定的子项不存在
                {
                    registry = Registry.CurrentUser.CreateSubKey(
                        "Software\\Microsoft\\Windows\\CurrentVersion\\Run"); //则创建指定的子项
                }
                else
                {
                    if (Equals(registry.GetValue("Kitopia"), $"\"{strName}\""))
                    {
                        Log.Information("开机自启配置已存在");
                        return true;
                    }
                }

                Log.Information("用户确认启用开机自启");
                try
                {
                    registry.SetValue("Kitopia", $"\"{strName}\""); //设置该子项的新的“键值对”
                    ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))).Show("开机自启",
                        "开机自启设置成功");
                }
                catch (Exception exception)
                {
                    Log.Error("开机自启设置失败");
                    Log.Error(exception.StackTrace);
                    ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))).Show("开机自启",
                        "开机自启设置失败");
                    return false;
                }
            }
            else
            {
                try
                {
                    var registry =
                        Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                            true); //检索指定的子项
                    registry?.DeleteValue("Kitopia");
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e,"开机自启设置失败");
            ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))).Show("开机自启",
                "开机自启设置失败");
            return false;
        }

        return true;
    }
}