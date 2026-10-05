using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitopiaEx.Ocr;
using KitopiaEx.Translate;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx.CustomScenarioMethods;

public class Translate
{
    [ScenarioMethod("lang.kitopiaex.translate_ocr_results", $"{nameof(dResult)}=lang.kitopiaex.ocr_result_data",$"{nameof(sourceTranslateLang)}=lang.kitopiaex.source_language",$"{nameof(translateLang)}=lang.kitopiaex.target_language", "return=lang.kitopiaex.ocr_result_data", Id = "翻译文字提取结果")]
    public async Task<IEnumerable<OcrResult>> TranslateOcrResults(IEnumerable<OcrResult> dResult,[SelfInput]SourceTranslateLang sourceTranslateLang,[SelfInput]TargetTranslateLang translateLang, CancellationToken ct)
    {
        List<OcrResult> result = new List<OcrResult>();
        
        
        foreach (var item in dResult)
        {
                
            result.Add(item with { Text = await TranslateApi.GetTranslation(item.Text,sourceTranslateLang,translateLang) });
        }

        return result;
    }
    
    [ScenarioMethod("lang.kitopiaex.translate_text", $"{nameof(dResult)}=lang.kitopiaex.ocr_result_data",$"{nameof(sourceTranslateLang)}=lang.kitopiaex.source_language",$"{nameof(translateLang)}=lang.kitopiaex.target_language", "return=lang.kitopiaex.ocr_result_data", Id = "翻译文字")]
    public async Task<string> TranslateOcrResults(string dResult,[SelfInput]SourceTranslateLang sourceTranslateLang,[SelfInput]TargetTranslateLang translateLang, CancellationToken? ct=null)
    {
        return await TranslateApi.GetTranslation(dResult,sourceTranslateLang,translateLang);
            
        
    }
    
    
}