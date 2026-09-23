using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Win32.Input;
using Kitopia.Desktop.Features.Services.HotKey;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Ursa.Controls;

namespace Kitopia.Desktop.Windows;

public partial class HotKeyEditorWindow : UrsaWindow
{
    private readonly HotKeyModel _hotKeyModel;
    private readonly ObservableCollection<string> _processTags;
    private readonly ObservableCollection<string> _filteredSuggestions = [];
    private List<string> _runningProcesses = [];
    private HotKeyType _type;
    private EKey? _selectedKey;
    private ushort? _selectedMouseButton;

    public HotKeyEditorWindow(HotKeyModel hotKeyModel)
    {
        ArgumentNullException.ThrowIfNull(hotKeyModel);
        InitializeComponent();
        _hotKeyModel = hotKeyModel;
        Name.Text = Converter.HotKeySignNameToStringCtr.FormatFriendlyName(hotKeyModel.SignName);
        _type = hotKeyModel.Type;
        _selectedKey = hotKeyModel.SelectKey;
        _selectedMouseButton = hotKeyModel.MouseButton;
        KeyBoard.IsChecked = _type == HotKeyType.Keyboard;
        Mouse.IsChecked = _type == HotKeyType.Mouse;
        Slider.Value = hotKeyModel.PressTimeMillis;
        Ctrl.IsVisible = _type == HotKeyType.Keyboard && hotKeyModel.IsSelectCtrl;
        Alt.IsVisible = _type == HotKeyType.Keyboard && hotKeyModel.IsSelectAlt;
        Shift.IsVisible = _type == HotKeyType.Keyboard && hotKeyModel.IsSelectShift;
        Win.IsVisible = _type == HotKeyType.Keyboard && hotKeyModel.IsSelectWin;
        KeyName.Content = _type == HotKeyType.Keyboard ? hotKeyModel.SelectKey.ToString() : MouseButtonName(hotKeyModel.MouseButton);
        ProcessScope.SelectedIndex = (int)hotKeyModel.ProcessScope;
        _processTags = new ObservableCollection<string>(
            hotKeyModel.ProcessNames
                .Select(HotKeyModel.NormalizeProcessName)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        ProcessTagsControl.ItemsSource = _processTags;
        _processTags.CollectionChanged += (_, _) => UpdateEmptyTagsHint();
        UpdateEmptyTagsHint();

        ProcessSuggestionsList.ItemsSource = _filteredSuggestions;
        ProcessSuggestionsPopup.PlacementTarget = ProcessNames;
        ProcessSuggestionsPopup.Placement = PlacementMode.BottomEdgeAlignedLeft;
        ProcessSuggestionsPopup.Opened += (_, _) =>
        {
            if (ProcessSuggestionsPopup.Child is Control child && ProcessNames.Bounds.Width > 0)
            {
                child.Width = ProcessNames.Bounds.Width;
            }
        };
        ProcessNames.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                UpdateSuggestions();
            }
        };
        ProcessNames.GotFocus += (_, _) => UpdateSuggestions();

        LoadRunningProcesses();
        IgnoreTextInput.IsChecked = hotKeyModel.IgnoreTextInput;
        Opened += (_, _) => MouseCaptureArea.Focus();
    }

    private void HotKeyEditorWindow_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_type != HotKeyType.Keyboard || !MouseCaptureArea.IsKeyboardFocusWithin) return;
        Ctrl.IsVisible = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        Alt.IsVisible = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        Shift.IsVisible = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        Win.IsVisible = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var key = e.Key == Key.System ? e.PhysicalKey.ToQwertyKey() : e.Key;
        if (key is Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            or Key.LeftCtrl or Key.RightCtrl or Key.None) return;
        _selectedKey = (EKey)KeyInterop.VirtualKeyFromKey(key);
        KeyName.Content = _selectedKey.ToString();
        KeyName.IsVisible = true;
        e.Handled = true;
    }

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        if ((_type == HotKeyType.Keyboard && _selectedKey is null or EKey.未设置 or 0) ||
            (_type == HotKeyType.Mouse && _selectedMouseButton is null or 0 or ushort.MaxValue))
        {
            ValidationMessage.Text = "请先录入快捷键。";
            return;
        }
        var scope = (HotKeyProcessScope)ProcessScope.SelectedIndex;
        var textProcesses = (ProcessNames.Text ?? "").Split(['\r', '\n', ',', ';', '，', '；', '、'],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(HotKeyModel.NormalizeProcessName).Where(name => name.Length > 0);
        var processes = _processTags
            .Select(HotKeyModel.NormalizeProcessName)
            .Concat(textProcesses)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (scope != HotKeyProcessScope.All && processes.Length == 0)
        {
            ValidationMessage.Text = "请至少填写一个进程名。";
            ProcessNames.Focus();
            return;
        }
        var candidate = new HotKeyModel(_hotKeyModel)
        {
            IsSelectAlt = Alt.IsVisible,
            IsSelectWin = Win.IsVisible,
            IsSelectShift = Shift.IsVisible,
            IsSelectCtrl = Ctrl.IsVisible,
            SelectKey = _selectedKey ?? EKey.未设置,
            MouseButton = _selectedMouseButton,
            Type = _type,
            PressTimeMillis = (ushort)Slider.Value,
            ProcessScope = scope,
            ProcessNames = processes,
            IgnoreTextInput = IgnoreTextInput.IsChecked == true
        };
        if (!ServiceManager.Services.GetRequiredService<IHotKetImpl>().Modify(candidate))
        {
            ValidationMessage.Text = "快捷键注册失败，可能已被其他程序占用。";
            return;
        }
        Close();
    }

    private void ButtonCancle_OnClick(object sender, RoutedEventArgs e) => Close();

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MouseCaptureArea.Focus();
        if (_type != HotKeyType.Mouse) return;
        var properties = e.GetCurrentPoint(this).Properties;
        _selectedMouseButton = properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => (ushort)MouseHookType.LeftButton,
            PointerUpdateKind.RightButtonPressed => (ushort)MouseHookType.RightButton,
            PointerUpdateKind.MiddleButtonPressed => (ushort)MouseHookType.MiddleButton,
            PointerUpdateKind.XButton1Pressed => (ushort)MouseHookType.XButton1,
            PointerUpdateKind.XButton2Pressed => (ushort)MouseHookType.XButton2,
            _ => _selectedMouseButton
        };
        KeyName.Content = MouseButtonName(_selectedMouseButton);
        KeyName.IsVisible = true;
        e.Handled = true;
    }

    private static string MouseButtonName(ushort? button) => button switch
    {
        (ushort)MouseHookType.LeftButton => "鼠标左键",
        (ushort)MouseHookType.RightButton => "鼠标右键",
        (ushort)MouseHookType.MiddleButton => "鼠标中键",
        (ushort)MouseHookType.XButton1 => "鼠标侧键1",
        (ushort)MouseHookType.XButton2 => "鼠标侧键2",
        _ => "未设置"
    };

    private void KeyBoard_OnClick(object? sender, RoutedEventArgs e)
    {
        _type = HotKeyType.Keyboard;
        KeyName.Content = _selectedKey?.ToString() ?? "未设置";
        MouseCaptureArea.Focus();
    }

    private void Mouse_OnClick(object? sender, RoutedEventArgs e)
    {
        _type = HotKeyType.Mouse;
        Ctrl.IsVisible = Alt.IsVisible = Shift.IsVisible = Win.IsVisible = false;
        KeyName.Content = MouseButtonName(_selectedMouseButton);
        MouseCaptureArea.Focus();
    }

    private void ProcessScope_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProcessListSection is not null) ProcessListSection.IsVisible = ProcessScope.SelectedIndex > 0;
    }

    private void UpdateEmptyTagsHint()
    {
        if (EmptyTagsHint is not null)
        {
            EmptyTagsHint.IsVisible = _processTags.Count == 0;
        }
    }

    private void LoadRunningProcesses()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        var name = p.ProcessName;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            names.Add(HotKeyModel.NormalizeProcessName(name));
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
                var result = names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                Dispatcher.UIThread.Post(() =>
                {
                    _runningProcesses = result;
                    if (!string.IsNullOrWhiteSpace(ProcessNames.Text))
                    {
                        UpdateSuggestions();
                    }
                });
            }
            catch
            {
                Dispatcher.UIThread.Post(() => _runningProcesses = []);
            }
        });
    }

    private void UpdateSuggestions()
    {
        if (ProcessSuggestionsPopup is null || ProcessSuggestionsList is null) return;
        var query = ProcessNames.Text?.Trim();
        if (string.IsNullOrEmpty(query) || !ProcessNames.IsKeyboardFocusWithin)
        {
            ProcessSuggestionsPopup.IsOpen = false;
            return;
        }

        var queryNorm = HotKeyModel.NormalizeProcessName(query);
        var available = _runningProcesses
            .Where(p => !_processTags.Contains(p, StringComparer.OrdinalIgnoreCase)
                        && (p.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            p.Contains(queryNorm, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(p => p.StartsWith(queryNorm, StringComparison.OrdinalIgnoreCase) || p.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        _filteredSuggestions.Clear();
        foreach (var item in available)
        {
            _filteredSuggestions.Add(item);
        }

        if (ProcessSuggestionsPopup.Child is Control child && ProcessNames.Bounds.Width > 0)
        {
            child.Width = ProcessNames.Bounds.Width;
        }
        ProcessSuggestionsPopup.IsOpen = _filteredSuggestions.Count > 0;
    }

    private void AddProcessTag(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput)) return;
        var items = rawInput.Split(['\r', '\n', ',', ';', '，', '；', '、'],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in items)
        {
            var normalized = HotKeyModel.NormalizeProcessName(item);
            if (normalized.Length > 0 && !_processTags.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                _processTags.Add(normalized);
            }
        }
        ProcessNames.Text = string.Empty;
        if (ProcessSuggestionsPopup is not null)
        {
            ProcessSuggestionsPopup.IsOpen = false;
        }
        ValidationMessage.Text = string.Empty;
    }

    private void RemoveProcessTag_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: string tag })
        {
            _processTags.Remove(tag);
            UpdateSuggestions();
        }
    }

    private void AddProcessButton_OnClick(object? sender, RoutedEventArgs e)
    {
        AddProcessTag(ProcessNames.Text);
        ProcessNames.Focus();
    }

    private void ProcessNames_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && ProcessSuggestionsPopup.IsOpen && _filteredSuggestions.Count > 0)
        {
            ProcessSuggestionsList.Focus();
            if (ProcessSuggestionsList.SelectedIndex < 0)
            {
                ProcessSuggestionsList.SelectedIndex = 0;
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            if (!string.IsNullOrWhiteSpace(ProcessNames.Text))
            {
                AddProcessTag(ProcessNames.Text);
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && ProcessSuggestionsPopup.IsOpen)
        {
            ProcessSuggestionsPopup.IsOpen = false;
            e.Handled = true;
            return;
        }
    }

    private void ProcessSuggestionsList_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ProcessSuggestionsList.SelectedItem is string selected)
            {
                AddProcessTag(selected);
                ProcessNames.Focus();
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            ProcessSuggestionsPopup.IsOpen = false;
            ProcessNames.Focus();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Up && ProcessSuggestionsList.SelectedIndex <= 0)
        {
            ProcessSuggestionsList.SelectedIndex = -1;
            ProcessNames.Focus();
            e.Handled = true;
            return;
        }
    }

    private void SuggestionItem_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control { DataContext: string procName })
        {
            AddProcessTag(procName);
            ProcessNames.Focus();
            e.Handled = true;
        }
    }
}
