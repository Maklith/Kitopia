using System;
using KitopiaEx.Translate;
using PluginCore.Config;
using PluginCore.CustomScenario.Attribute.ConfigField;

namespace KitopiaEx;

[ConfigName("lang.kitopiaex.kitopiaex_settings")]
public class Config : ConfigBase
{
    public static Config INSTANCE;
    [ConfigFieldCategory("lang.kitopiaex.translate")]
    [ConfigField<TargetTranslateLang>("lang.kitopiaex.default_target_language", "lang.kitopiaex.choose_the_default_target_language_for_translation", 0xE61C)]
    public TargetTranslateLang DefaultLanguage = TargetTranslateLang.简体中文;

    [ConfigField("lang.kitopiaex.translation_search_prefix", "lang.kitopiaex.show_translation_when_the_search_starts_with_this_prefix", 0xf8cb, ConfigFieldType.字符串)]
    public string TranslatePreString = "f";
    [ConfigField("lang.kitopiaex.minimum_translation_text_length", "lang.kitopiaex.show_translation_when_the_search_text_exceeds_this_length", 0xf8cb, ConfigFieldType.整数, null, 1000, 5, 5)]
    public int TranslateMinCount =20;
    
    [ConfigFieldCategory("lang.kitopiaex.ocr")]
    [ConfigField("lang.kitopiaex.use_server_ocr_model", "lang.kitopiaex.use_the_server_ocr_model_for_improved_recognition", 0xf8cb, ConfigFieldType.布尔)]
    public bool UseServerOcrRecModel = false;
    public override void AfterLoad()
    {
        base.AfterLoad();
        INSTANCE = this;
        ConfigChanged += (sender, args) =>
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
