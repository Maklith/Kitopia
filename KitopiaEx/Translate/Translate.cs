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
    [Capture("翻译",0xf834)]
    public void TranslateImgCapture(ScreenCaptureResult dResult)
    {
        var service = KitopiaEx.ServiceProvider.GetService<Ocr.Ocr>()!;
        var ocrResults = service.OcrImg(dResult, CancellationToken.None);
        ocrResults = TranslateOcrResults(ocrResults, SourceTranslateLang.自动检测,TargetTranslateLang.简体中文,CancellationToken.None);
        service.OcrResultShow(dResult, ocrResults, CancellationToken.None);
    }
    
    [SearchMethod]
    public SearchViewItem? TranslateSearch(string search)
    {
        if (search.StartsWith(Config.INSTANCE.TranslatePreString) || search.Length > Config.INSTANCE.TranslateMinCount)
        {
            return new SearchViewItem()
            {
                ItemDisplayName = "翻译",
                FileType = FileType.自定义,
                IconSymbol = 0xf834,
                Action = (e,s) =>
                {
                    
                    if (s.StartsWith(Config.INSTANCE.TranslatePreString))
                    {
                        s = search.Substring(Config.INSTANCE.TranslatePreString.Length);
                    }
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var translateWindowViewModel = new TranslateWindowViewModel()
                        {
                            SourceText = s
                        };
                        var translateWindow = new TranslateWindow()
                        {
                            DataContext = translateWindowViewModel
                        };
                        translateWindow.Show();
                    });

                }
            };
        }

        return null;
    }
}