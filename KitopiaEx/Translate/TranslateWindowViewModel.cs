using System;
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
    
    public TranslateWindowViewModel()
    {
       
    }
    [RelayCommand]
    private async Task TryTranslate()
    {
        TargetText=await TranslateApi.GetTranslation(SourceText, SourceTranslateLang, TargetTranslateLang);
    }
    [RelayCommand]
    private async Task SwapLanguages()
    {
        var tempLang = SourceTranslateLang;
        SourceTranslateLang = TargetTranslateLang switch
        {
            TargetTranslateLang.简体中文 => SourceTranslateLang.简体中文,
            TargetTranslateLang.繁體中文 => SourceTranslateLang.繁體中文,
            TargetTranslateLang.English => SourceTranslateLang.English,
            TargetTranslateLang.日本語 => SourceTranslateLang.日本語,
            _ => throw new ArgumentOutOfRangeException()
        };
        TargetTranslateLang = tempLang switch
        {
            SourceTranslateLang.简体中文 => TargetTranslateLang.简体中文,
            SourceTranslateLang.繁體中文 => TargetTranslateLang.繁體中文,
            SourceTranslateLang.English => TargetTranslateLang.English,
            SourceTranslateLang.日本語 => TargetTranslateLang.日本語,
            SourceTranslateLang.自动检测 => TargetTranslateLang.简体中文,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
   
}