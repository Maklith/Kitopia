using System;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Config;

namespace KitopiaEx;

[ConfigName("KitopiaEx主配置文件")]
public class Config : ConfigBase
{
    [ConfigFieldCategory("基本")] [ConfigField("自动启动", "可能被杀毒软件阻止", 0xE61C, ConfigFieldType.布尔)]
    public bool autoStart = true;

    public override void AfterLoad()
    {
        base.AfterLoad();
        var a = this;
        Instance.ConfigChanged += (sender, args) =>
        {
            switch (args.Name)
            {
                case "autoStart":
                {
                    Console.WriteLine(args.Value);
                    break;
                }
            }
        };
    }
}