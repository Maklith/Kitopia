using System;
using System.IO;
using KitopiaEx.Translate;
using PluginCore.Config;
using PluginCore.CustomScenario.Attribute.ConfigField;

namespace KitopiaEx;

[ConfigName("lang.kitopiaex.kitopiaex_settings")]
public class Config : ConfigBase
{
    public static Config INSTANCE;
    [ConfigFieldCategory("lang.kitopiaex.translate", 0xF834)]
    [ConfigField<TargetTranslateLang>("lang.kitopiaex.default_target_language", "lang.kitopiaex.choose_the_default_target_language_for_translation", 0xF45B)]
    public TargetTranslateLang DefaultLanguage = TargetTranslateLang.简体中文;

    [ConfigField("lang.kitopiaex.translation_search_prefix", "lang.kitopiaex.show_translation_when_the_search_starts_with_this_prefix", 0xF7E0, ConfigFieldType.字符串)]
    public string TranslatePreString = "f";
    [ConfigField("lang.kitopiaex.minimum_translation_text_length", "lang.kitopiaex.show_translation_when_the_search_text_exceeds_this_length", 0xF57E, ConfigFieldType.整数, null, 1000, 5, 5)]
    public int TranslateMinCount =20;
    
    [ConfigFieldCategory("lang.kitopiaex.ocr", 0xEA72)]
    [ConfigField("lang.kitopiaex.use_server_ocr_model", "lang.kitopiaex.use_the_server_ocr_model_for_improved_recognition", 0xEA72, ConfigFieldType.布尔)]
    public bool UseServerOcrRecModel = false;

    [ConfigFieldCategory("lang.kitopiaex.image", 0xF489)]
    [ConfigField("lang.kitopiaex.image_save_directory", "lang.kitopiaex.image_save_directory_description", 0xF419, ConfigFieldType.目录)]
    public string ImageSaveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public override void AfterLoad()
    {
        base.AfterLoad();
        if (string.IsNullOrWhiteSpace(ImageSaveDirectory))
            ImageSaveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
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
