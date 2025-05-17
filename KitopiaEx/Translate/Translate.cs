using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using KitopiaEx.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using PluginCore;
using PluginCore.Attribute;

namespace KitopiaEx.Translate;

public class Translate
{
    [ScenarioMethod("翻译文字提取结果", $"{nameof(dResult)}=文字识别结果数据",$"{nameof(sourceTranslateLang)}=源语言",$"{nameof(translateLang)}=目标语言", "return=文字识别结果数据")]
    public IEnumerable<OcrResult> TranslateOcrResults(IEnumerable<OcrResult> dResult,[SelfInput]SourceTranslateLang sourceTranslateLang,[SelfInput]TargetTranslateLang translateLang, CancellationToken ct)
    {
        List<OcrResult> result = new List<OcrResult>();
        
        
        foreach (var item in dResult)
        {
                
            result.Add(item with { Text = TranslateApi.GetTranslation(item.Text,sourceTranslateLang,translateLang).Result });
        }

        return result;
    }
    
    [ScenarioMethod("翻译文字", $"{nameof(dResult)}=文字识别结果数据",$"{nameof(sourceTranslateLang)}=源语言",$"{nameof(translateLang)}=目标语言", "return=文字识别结果数据")]
    public string TranslateOcrResults(string dResult,[SelfInput]SourceTranslateLang sourceTranslateLang,[SelfInput]TargetTranslateLang translateLang, CancellationToken? ct=null)
    {
        return TranslateApi.GetTranslation(dResult,sourceTranslateLang,translateLang).Result;
            
        
    }
    
    
}