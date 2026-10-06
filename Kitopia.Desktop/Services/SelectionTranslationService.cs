#if WINDOWS
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Kitopia.Desktop.Abstractions.TextSelection;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Windows;
using Kitopia.Feature.Localization;
using PluginCore;
using SharpHook;
using Vanara.PInvoke;

namespace Kitopia.Desktop.Services;

public sealed class SelectionTranslationService : ISelectionTranslationService
{
    private const int AutomaticDelayMilliseconds = 350;
    private const int MaximumTextLength = 1000;

    private readonly SimpleGlobalHook _inputHook;
    private readonly IHotKetImpl _hotkeys;
    private readonly ITextSelectionService _selectionService;
    private readonly ITranslationService _translationService;
    private readonly SelectionTranslationWindow _window;
    private readonly SemaphoreSlim _selectionReadGate = new(1, 1);
    private CancellationTokenSource? _requestCancellation;
    private TextSelectionSnapshot? _currentSelection;
    private int _requestVersion;
    private bool _started;

    public SelectionTranslationService(
        SimpleGlobalHook inputHook,
        IHotKetImpl hotkeys,
        ITextSelectionService selectionService,
        ITranslationService translationService,
        SelectionTranslationWindow window)
    {
        _inputHook = inputHook;
        _hotkeys = hotkeys;
        _selectionService = selectionService;
        _translationService = translationService;
        _window = window;
        _window.ViewModel.CloseRequested += CloseWindow;
        _window.ViewModel.RetryRequested += RetryCurrentSelection;
        _window.ViewModel.LanguageChanged += TranslateCurrentSelection;
        _window.ViewModel.ExcludeCurrentProcessRequested += ExcludeCurrentProcess;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _inputHook.MousePressed += OnMousePressed;
    }

    public void Refresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }

        var hotkey = ConfigManger.Config.selectionTranslationAutoHotKey;
        if (!hotkey.IsEnabled || (hotkey.Type == HotKeyType.Mouse && !ConfigManger.Config.mouseCapture) ||
            (_currentSelection is { } selection && !hotkey.CanExecuteInProcess(selection.ProcessName)))
        {
            CancelCurrentRequest();
            if (!_window.ViewModel.IsPinned) CloseWindow();
        }
    }

    public async Task TriggerManualAsync()
    {
        await TranslateFromSelectionAsync(manual: true).ConfigureAwait(false);
    }

    public Task TriggerAutomaticAsync(HotKeyModel hotKeyModel) => _started
        ? TranslateFromSelectionAsync(manual: false, hotKeyModel.TriggerPosition)
        : Task.CompletedTask;

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _inputHook.MousePressed -= OnMousePressed;
        CancelCurrentRequest();
        Dispatcher.UIThread.Post(CloseWindow);
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        if ((ushort)e.Data.Button != 1) return;
        if (!User32.GetCursorPos(out var point)) return;

        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (!_window.IsVisible || _window.ViewModel.IsPinned ||
                        IsInsideWindow(point.X, point.Y)) return;
                    CloseWindow();
                }
                catch (Exception exception)
                {
                    LogManager.Logger.Warning(exception, "处理划词翻译鼠标按下事件失败");
                }
            });
        }
        catch (Exception exception)
        {
            LogManager.Logger.Warning(exception, "投递划词翻译鼠标按下事件失败");
        }
    }

    private async Task TranslateFromSelectionAsync(bool manual, PixelPoint? releasePosition = null)
    {
        var (version, token) = BeginRequest();
        Dispatcher.UIThread.Post(() =>
        {
            if (version == Volatile.Read(ref _requestVersion))
                _window.ViewModel.CanExcludeCurrentProcess = false;
        });
        try
        {
            var foreground = (nint)User32.GetForegroundWindow();
            if (foreground == 0) return;
            if (releasePosition is null && User32.GetCursorPos(out var point))
                releasePosition = new PixelPoint(point.X, point.Y);

            if (!manual)
                await Task.Delay(AutomaticDelayMilliseconds, token).ConfigureAwait(false);

            if ((nint)User32.GetForegroundWindow() != foreground) return;
            TextSelectionSnapshot? snapshot;
            await _selectionReadGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                snapshot = await Task.Run(
                        () => _selectionService.TryGetSelectionAsync(
                            manual && ConfigManger.Config.canReadClipboard, token), token)
                    .ConfigureAwait(false);
            }
            finally
            {
                _selectionReadGate.Release();
            }
            if (snapshot is null || snapshot.SourceWindow != foreground || string.IsNullOrWhiteSpace(snapshot.Text))
            {
                if (manual)
                    ShowError(version, Lang.Get("lang.kitopia.selection_translation_no_selection"), releasePosition);
                return;
            }
            if (!manual && (!ConfigManger.Config.selectionTranslationAutoHotKey.IsEnabled ||
                !ConfigManger.Config.selectionTranslationAutoHotKey.CanExecuteInProcess(snapshot.ProcessName))) return;
            if (snapshot.Text.Length > MaximumTextLength)
            {
                ShowError(version, Lang.Get("lang.kitopia.selection_translation_text_too_long"), releasePosition);
                return;
            }
            var windowState = await Dispatcher.UIThread.InvokeAsync(() =>
                (IsVisible: _window.IsVisible, IsPinned: _window.ViewModel.IsPinned,
                    SourceLanguage: _window.ViewModel.SourceLanguage,
                    TargetLanguage: _window.ViewModel.TargetLanguage));
            if (!manual && windowState.IsVisible && windowState.IsPinned) return;
            if (!manual && IsSameSelection(snapshot)) return;

            _currentSelection = snapshot;
            var sourceLanguage = ConfigManger.Config.selectionTranslationSourceLanguage;
            var targetLanguage = ConfigManger.Config.selectionTranslationTargetLanguage;
            if (windowState.IsVisible)
            {
                sourceLanguage = windowState.SourceLanguage;
                targetLanguage = windowState.TargetLanguage;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (version != Volatile.Read(ref _requestVersion)) return;
                _window.ViewModel.CanExcludeCurrentProcess =
                    !string.IsNullOrWhiteSpace(snapshot.ProcessName);
                _window.ViewModel.BeginTranslation(snapshot.Text, sourceLanguage, targetLanguage);
                _window.ShowAt(releasePosition);
            });

            var translated = await _translationService.TranslateAsync(
                    snapshot.Text, sourceLanguage, targetLanguage, token)
                .ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (version != Volatile.Read(ref _requestVersion) || !_window.IsVisible) return;
                _window.ViewModel.SetTranslatedText(translated);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogManager.Logger.Warning(exception, "划词翻译失败");
            ShowError(version, Lang.Get("lang.kitopia.selection_translation_failed"), releasePosition);
        }
    }

    private void TranslateCurrentSelection()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(TranslateCurrentSelection);
            return;
        }

        if (_currentSelection is null || !_window.IsVisible) return;
        _ = TranslateExistingSelectionAsync(_currentSelection);
    }

    private void RetryCurrentSelection()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RetryCurrentSelection);
            return;
        }

        if (_currentSelection is null) return;
        _ = TranslateExistingSelectionAsync(_currentSelection);
    }

    private void ExcludeCurrentProcess()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ExcludeCurrentProcess);
            return;
        }

        var processName = _currentSelection?.ProcessName;
        if (string.IsNullOrWhiteSpace(processName)) return;

        var normalized = HotKeyModel.NormalizeProcessName(processName);
        var candidate = new HotKeyModel(ConfigManger.Config.selectionTranslationAutoHotKey);
        if (candidate.ProcessScope == HotKeyProcessScope.Include)
        {
            candidate.ProcessNames = candidate.ProcessNames.Where(name =>
                !string.Equals(HotKeyModel.NormalizeProcessName(name), normalized,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        else
        {
            candidate.ProcessNames = candidate.ProcessScope == HotKeyProcessScope.All
                ? [normalized]
                : candidate.ProcessNames.Append(normalized).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            candidate.ProcessScope = HotKeyProcessScope.Exclude;
        }
        if (_hotkeys.Modify(candidate)) CloseWindow();
    }

    private async Task TranslateExistingSelectionAsync(TextSelectionSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => _ = TranslateExistingSelectionAsync(snapshot));
            return;
        }

        var sourceLanguage = _window.ViewModel.SourceLanguage;
        var targetLanguage = _window.ViewModel.TargetLanguage;
        var (version, token) = BeginRequest();
        Dispatcher.UIThread.Post(() =>
        {
            if (version == Volatile.Read(ref _requestVersion))
                _window.ViewModel.BeginTranslation(snapshot.Text, sourceLanguage, targetLanguage);
        });
        try
        {
            var translated = await _translationService.TranslateAsync(
                    snapshot.Text, sourceLanguage, targetLanguage, token)
                .ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (version == Volatile.Read(ref _requestVersion) && _window.IsVisible)
                    _window.ViewModel.SetTranslatedText(translated);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogManager.Logger.Warning(exception, "重新翻译选区失败");
            ShowError(version, Lang.Get("lang.kitopia.selection_translation_failed"));
        }
    }

    private (int Version, CancellationToken Token) BeginRequest()
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _requestCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        return (Interlocked.Increment(ref _requestVersion), cancellation.Token);
    }

    private void CancelCurrentRequest()
    {
        var cancellation = Interlocked.Exchange(ref _requestCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        Interlocked.Increment(ref _requestVersion);
    }

    private bool IsSameSelection(TextSelectionSnapshot snapshot) =>
        _currentSelection is { } previous &&
        previous.SourceWindow == snapshot.SourceWindow &&
        string.Equals(previous.Text, snapshot.Text, StringComparison.Ordinal);

    private bool IsInsideWindow(int x, int y)
    {
        var screen = _window.Screens.ScreenFromWindow(_window);
        var scaling = screen?.Scaling ?? 1;
        var bounds = _window.Bounds;
        var left = _window.Position.X;
        var top = _window.Position.Y;
        var right = left + (int)Math.Ceiling(bounds.Width * scaling);
        var bottom = top + (int)Math.Ceiling(bounds.Height * scaling);
        return x >= left && x <= right && y >= top && y <= bottom;
    }

    private void ShowError(int version, string message, PixelPoint? releasePosition = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (version != Volatile.Read(ref _requestVersion)) return;
            _window.ViewModel.SetError(message);
            if (!_window.IsVisible) _window.ShowAt(releasePosition);
        });
    }

    private void CloseWindow()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(CloseWindow);
            return;
        }

        CancelCurrentRequest();
        _window.ViewModel.CanExcludeCurrentProcess = false;
        _window.Hide();
    }
}
#endif
