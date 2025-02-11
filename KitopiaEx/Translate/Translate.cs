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

public class Translate
{
    [ScenarioMethod("翻译文字提取结果", $"{nameof(dResult)}=文字识别结果数据", "return=文字识别结果数据")]
    public IEnumerable<OcrResult> TranslateOcrResults(IEnumerable<OcrResult> dResult, CancellationToken ct)
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
                    "https://api-edge.cognitive.microsofttranslator.com/translate?from=&to=zh-Hans&api-version=3.0&includeSentenceLength=true");
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
            return result;
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
        ocrResults = TranslateOcrResults(ocrResults, CancellationToken.None);
        service.OcrResultShow(dResult, ocrResults, CancellationToken.None);
    }
}