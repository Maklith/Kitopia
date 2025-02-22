using System;
using KitopiaEx.Translate;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Config;

namespace KitopiaEx;

[ConfigName("KitopiaEx主配置文件")]
public class Config : ConfigBase
{
    public static Config INSTANCE;
    [ConfigFieldCategory("翻译")] 
    [ConfigField<TargetTranslateLang>("默认目标语言", "修改翻译的默认目标语言", 0xE61C)]
    public TargetTranslateLang DefaultLanguage = TargetTranslateLang.简体中文;

    [ConfigField("搜索框翻译功能前缀", "如果搜索内容直接以该前缀开始,显示翻译功能", 0xf8cb, ConfigFieldType.字符串)]
    public string TranslatePreString = "f";
    [ConfigField("翻译功能最短显示字数", "如果搜索内容字数超过该值,显示翻译功能", 0xf8cb, ConfigFieldType.整数, null, 1000, 5, 5)]
    public int TranslateMinCount =20;
    
    [ConfigFieldCategory("文字识别")] 
    [ConfigField("使用服务器版Ocr模型", "使用服务器版Ocr模型,提供更好的识别效果", 0xf8cb, ConfigFieldType.布尔)]
    public bool UseServerOcrRecModel = false;
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