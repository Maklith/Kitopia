using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using KitopiaEx.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using PluginCore;
using PluginCore.Attribute;

namespace KitopiaEx.Translate;

public enum TranslateLang
{
    简体中文,
    繁體中文,
    English,
    日本語,
}
public class Translate
{
    public string TranslateLangToName(TranslateLang lang)
    {
        return lang switch
        {
            TranslateLang.简体中文 => "zh-Hans",
            TranslateLang.繁體中文 => "zh-Hant",
            TranslateLang.English => "en",
            TranslateLang.日本語 => "ja",
            _ => throw new ArgumentOutOfRangeException(nameof(lang), lang, null)
        };
    }
    [ScenarioMethod("翻译文字提取结果", $"{nameof(dResult)}=文字识别结果数据",$"{nameof(translateLang)}=目标语言", "return=文字识别结果数据")]
    public IEnumerable<OcrResult> TranslateOcrResults(IEnumerable<OcrResult> dResult,[SelfInput]TranslateLang translateLang, CancellationToken ct)
    {
        List<OcrResult> result = new List<OcrResult>();
        HttpClient httpClient = new HttpClient();
        try
        {
            
            httpClient.DefaultRequestHeaders.Add("User-Agent","KitopiaEx/1.1.0");
            var s = httpClient.GetStringAsync("https://edge.microsoft.com/translate/auth").Result;
            httpClient.Dispose();
            httpClient = new HttpClient();
            httpClient.BaseAddress =
                new Uri(
                 $"https://api-edge.cognitive.microsofttranslator.com/translate?from=&to={TranslateLangToName(translateLang)}&api-version=3.0&includeSentenceLength=true");
            httpClient.DefaultRequestHeaders.Add("authorization",$"Bearer {s}");
            httpClient.DefaultRequestHeaders.Add("User-Agent","KitopiaEx/1.1.0");
            foreach (var item in dResult)
            {
                var content = new StringContent($$"""[{"Text":"{{item.Text}}"}]""", Encoding.UTF8, "application/json");

                var text = httpClient.PostAsync("",content,ct).Result.Content.ReadAsStringAsync(ct).Result;


                try
                {
                    using var doc = JsonDocument.Parse(text);
                    var text2 = doc.RootElement[0]
                        .GetProperty("translations")[0]
                        .GetProperty("text")
                        .GetString();
                    result.Add(item with { Text = text2 });
                }
                catch (Exception e)
                {
                    result.Add(item);
                }
            
            }
            httpClient.Dispose();
            return result;
        }
        catch (Exception e)
        {
            return dResult;
        }finally
        {
            httpClient.Dispose();
        }
    }
    
    [ScenarioMethod("翻译文字", $"{nameof(dResult)}=文字识别结果数据",$"{nameof(translateLang)}=目标语言", "return=文字识别结果数据")]
    public string TranslateOcrResults(string dResult,[SelfInput]TranslateLang translateLang, CancellationToken ct)
    {
        List<OcrResult> result = new List<OcrResult>();
        try
        {
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent","KitopiaEx/1.1.0");
            var s = httpClient.GetStringAsync("https://edge.microsoft.com/translate/auth").Result;
            httpClient.Dispose();
            httpClient = new HttpClient();
            httpClient.BaseAddress =
                new Uri(
                    $"https://api-edge.cognitive.microsofttranslator.com/translate?from=&to={TranslateLangToName(translateLang)}&api-version=3.0&includeSentenceLength=true");
            httpClient.DefaultRequestHeaders.Add("authorization",$"Bearer {s}");
            httpClient.DefaultRequestHeaders.Add("User-Agent","KitopiaEx/1.1.0");
            
            var content = new StringContent($$"""[{"Text":"{{dResult}}"}]""", Encoding.UTF8, "application/json");

            var text = httpClient.PostAsync("",content,ct).Result.Content.ReadAsStringAsync(ct).Result;


            try
            {
                using var doc = JsonDocument.Parse(text);
                var text2 = doc.RootElement[0]
                    .GetProperty("translations")[0]
                    .GetProperty("text")
                    .GetString();
                return text2;
            }
            catch (Exception e)
            {
                return dResult;
            }
            finally
            {
                httpClient.Dispose();
            }
            
            
            return dResult;
        }
        catch (Exception e)
        {
            return dResult;
        }
    }
    [Capture("翻译",0xf834)]
    public void TranslateImgCapture(ScreenCaptureResult dResult)
    {
        var service = KitopiaEx.ServiceProvider.GetService<Ocr.Ocr>()!;
        var ocrResults = service.OcrImg(dResult, CancellationToken.None);
        ocrResults = TranslateOcrResults(ocrResults, TranslateLang.简体中文,CancellationToken.None);
        service.OcrResultShow(dResult, ocrResults, CancellationToken.None);
    }
}