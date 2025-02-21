using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace KitopiaEx.Translate;

public static class TranslateApi
{
    static HttpClient httpClient = new HttpClient();
    private static string token;
    
    private static void Auth()
    {
        httpClient.DefaultRequestHeaders.Remove("Authorization");
        httpClient.DefaultRequestHeaders.Add("Authorization",$"Bearer {token}");
        httpClient.DefaultRequestHeaders.Add("User-Agent","KitopiaEx/1.1.0");
        token = httpClient.GetStringAsync("https://edge.microsoft.com/translate/auth").Result;
    }
    public static string TargetTranslateLangToName(TargetTranslateLang lang)
    {
        return lang switch
        {
            TargetTranslateLang.简体中文 => "zh-Hans",
            TargetTranslateLang.繁體中文 => "zh-Hant",
            TargetTranslateLang.English => "en",
            TargetTranslateLang.日本語 => "ja",
            _ => throw new ArgumentOutOfRangeException(nameof(lang), lang, null)
        };
    }
    public static string SourceTranslateLangToName(SourceTranslateLang lang)
    {
        return lang switch
        {
            SourceTranslateLang.自动检测=>"",
            SourceTranslateLang.简体中文 => "zh-Hans",
            SourceTranslateLang.繁體中文 => "zh-Hant",
            SourceTranslateLang.English => "en",
            SourceTranslateLang.日本語 => "ja",
            _ => throw new ArgumentOutOfRangeException(nameof(lang), lang, null)
        };
    }
    
    private static bool CheckIsSuccess(string text)
    {
        return text.Contains("translations");
    }
    public static async Task<string> GetTranslation(string text, SourceTranslateLang from, TargetTranslateLang to)
    {
        try
        {
            var jsonArray = new JsonArray();
            jsonArray.Add(new JsonObject
            {
                ["Text"] = text
            });
            var content = new StringContent(jsonArray.ToJsonString(), Encoding.UTF8, "application/json");
            
            var r=await httpClient.PostAsync(new Uri($"https://api-edge.cognitive.microsofttranslator.com/translate?from={SourceTranslateLangToName(from)}&to={TargetTranslateLangToName(to)}&api-version=3.0&includeSentenceLength=true"),content).Result.Content.ReadAsStringAsync();
            if (CheckIsSuccess(r))
            {
                using var doc = JsonDocument.Parse(r);
                var text2 = doc.RootElement[0]
                    .GetProperty("translations")[0]
                    .GetProperty("text")
                    .GetString();
                return text2;
            }
            else
            {
                Auth();
                return await GetTranslation(text, from, to);
            }
        }
        catch (Exception e)
        {
            return e.Message;
        }
        
    }
}