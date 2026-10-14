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
        System.ArgumentNullException.ThrowIfNull(dResult);
        ct.ThrowIfCancellationRequested();
        List<OcrResult> result = new List<OcrResult>();
        
        
        foreach (var item in dResult)
        {
                
            result.Add(item with { Text = await TranslateApi.GetTranslation(item.Text,sourceTranslateLang,translateLang, ct).ConfigureAwait(false) });
        }

        return result;
    }
    
    [ScenarioMethod("lang.kitopiaex.translate_text", $"{nameof(dResult)}=lang.kitopiaex.text",$"{nameof(sourceTranslateLang)}=lang.kitopiaex.source_language",$"{nameof(translateLang)}=lang.kitopiaex.target_language", "return=lang.kitopiaex.text", Id = "翻译文字")]
    public Task<string> TranslateOcrResults(string dResult,[SelfInput]SourceTranslateLang sourceTranslateLang,[SelfInput]TargetTranslateLang translateLang, CancellationToken? ct=null)
    {
        return TranslateApi.GetTranslation(dResult,sourceTranslateLang,translateLang, ct ?? CancellationToken.None);
            
        
    }
    
    
}
