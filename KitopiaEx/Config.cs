using System;
using KitopiaEx.Translate;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Config;

namespace KitopiaEx;

[ConfigName("KitopiaEx主配置文件")]
public class Config : ConfigBase
{
    public Config INSTANCE;
    [ConfigFieldCategory("翻译")] 
    [ConfigField<TranslateLang>("默认目标语言", "修改翻译的默认目标语言", 0xE61C)]
    public TranslateLang DefaultLanguage = TranslateLang.简体中文;

    public override void AfterLoad()
    {
        base.AfterLoad();
        var a = this;
        INSTANCE = (Config)Instance;
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