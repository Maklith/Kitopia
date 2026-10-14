using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PluginCore;

namespace Kitopia.Desktop.ViewModels;

public partial class SelectionTranslationWindowViewModel : ObservableObject
{
    private readonly IClipboardService _clipboard;

    public SelectionTranslationWindowViewModel(IClipboardService clipboard)
    {
        _clipboard = clipboard;
    }

    public TranslationSourceLanguage[] SourceLanguages { get; } = Enum.GetValues<TranslationSourceLanguage>();
    public TranslationTargetLanguage[] TargetLanguages { get; } = Enum.GetValues<TranslationTargetLanguage>();

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _translatedText = string.Empty;

    [ObservableProperty]
    private TranslationSourceLanguage _sourceLanguage = TranslationSourceLanguage.Auto;

    [ObservableProperty]
    private TranslationTargetLanguage _targetLanguage = TranslationTargetLanguage.SimplifiedChinese;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private bool _canExcludeCurrentProcess;

    public bool HasTranslation => !string.IsNullOrEmpty(TranslatedText);
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public event Action? CloseRequested;
    public event Action? RetryRequested;
    public event Action? LanguageChanged;
    public event Action? ExcludeCurrentProcessRequested;

    partial void OnTranslatedTextChanged(string value) =>
        OnPropertyChanged(nameof(HasTranslation));

    partial void OnErrorMessageChanged(string? value) =>
        OnPropertyChanged(nameof(HasError));

    partial void OnSourceLanguageChanged(TranslationSourceLanguage value) => LanguageChanged?.Invoke();

    partial void OnTargetLanguageChanged(TranslationTargetLanguage value) => LanguageChanged?.Invoke();

    public void BeginTranslation(string sourceText, TranslationSourceLanguage sourceLanguage,
        TranslationTargetLanguage targetLanguage)
    {
        SourceText = sourceText;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        TranslatedText = string.Empty;
        ErrorMessage = null;
        IsLoading = true;
    }

    public void SetTranslatedText(string text)
    {
        TranslatedText = text;
        ErrorMessage = null;
        IsLoading = false;
    }

    public void SetError(string message)
    {
        TranslatedText = string.Empty;
        ErrorMessage = message;
        IsLoading = false;
    }

    [RelayCommand]
    private void CopySource()
    {
        if (!string.IsNullOrEmpty(SourceText)) _clipboard.SetText(SourceText);
    }

    [RelayCommand]
    private void CopyTranslation()
    {
        if (!string.IsNullOrEmpty(TranslatedText)) _clipboard.SetText(TranslatedText);
    }

    [RelayCommand]
    private void Retry() => RetryRequested?.Invoke();

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    [RelayCommand]
    private void ExcludeCurrentProcess() => ExcludeCurrentProcessRequested?.Invoke();
}
