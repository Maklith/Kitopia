using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Feature.Localization;
using Kitopia.Feature.Avalonia.Localization;
using Microsoft.Extensions.DependencyInjection;
using Kitopia.Desktop.Controls;
using PluginCore;
using PluginCore.Config;
using PluginCore.CustomScenario.Attribute.ConfigField;
using Ursa.Controls;
using FontIcon = Kitopia.Desktop.Controls.FontIcon;
using SettingsExpander = Kitopia.Desktop.Controls.SettingsExpander.SettingsExpander;

namespace Kitopia.Desktop.Pages;

public partial class SettingPage : UserControl
{
    private ConfigBase? _configBase;
    private CompositeDisposable disposables = new();
    private readonly Dictionary<string, Control> _fieldControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _pendingConfigSaves = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private int _saveVersion;
    private string? _requestedFieldName;
    private Control? _requestedFieldContainer;

    private StackPanel nowControl;

    public SettingPage()
    {
        InitializeComponent();
        _saveTimer.Tick += OnSaveTimerTick;
        AttachedToVisualTree += (_, _) => ScheduleRequestedFieldScroll();
    }

    public void ChangeConfig(ConfigBase configBase)
    {
        FlushPendingConfigSaves();
        disposables.Clear();
        _fieldControls.Clear();
        _requestedFieldName = null;
        _requestedFieldContainer = null;
        _configBase = configBase;
        var title = configBase.GetType().GetCustomAttribute<ConfigName>()?.Name ?? configBase.Name;
        disposables.Add(TextBlock.Bind(TextBlock.TextProperty, (BindingBase)new LangExtension(title).ProvideValue(null!)));
        StackPanel.Children.Clear();
        LoadConfig(StackPanel, configBase);
    }

    public void LoadAllConfigs(string? requestedFieldName = null)
    {
        FlushPendingConfigSaves();
        disposables.Clear();
        _fieldControls.Clear();
        _requestedFieldName = requestedFieldName;
        _requestedFieldContainer = null;
        StackPanel.Children.Clear();
        disposables.Add(TextBlock.Bind(TextBlock.TextProperty, new Binding("[lang.kitopia.settings]") { Source = Lang.Current }));
        
        // Main Config
        var mainStackPanel = new StackPanel { Spacing = 4 };
        StackPanel.Children.Add(mainStackPanel);
        
        LoadConfig(mainStackPanel, ConfigManger.Config);

        // Plugin Configs
        var pluginConfigs = ConfigManger.AllConfigs.Values.Where(c => c != ConfigManger.Config).ToList();
        if (pluginConfigs.Count > 0)
        {
            var pluginHeader = new TextBlock
            {
                Text = Lang.Get("lang.kitopia.extensions_and_plugins"),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = Application.Current?.FindResource("SemiColorText2") as IBrush ?? Brushes.Gray,
                Margin = new Thickness(4, 24, 0, 8)
            };
            StackPanel.Children.Add(pluginHeader);
            disposables.Add(pluginHeader.Bind(TextBlock.TextProperty, new Binding("[lang.kitopia.extensions_and_plugins]") { Source = Lang.Current }));

            foreach (var config in pluginConfigs)
            {
                var expander = new Expander();
                expander.Classes.Add("SemiExpander");
                var title = config.GetType().GetCustomAttribute<ConfigName>()?.Name ?? config.Name;
                var header = new TextBlock { FontSize = 14, FontWeight = FontWeight.SemiBold };
                disposables.Add(header.Bind(TextBlock.TextProperty, (BindingBase)new LangExtension(title).ProvideValue(null!)));
                expander.Header = header;
                expander.HorizontalAlignment = HorizontalAlignment.Stretch;
                expander.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                expander.IsExpanded = false;
                expander.Margin = new Thickness(0, 4);
                var stackPanel = new StackPanel { Spacing = 4 };
                expander.Content = stackPanel;
                StackPanel.Children.Add(expander);
                
                LoadConfig(stackPanel, config);
            }
        }

        ScheduleRequestedFieldScroll();
    }
    
    ~SettingPage()
    {
        disposables.Dispose();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        FlushPendingConfigSaves();
        disposables.Clear();
        _configBase = null;
        _fieldControls.Clear();
        _requestedFieldName = null;
        _requestedFieldContainer = null;
        nowControl = null;
        StackPanel.Children.Clear();
    }

    private void ScheduleConfigSave(ConfigBase config)
    {
        if (!ConfigManger.AllConfigs.TryGetValue(config.Name, out var current) || !ReferenceEquals(current, config))
            return;

        _pendingConfigSaves[config.Name] = ++_saveVersion;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void OnSaveTimerTick(object? sender, EventArgs args)
    {
        _saveTimer.Stop();
        foreach (var (key, version) in _pendingConfigSaves.ToArray())
        {
            if (!_pendingConfigSaves.ContainsKey(key)) continue;
            try
            {
                await ConfigManger.SaveAsync(key);
                if (_pendingConfigSaves.TryGetValue(key, out var currentVersion) && currentVersion == version)
                    _pendingConfigSaves.Remove(key);
            }
            catch (Exception)
            {
                // The manager reports the failure. Keep the request for a later edit or final flush.
            }
        }
    }

    private void FlushPendingConfigSaves()
    {
        _saveTimer.Stop();
        foreach (var key in _pendingConfigSaves.Keys.ToArray())
        {
            try
            {
                if (ConfigManger.AllConfigs.ContainsKey(key)) ConfigManger.Save(key);
                _pendingConfigSaves.Remove(key);
            }
            catch (Exception)
            {
                // Keep navigation usable; the manager has already reported the failed save.
            }
        }
    }


    private void LoadConfig(Panel container, ConfigBase configBase)
    {
        nowControl = (StackPanel)container;
        _configBase = configBase; // Note: This sets the global _configBase to the last loaded config. 
                                  // This is strictly for potential side effects if other methods use _configBase.
                                  // However, our refactored LoadConfig uses the local 'configBase' parameter.
                                  
        Application.Current.TryGetResource("FluentFont", null, out var font);
        if (configBase is null) return;
        if (string.IsNullOrWhiteSpace(configBase.Name))
        {
            configBase.Name = ConfigManger.AllConfigs.FirstOrDefault(x => x.Value == configBase).Key
                              ?? (configBase is KitopiaConfig ? "KitopiaConfig" : string.Empty);
        }
        TextBlock? categoryHeader = null;
        if (configBase is KitopiaConfig config)
        {
            var languagePicker = new ComboBox
            {
                ItemsSource = Lang.Current.Languages.Prepend(string.Empty),
                SelectedItem = Lang.Current.Languages.Contains(config.language) ? config.language : string.Empty,
                MinWidth = 180,
                Height = 32,
                ItemTemplate = new FuncDataTemplate<string>((code, _) =>
                {
                    var label = new TextBlock();
                    if (string.IsNullOrEmpty(code))
                        disposables.Add(label.Bind(TextBlock.TextProperty, new Binding("[lang.kitopia.system_default]") { Source = Lang.Current }));
                    else
                        label.Text = CultureInfo.GetCultureInfo(code).NativeName;
                    return label;
                })
            };
            var languageRow = new SettingsExpander
            {
                Footer = languagePicker,
                IconSource = new FontIcon { Glyph = char.ConvertFromUtf32(0xf33c) },
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            disposables.Add(languageRow.Bind(SettingsExpander.HeaderProperty, new Binding("[lang.kitopia.display_language]") { Source = Lang.Current }));
            disposables.Add(languageRow.Bind(SettingsExpander.DescriptionProperty, new Binding("[lang.kitopia.changes_apply_immediately]") { Source = Lang.Current }));
            container.Children.Add(languageRow);
            _fieldControls[nameof(KitopiaConfig.language)] = languageRow;
            disposables.Add(languagePicker.GetObservable(ComboBox.SelectedItemProperty).Skip(1).Subscribe(value =>
            {
                if (value is not string code || config.language == code) return;
                config.language = code;
                Lang.Current.UseLanguage(code);
                config.OnConfigChanged(this, nameof(KitopiaConfig.language), code);
                ScheduleConfigSave(config);
            }));
        }
        foreach (var fieldInfo in configBase.GetType()
                     .GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            var configFieldCategory = fieldInfo.GetCustomAttribute<ConfigFieldCategory>();
            if (configFieldCategory is not null)
            {
                categoryHeader = new TextBlock
                {
                    Text = configFieldCategory.Category,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Application.Current?.FindResource("SemiColorText2") as IBrush ?? Brushes.Gray,
                    Margin = new Thickness(4, 18, 0, 6)
                };
                disposables.Add(categoryHeader.Bind(TextBlock.TextProperty, (BindingBase)new LangExtension(configFieldCategory.Category).ProvideValue(null!)));
                var stackPanel = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Spacing = 4
                };
                nowControl = stackPanel;
                container.Children.Add(categoryHeader);
                container.Children.Add(stackPanel);
            }

            if (fieldInfo.GetCustomAttribute<ConfigField>() is { } configField)
            {
                var SettingsExpander = new SettingsExpander
                {
                    Header = configField.Tittle,
                    Description = configField.Description,
                    HorizontalAlignment = HorizontalAlignment.Stretch,

                    IconSource = new FontIcon
                    {
                        Glyph = Convert.ToChar(configField.Symbol)
                            .ToString()
                    }
                };
                disposables.Add(SettingsExpander.Bind(SettingsExpander.HeaderProperty, (BindingBase)new LangExtension(configField.Tittle).ProvideValue(null!)));
                if (!string.IsNullOrEmpty(configField.Description))
                    disposables.Add(SettingsExpander.Bind(SettingsExpander.DescriptionProperty, (BindingBase)new LangExtension(configField.Description).ProvideValue(null!)));
                _fieldControls[fieldInfo.Name] = SettingsExpander;
                if (fieldInfo.Name == _requestedFieldName)
                {
                    _requestedFieldContainer = nowControl;
                }

                var selectedValue = fieldInfo.GetValue(configBase);
                switch (configField.FieldType)
                {
                    case ConfigFieldType.颜色:
                    {
                        if (fieldInfo.FieldType != typeof(string))
                            throw new InvalidOperationException($"颜色配置 {fieldInfo.Name} 必须是保存 RGB 十六进制颜色的字符串字段。");
                        var colorPicker = new ColorPicker
                        {
                            Color = Color.TryParse(selectedValue as string, out var color) ? color : Color.FromRgb(0, 100, 250),
                            IsAlphaEnabled = false,
                            IsAlphaVisible = false,
                            Width = 64,
                            Height = 32,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Right
                        };
                        disposables.Add(colorPicker.Bind(ToolTip.TipProperty, (BindingBase)new LangExtension(configField.Tittle).ProvideValue(null!)));
                        EventHandler<ColorChangedEventArgs> handler = (_, args) =>
                        {
                            var value = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
                            if (Equals(fieldInfo.GetValue(configBase), value)) return;
                            fieldInfo.SetValue(configBase, value);
                            configBase.OnConfigChanged(this, fieldInfo.Name, value);
                            ScheduleConfigSave(configBase);
                        };
                        colorPicker.ColorChanged += handler;
                        disposables.Add(Disposable.Create(() => colorPicker.ColorChanged -= handler));
                        SettingsExpander.Footer = colorPicker;
                        break;
                    }
                    case ConfigFieldType.字符串:
                    {
                        var textBox = new TextBox
                        {
                            Text = selectedValue?.ToString(),
                            Width = 220,
                            Height = 32,
                            CornerRadius = new CornerRadius(6),
                            VerticalContentAlignment = VerticalAlignment.Center
                        };
                        disposables.Add(textBox.GetObservable(TextBox.TextProperty)
                            .Skip(1)
                            .Subscribe((d) =>
                            {
                                if (Equals(fieldInfo.GetValue(configBase), d)) return;
                                fieldInfo.SetValue(configBase, d);
                                configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                ScheduleConfigSave(configBase);
                            }));
                        SettingsExpander.Footer = textBox;
                        break;
                    }
                    case ConfigFieldType.整数:
                    {
                        var value = (int)selectedValue;
                        var textBox = new NumericIntUpDown
                        {
                            Value = value,
                            Maximum = configField.MaxValue,
                            Minimum = configField.MinValue,
                            Step = configField.Step,
                            Width = 140,
                            Height = 32,
                            CornerRadius = new CornerRadius(6),
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Right
                        };
                        disposables.Add(
                            textBox.GetObservable(NumericIntUpDown.ValueProperty)
                                .Skip(1)
                                .Subscribe((d) =>
                                {
                                    if (d is null || Equals(fieldInfo.GetValue(configBase), d)) return;
                                    fieldInfo.SetValue(configBase, d);
                                    configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                    ScheduleConfigSave(configBase);
                                }));

                        SettingsExpander.Footer = textBox;
                        break;
                    }
                    case ConfigFieldType.整数列表:
                    {
                        var comboBox = new ComboBox
                        {
                            ItemsSource = Enumerable.Range(configField.MinValue, configField.MaxValue)
                                .Select(x => (int)x % configField.Step == 0 ? x : 0)
                                .Where(x => x != 0)
                                .ToList(),
                            SelectedValue = selectedValue,
                            MinWidth = 140,
                            Height = 32,
                            CornerRadius = new CornerRadius(6),
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Right
                        };
                        disposables.Add(
                            comboBox.GetObservable(ComboBox.SelectedValueProperty)
                                .Skip(1)
                                .Subscribe((d) =>
                                {
                                    if (d is null || Equals(fieldInfo.GetValue(configBase), d)) return;
                                    fieldInfo.SetValue(configBase, d);
                                    configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                    ScheduleConfigSave(configBase);
                                }));
                        SettingsExpander.Footer = comboBox;
                        break;
                    }
                    case ConfigFieldType.整数滑块:
                    {
                        var stackPanel = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Right
                        };
                        var slider = new Slider
                        {
                            Maximum = configField.MaxValue,
                            Minimum = configField.MinValue,
                            Value = (int)selectedValue,
                            TickFrequency = configField.Step,
                            IsSnapToTickEnabled = true,
                            Width = 160,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        var textBox = new TextBlock
                        {
                            FontSize = 13,
                            FontWeight = FontWeight.Medium,
                            MinWidth = 36,
                            TextAlignment = TextAlignment.Right,
                            Margin = new Thickness(12, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };


                        var binding = new Binding("Value")
                        {
                            Source = slider,
                            Mode = BindingMode.OneWay
                        };
                        textBox.SetValue(ToolTip.TipProperty, binding);
                        textBox.SetValue(ToolTip.PlacementProperty, PlacementMode.Center);
                        
                        disposables.Add(textBox.Bind(TextBlock.TextProperty, binding));
                        disposables.Add(
                            slider.GetObservable(Slider.ValueProperty)
                                .Skip(1)
                                .Subscribe((d) =>
                                {
                                    var value = (int)d;
                                    if (Equals(fieldInfo.GetValue(configBase), value)) return;
                                    fieldInfo.SetValue(configBase, value);
                                    configBase.OnConfigChanged(this, fieldInfo.Name, value);
                                    ScheduleConfigSave(configBase);
                                }));
                        stackPanel.Children.Add(slider);
                        stackPanel.Children.Add(textBox);

                        SettingsExpander.Footer = stackPanel;
                        break;
                    }

                    case ConfigFieldType.浮点数:
                        var textBox1 = new NumericDoubleUpDown
                        {
                            Value = (double)selectedValue,
                            Maximum = configField.MaxValue,
                            Minimum = configField.MinValue,
                            Width = 140,
                            Height = 32,
                            CornerRadius = new CornerRadius(6),
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Right
                        };

                        disposables.Add(
                            textBox1.GetObservable(NumericDoubleUpDown.ValueProperty)
                                .Skip(1)
                                .Subscribe((d) =>
                                {
                                    if (d is null || Equals(fieldInfo.GetValue(configBase), d)) return;
                                    fieldInfo.SetValue(configBase, d);
                                    configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                    ScheduleConfigSave(configBase);
                                }));

                        SettingsExpander.Footer = textBox1;
                        break;
                    case ConfigFieldType.布尔:
                    {
                        var toggleSwitch = new ToggleSwitch
                        {
                            IsChecked = (bool)selectedValue,
                            OnContent = null,
                            OffContent = null,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        disposables.Add(
                            toggleSwitch.GetObservable(ToggleSwitch.IsCheckedProperty)
                                .Skip(1)
                                .Subscribe((d) =>
                                {
                                    if (d is null || Equals(fieldInfo.GetValue(configBase), d)) return;
                                    fieldInfo.SetValue(configBase, d);
                                    configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                    ScheduleConfigSave(configBase);
                                }));
                        SettingsExpander.Footer = toggleSwitch;
                        break;
                    }
                    case ConfigFieldType.快捷键:
                    {
                        var hotKeyModel = (HotKeyModel)selectedValue;
                        var hotKeyControl = new HotKeyShow();
                        hotKeyControl.HotKeyModel = hotKeyModel;
                        disposables.Add(
                            hotKeyControl.GetObservable(HotKeyShow.HotKeyModelProperty)
                                .Skip(1)
                                .Subscribe((d) =>
                                {
                                    if (d is null || Equals(fieldInfo.GetValue(configBase), d)) return;
                                    fieldInfo.SetValue(configBase, d);
                                    configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                    ScheduleConfigSave(configBase);
                                }));
                        SettingsExpander.Footer = hotKeyControl;
                        break;
                    }
                    case ConfigFieldType.自定义选项:
                    {
                        if (configField.GetType()
                            .IsGenericType) //判断是不是ConfigField<Enum>
                        {
                            var typeArguments = configField.GetType()
                                .GetGenericArguments();
                            if (typeArguments[0].IsEnum)
                            {
                                var comboBox = new ComboBox
                                {
                                    ItemsSource = typeArguments[0]
                                        .GetEnumValues(),
                                    ItemTemplate = new FuncDataTemplate<object>((value, _) =>
                                    {
                                        var label = new TextBlock();
                                        disposables.Add(label.Bind(TextBlock.TextProperty, (BindingBase)new LangExtension
                                        {
                                            Value = new Binding { Source = value }
                                        }.ProvideValue(null!)));
                                        return label;
                                    }),
                                    SelectedValue = selectedValue,
                                    MinWidth = 140,
                                    Height = 32,
                                    CornerRadius = new CornerRadius(6)
                                };
                                disposables.Add(
                                    comboBox.GetObservable(ComboBox.SelectedValueProperty)
                                        .Skip(1)
                                        .Subscribe((d) =>
                                        {
                                            if (d is null || Equals(fieldInfo.GetValue(configBase), d)) return;
                                            fieldInfo.SetValue(configBase, d);
                                            configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                            ScheduleConfigSave(configBase);
                                        }));
                                disposables.Add(Disposable.Create(() =>
                                {
                                    comboBox.ItemTemplate = null;
                                    comboBox.ItemsSource = null;
                                    comboBox.SelectedValue = null;
                                }));
                                SettingsExpander.Footer = comboBox;
                            }
                        }

                        if (configField.ActionName == null) break;
                        if (configBase.invokes.TryGetValue(configField.ActionName, out var value))
                            if (value is Delegate func)
                            {
                                // 使用 DynamicInvoke 来执行这个委托
                                var result = func.DynamicInvoke();

                                // 确保 result 转换为 IEnumerable<T>
                                var comboBox = new ComboBox
                                {
                                    ItemsSource = result as IEnumerable,
                                    SelectedValue = selectedValue,
                                    MinWidth = 140,
                                    Height = 32,
                                    CornerRadius = new CornerRadius(6)
                                };

                                disposables.Add(
                                    comboBox.GetObservable(ComboBox.SelectedValueProperty)
                                        .Skip(1)
                                        .Subscribe((d) =>
                                        {
                                            if (d is null || Equals(fieldInfo.GetValue(configBase), d)) return;
                                            fieldInfo.SetValue(configBase, d);
                                            configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                            ScheduleConfigSave(configBase);
                                        }));
                                disposables.Add(Disposable.Create(() =>
                                {
                                    comboBox.ItemsSource = null;
                                    comboBox.SelectedValue = null;
                                }));
                                SettingsExpander.Footer = comboBox;
                            }


                        break;
                    }
                    case ConfigFieldType.字符串列表:
                    case ConfigFieldType.字符串列表支持添加:
                    case ConfigFieldType.目录列表:
                    case ConfigFieldType.文件列表:
                    case ConfigFieldType.文件和目录列表:
                    {
                        var listShow = new ListShow
                        {
                            WithAdd = configField.FieldType == ConfigFieldType.字符串列表支持添加
                        };
                        SettingsExpander.Bind(WidthProperty, new Binding("Bounds.Width")
                        {
                            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
                            {
                                AncestorType = typeof(SettingsExpander)
                            },
                            Mode = BindingMode.OneWay
                        });
                        var enumerable = (IEnumerable?)selectedValue;
                        if (enumerable is ObservableCollection<string> observableCollection)
                        {
                            NotifyCollectionChangedEventHandler handler = (sender, args) => ObservableCollectionChange(sender, args, configBase, fieldInfo.Name);
                            observableCollection.CollectionChanged += handler;
                            disposables.Add(Disposable.Create(() => {
                                observableCollection.CollectionChanged -= handler;
                            }));

                            if (configField.FieldType is ConfigFieldType.目录列表 or ConfigFieldType.文件和目录列表)
                            {
                                listShow.ShowFolderPicker = true;
                                listShow.PickFoldersCommand = new AsyncRelayCommand(async () =>
                                {
                                    var picker = ServiceManager.Services.GetService<IFeatureFilePicker>();
                                    if (picker is null) return;
                                    AddPaths(observableCollection, await picker.PickFoldersAsync(Lang.Get("lang.kitopia.select_folder"), true), handler);
                                });
                            }

                            if (configField.FieldType is ConfigFieldType.文件列表 or ConfigFieldType.文件和目录列表)
                            {
                                listShow.ShowFilePicker = true;
                                listShow.PickFilesCommand = new AsyncRelayCommand(async () =>
                                {
                                    var picker = ServiceManager.Services.GetService<IFeatureFilePicker>();
                                    if (picker is null) return;
                                    AddPaths(observableCollection, await picker.PickFilesAsync(Lang.Get("lang.kitopia.select_file"), true), handler);
                                });
                            }
                        }

                        listShow.ItemsSource = enumerable;
                        // Detached controls can remain in Avalonia's composition tree. Drop plugin references explicitly.
                        disposables.Add(Disposable.Create(() =>
                        {
                            listShow.ClearValue(ListShow.PickFoldersCommandProperty);
                            listShow.ClearValue(ListShow.PickFilesCommandProperty);
                            listShow.ItemsSource = null;
                        }));
                        SettingsExpander.ItemsSource = new[] { listShow };
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }


                nowControl.Children.Add(SettingsExpander);
                BindConfigVisibility(SettingsExpander, configBase, configField, categoryHeader);
            }
        }

        foreach (var methodInfo in configBase.GetType().GetMethods())
        {
             var configFieldCategory = methodInfo.GetCustomAttribute<ConfigFieldCategory>();
            if (configFieldCategory is not null)
            {
                categoryHeader = new TextBlock
                {
                    Text = configFieldCategory.Category,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Application.Current?.FindResource("SemiColorText2") as IBrush ?? Brushes.Gray,
                    Margin = new Thickness(4, 16, 0, 8)
                };
                disposables.Add(categoryHeader.Bind(TextBlock.TextProperty, (BindingBase)new LangExtension(configFieldCategory.Category).ProvideValue(null!)));
                var stackPanel = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Spacing = 4
                };
                nowControl = stackPanel;
                container.Children.Add(categoryHeader);
                container.Children.Add(stackPanel);
            }

            if (methodInfo.GetCustomAttribute<ConfigField>() is { } configField)
            {
                var SettingsExpander = new SettingsExpander
                {
                    Header = configField.Tittle,
                    Description = configField.Description,
                    HorizontalAlignment = HorizontalAlignment.Stretch,

                    IconSource = new FontIcon
                    {
                        Glyph = Convert.ToChar(configField.Symbol)
                            .ToString()
                    }
                };

                disposables.Add(SettingsExpander.Bind(SettingsExpander.HeaderProperty, (BindingBase)new LangExtension(configField.Tittle).ProvideValue(null!)));
                if (!string.IsNullOrEmpty(configField.Description))
                    disposables.Add(SettingsExpander.Bind(SettingsExpander.DescriptionProperty, (BindingBase)new LangExtension(configField.Description).ProvideValue(null!)));
                
                switch (configField.FieldType)
                {
                    case ConfigFieldType.按钮:
                    {
                        System.Windows.Input.ICommand command;
                        if (typeof(System.Threading.Tasks.Task).IsAssignableFrom(methodInfo.ReturnType))
                        {
                            command = new AsyncRelayCommand(async () =>
                            {
                                var result = methodInfo.Invoke(configBase, null);
                                if (result is System.Threading.Tasks.Task task)
                                {
                                    await task;
                                }
                            });
                        }
                        else
                        {
                            command = new RelayCommand(() =>
                            {
                                methodInfo.Invoke(configBase, null);
                            });
                        }

                        var actionButton = new Button
                        {
                            Command = command,
                            Content = configField.ActionName,
                            Height = 32,
                            Padding = new Thickness(14, 0),
                            CornerRadius = new CornerRadius(6),
                            FontSize = 13,
                            FontWeight = FontWeight.Medium,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Right
                        };
                        disposables.Add(actionButton.Bind(ContentControl.ContentProperty, (BindingBase)new LangExtension(configField.Tittle).ProvideValue(null!)));
                        disposables.Add(Disposable.Create(() => actionButton.Command = null));
                        SettingsExpander.Footer = actionButton;
                        break;
                    }
                  
                    default:
                        throw new ArgumentOutOfRangeException();
                }


                nowControl.Children.Add(SettingsExpander);
                BindConfigVisibility(SettingsExpander, configBase, configField, categoryHeader);
            }
        }
    }

    private void BindConfigVisibility(Control control, ConfigBase configBase, ConfigField configField, TextBlock? categoryHeader)
    {
        var category = (Panel)control.Parent!;
        void UpdateVisibility(bool visible)
        {
            control.IsVisible = visible;
            if (categoryHeader is not null)
            {
                categoryHeader.IsVisible = category.Children.Any(item => item.IsVisible);
                category.IsVisible = categoryHeader.IsVisible;
            }
        }

        if (string.IsNullOrEmpty(configField.VisibleWhen))
        {
            UpdateVisibility(true);
            return;
        }

        var type = configBase.GetType();
        var field = type.GetField(configField.VisibleWhen, BindingFlags.Instance | BindingFlags.Public);
        var property = type.GetProperty(configField.VisibleWhen, BindingFlags.Instance | BindingFlags.Public);
        var value = field is not null ? field.GetValue(configBase)
            : property is { CanRead: true } && property.GetIndexParameters().Length == 0 ? property.GetValue(configBase) : null;
        if (value is not bool condition)
            throw new InvalidOperationException($"配置 {type.Name} 的显示条件 {configField.VisibleWhen} 必须是可读取的公共布尔字段或属性。");

        UpdateVisibility(condition == configField.VisibleWhenValue);
        EventHandler<ConfigChangedArgs> handler = (_, args) =>
        {
            if (args.Name != configField.VisibleWhen || args.Value is not bool changedValue)
                return;

            if (Dispatcher.UIThread.CheckAccess())
                UpdateVisibility(changedValue == configField.VisibleWhenValue);
            else
                Dispatcher.UIThread.Post(() => UpdateVisibility(changedValue == configField.VisibleWhenValue));
        };
        configBase.ConfigChanged += handler;
        disposables.Add(Disposable.Create(() => configBase.ConfigChanged -= handler));
    }

    private void ObservableCollectionChange(object? sender,
        NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs, ConfigBase configBase, string fieldName)
    {
        configBase.OnConfigChanged(this, fieldName, notifyCollectionChangedEventArgs.NewItems);
        ScheduleConfigSave(configBase);
        if (configBase is not KitopiaConfig)
        {
            return;
        }

        var maintenanceService = ServiceManager.Services.GetService<IIndexMaintenanceService>();
        if (fieldName == nameof(KitopiaConfig.everythingSearchExtensions))
        {
            _ = maintenanceService?.RefreshEverythingFilesAsync();
        }
        else if (fieldName == nameof(KitopiaConfig.transientDirectoryNames))
        {
            _ = RefreshAllFileSourcesAsync(maintenanceService);
        }
        else if (fieldName == nameof(KitopiaConfig.ignoreItems))
        {
            _ = RefreshAllFileSourcesAsync(maintenanceService);
        }
        else if (fieldName == nameof(KitopiaConfig.allowedFileExtensions))
        {
            _ = RefreshManagedIndexAsync(maintenanceService);
        }
        else if (fieldName is nameof(KitopiaConfig.managedIndexDirectories) or nameof(KitopiaConfig.managedIndexFiles))
        {
            _ = RefreshManagedIndexAsync(maintenanceService);
        }
    }

    private static void AddPaths(ObservableCollection<string> target, IEnumerable<string> paths,
        NotifyCollectionChangedEventHandler collectionChanged)
    {
        var added = false;
        target.CollectionChanged -= collectionChanged;
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path)
                && !target.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                target.Add(path);
                added = true;
            }
        }

        target.CollectionChanged += collectionChanged;
        if (added)
        {
            collectionChanged(target, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    private static async Task RefreshManagedIndexAsync(IIndexMaintenanceService? maintenanceService)
    {
        if (maintenanceService is null)
        {
            return;
        }

        await maintenanceService.StopBackgroundIndexingAsync();
        await maintenanceService.RefreshManagedFilesAsync();
        var index = ServiceManager.Services.GetService<IIndexService>();
        if (index is null)
        {
            return;
        }

        await index.IndexIncrementalAsync(IndexRebuildScope.Files);
    }

    private static async Task RefreshAllFileSourcesAsync(IIndexMaintenanceService? maintenanceService)
    {
        var index = ServiceManager.Services.GetService<IIndexService>();
        if (maintenanceService is null)
        {
            if (index is not null)
            {
                await index.RemoveIgnoredEntriesAsync();
            }

            return;
        }

        await maintenanceService.StopBackgroundIndexingAsync();
        if (index is not null)
        {
            await index.RemoveIgnoredEntriesAsync();
        }

        await maintenanceService.RefreshManagedFilesAsync();
        await maintenanceService.RefreshEverythingFilesAsync();
        if (index is not null)
        {
            await index.IndexIncrementalAsync(IndexRebuildScope.Files);
        }
    }

    private void ScheduleRequestedFieldScroll()
    {
        if (string.IsNullOrEmpty(_requestedFieldName)
            || !_fieldControls.TryGetValue(_requestedFieldName, out var field))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            (_requestedFieldContainer ?? field).BringIntoView();
            _requestedFieldName = null;
            _requestedFieldContainer = null;
        }, DispatcherPriority.Loaded);
    }
}
