using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitopiaEx.Ocr;
using KitopiaEx.Translate;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx.CustomScenarioMethods;

public class Translate
{
    [ScenarioMethod("翻译文字提取结果", $"{nameof(dResult)}=文字识别结果数据",$"{nameof(sourceTranslateLang)}=源语言",$"{nameof(translateLang)}=目标语言", "return=文字识别结果数据")]
    public async Task<IEnumerable<OcrResult>> TranslateOcrResults(IEnumerable<OcrResult> dResult,[SelfInput]SourceTranslateLang sourceTranslateLang,[SelfInput]TargetTranslateLang translateLang, CancellationToken ct)
    {
        List<OcrResult> result = new List<OcrResult>();
        
        
        foreach (var item in dResult)
        {
                
            result.Add(item with { Text = await TranslateApi.GetTranslation(item.Text,sourceTranslateLang,translateLang) });
        }

        return result;
    }
    
    [ScenarioMethod("翻译文字", $"{nameof(dResult)}=文字识别结果数据",$"{nameof(sourceTranslateLang)}=源语言",$"{nameof(translateLang)}=目标语言", "return=文字识别结果数据")]
    public async Task<string> TranslateOcrResults(string dResult,[SelfInput]SourceTranslateLang sourceTranslateLang,[SelfInput]TargetTranslateLang translateLang, CancellationToken? ct=null)
    {
        return await TranslateApi.GetTranslation(dResult,sourceTranslateLang,translateLang);
            
        
    }
    
    
}