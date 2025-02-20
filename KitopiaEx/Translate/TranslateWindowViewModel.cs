using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KitopiaEx.Translate;

public partial class TranslateWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _sourceText = string.Empty;
    [ObservableProperty]
    private SourceTranslateLang _sourceTranslateLang  =SourceTranslateLang.自动检测 ;
    [ObservableProperty]
    private string _targetText = string.Empty;
    [ObservableProperty]
    private TargetTranslateLang _targetTranslateLang  =TargetTranslateLang.简体中文 ;
    HttpClient httpClient = new HttpClient();
    string token=String.Empty;
    public TranslateWindowViewModel()
    {
        httpClient.DefaultRequestHeaders.Add("User-Agent","KitopiaEx/1.1.0");
        token = httpClient.GetStringAsync("https://edge.microsoft.com/translate/auth").Result;
        httpClient.Dispose();
        httpClient = new HttpClient();
        httpClient.BaseAddress =
            new Uri(
                $"https://api-edge.cognitive.microsofttranslator.com/translate?from=&to={Translate.TranslateLangToName(TargetTranslateLang)}&api-version=3.0&includeSentenceLength=true");
        httpClient.DefaultRequestHeaders.Add("authorization",$"Bearer {token}");
        httpClient.DefaultRequestHeaders.Add("User-Agent","KitopiaEx/1.1.0");
    }
    [RelayCommand]
    private async Task TryTranslate()
    {
        var jsonArray = new JsonArray();
        jsonArray.Add(new JsonObject
        {
            ["Text"] = SourceText
        });
        var content = new StringContent(jsonArray.ToJsonString(), Encoding.UTF8, "application/json");

        var text =await httpClient.PostAsync("",content).Result.Content.ReadAsStringAsync();


        try
        {
            using var doc = JsonDocument.Parse(text);
            var text2 = doc.RootElement[0]
                .GetProperty("translations")[0]
                .GetProperty("text")
                .GetString();
            TargetText = text2;
        }catch (Exception e)
        {
            TargetText = e.Message;
        }
    }
    ~TranslateWindowViewModel()
    {
        httpClient.Dispose();
    }
}