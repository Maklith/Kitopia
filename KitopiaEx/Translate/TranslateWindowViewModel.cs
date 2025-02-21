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
    
    public TranslateWindowViewModel()
    {
       
    }
    [RelayCommand]
    private async Task TryTranslate()
    {
        TargetText=await TranslateApi.GetTranslation(SourceText, SourceTranslateLang, TargetTranslateLang);
    }
   
}