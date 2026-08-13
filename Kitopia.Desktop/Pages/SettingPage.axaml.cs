using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
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
    private string? _requestedFieldName;
    private Control? _requestedFieldContainer;

    private StackPanel nowControl;

    public SettingPage()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ScheduleRequestedFieldScroll();
    }

    public void ChangeConfig(ConfigBase configBase)
    {
        disposables.Clear();
        _configBase = configBase;
        TextBlock.Text = configBase.GetType()
            .GetCustomAttribute<ConfigName>()
            ?.Name ?? configBase.Name;
        StackPanel.Children.Clear();
        LoadConfig(StackPanel, configBase);
    }

    public void LoadAllConfigs(string? requestedFieldName = null)
    {
        disposables.Clear();
        _fieldControls.Clear();
        _requestedFieldName = requestedFieldName;
        _requestedFieldContainer = null;
        StackPanel.Children.Clear();
        TextBlock.Text = "设置";
        
        // Main Config
        var mainExpander = new Expander();
        mainExpander.Classes.Add("SemiExpander");
        mainExpander.Header = new TextBlock { Text = "主程序设置", FontSize = 16, FontWeight = FontWeight.SemiBold };
        mainExpander.HorizontalAlignment = HorizontalAlignment.Stretch;
        mainExpander.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        mainExpander.IsExpanded = true;
        mainExpander.Margin = new Thickness(0, 0, 0, 8);
        var mainStackPanel = new StackPanel();
        mainExpander.Content = mainStackPanel;
        StackPanel.Children.Add(mainExpander);
        
        LoadConfig(mainStackPanel, ConfigManger.Config);

        // Plugin Configs
        foreach (var config in ConfigManger.Configs.Values)
        {
            if (config == ConfigManger.Config) continue; 

            var expander = new Expander();
            expander.Classes.Add("SemiExpander");
            expander.Header = new TextBlock { Text = config.GetType().GetCustomAttribute<ConfigName>()?.Name ?? config.Name, FontSize = 14, FontWeight = FontWeight.SemiBold };
            expander.HorizontalAlignment = HorizontalAlignment.Stretch;
            expander.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            expander.IsExpanded = false;
            expander.Margin = new Thickness(0, 4);
            var stackPanel = new StackPanel();
            expander.Content = stackPanel;
            StackPanel.Children.Add(expander);
            
            LoadConfig(stackPanel, config);
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
        disposables.Clear();
        _configBase = null;
        nowControl = null;
        StackPanel.Children.Clear();
    }


    private void LoadConfig(Panel container, ConfigBase configBase)
    {
        nowControl = (StackPanel)container;
        _configBase = configBase; // Note: This sets the global _configBase to the last loaded config. 
                                  // This is strictly for potential side effects if other methods use _configBase.
                                  // However, our refactored LoadConfig uses the local 'configBase' parameter.
                                  
        Application.Current.TryGetResource("FluentFont", null, out var font);
        if (configBase is null) return;
        foreach (var fieldInfo in configBase.GetType()
                     .GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            var configFieldCategory = fieldInfo.GetCustomAttribute<ConfigFieldCategory>();
            if (configFieldCategory is not null)
            {
                var category = new Expander
                {
                    Header = new TextBlock { Text = configFieldCategory.Category, FontSize = 14, FontWeight = FontWeight.SemiBold },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsExpanded = true
                };
                var stackPanel = new StackPanel();

                stackPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                category.Content = stackPanel;
                nowControl = stackPanel;
                container.Children.Add(category);
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
                _fieldControls[fieldInfo.Name] = SettingsExpander;
                if (fieldInfo.Name == _requestedFieldName)
                {
                    _requestedFieldContainer = nowControl;
                }

                var selectedValue = fieldInfo.GetValue(configBase);
                switch (configField.FieldType)
                {
                    case ConfigFieldType.字符串:
                    {
                        var textBox = new TextBox
                        {
                            Text = selectedValue?.ToString()
                        };
                        disposables.Add(textBox.GetObservable(TextBox.TextProperty)
                            .Subscribe((d) =>
                            {
                                configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                fieldInfo.SetValue(configBase, d);
                                ConfigManger.Save(configBase.Name);
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
                            Step = configField.Step
                        };
                        disposables.Add(
                            textBox.GetObservable(NumericIntUpDown.ValueProperty)
                                .Subscribe((d) =>
                                {
                                    configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                    fieldInfo.SetValue(configBase, d);
                                    ConfigManger.Save(configBase.Name);
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
                            SelectedValue = selectedValue
                        };
                        disposables.Add(
                            comboBox.GetObservable(ComboBox.SelectedValueProperty)
                                .Subscribe((d) =>
                                {
                                    configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                    fieldInfo.SetValue(configBase, d);
                                    ConfigManger.Save(configBase.Name);
                                }));
                        SettingsExpander.Footer = comboBox;
                        break;
                    }
                    case ConfigFieldType.整数滑块:
                    {
                        var stackPanel = new StackPanel();
                        stackPanel.Orientation = Orientation.Horizontal;
                        stackPanel.VerticalAlignment = VerticalAlignment.Center;
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
                            FontSize = 14,
                            Margin = new Thickness(10, 0, 0, 0),
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
                                .Subscribe((d) =>
                                {
                                    configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                    fieldInfo.SetValue(configBase, (int)d);
                                    ConfigManger.Save(configBase.Name);
                                }));
                        stackPanel.Children.Add(textBox);
                        stackPanel.Children.Add(slider);

                        SettingsExpander.Footer = stackPanel;
                        break;
                    }

                    case ConfigFieldType.浮点数:
                        var textBox1 = new NumericDoubleUpDown
                        {
                            Value = (double)selectedValue,
                            Maximum = configField.MaxValue,
                            Minimum = configField.MinValue
                        };

                        disposables.Add(
                            textBox1.GetObservable(NumericDoubleUpDown.ValueProperty)
                                .Subscribe((d) =>
                                {
                                    configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                    fieldInfo.SetValue(configBase, d);
                                    ConfigManger.Save(configBase.Name);
                                }));

                        SettingsExpander.Footer = textBox1;
                        break;
                    case ConfigFieldType.布尔:
                    {
                        var toggleSwitch = new ToggleSwitch
                        {
                            IsChecked = (bool)selectedValue,
                            FlowDirection = FlowDirection.RightToLeft,
                            OnContent = "开",
                            OffContent = "关"
                        };
                        disposables.Add(
                            toggleSwitch.GetObservable(ToggleSwitch.IsCheckedProperty)
                                .Subscribe((d) =>
                                {
                                    configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                    fieldInfo.SetValue(configBase, d);
                                    ConfigManger.Save(configBase.Name);
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
                                .Subscribe((d) =>
                                {
                                    configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                    fieldInfo.SetValue(configBase, d);
                                    ConfigManger.Save(configBase.Name);
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
                                    SelectedValue = selectedValue
                                };
                                disposables.Add(
                                    comboBox.GetObservable(ComboBox.SelectedValueProperty)
                                        .Subscribe((d) =>
                                        {
                                            configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                            fieldInfo.SetValue(configBase, d);
                                            ConfigManger.Save(configBase.Name);
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
                                    SelectedValue = selectedValue
                                };

                                disposables.Add(
                                    comboBox.GetObservable(ComboBox.SelectedValueProperty)
                                        .Subscribe((d) =>
                                        {
                                            configBase.OnConfigChanged(this, fieldInfo.Name, d);
                                            fieldInfo.SetValue(configBase, d);
                                            ConfigManger.Save(configBase.Name);
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
                            disposables.Add(new AnonymousDisposable(() => {
                                observableCollection.CollectionChanged -= handler;
                            }));

                            if (configField.FieldType is ConfigFieldType.目录列表 or ConfigFieldType.文件和目录列表)
                            {
                                listShow.ShowFolderPicker = true;
                                listShow.PickFoldersCommand = new AsyncRelayCommand(async () =>
                                {
                                    var picker = ServiceManager.Services.GetService<IFeatureFilePicker>();
                                    if (picker is null) return;
                                    AddPaths(observableCollection, await picker.PickFoldersAsync("选择目录", true), handler);
                                });
                            }

                            if (configField.FieldType is ConfigFieldType.文件列表 or ConfigFieldType.文件和目录列表)
                            {
                                listShow.ShowFilePicker = true;
                                listShow.PickFilesCommand = new AsyncRelayCommand(async () =>
                                {
                                    var picker = ServiceManager.Services.GetService<IFeatureFilePicker>();
                                    if (picker is null) return;
                                    AddPaths(observableCollection, await picker.PickFilesAsync("选择文件", true), handler);
                                });
                            }
                        }

                        listShow.ItemsSource = enumerable;
                        SettingsExpander.ItemsSource = new[] { listShow };
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }


                nowControl.Children.Add(SettingsExpander);
            }
        }

        foreach (var methodInfo in configBase.GetType().GetMethods())
        {
             var configFieldCategory = methodInfo.GetCustomAttribute<ConfigFieldCategory>();
            if (configFieldCategory is not null)
            {
                var category = new Expander
                {
                    Header = new TextBlock { Text = configFieldCategory.Category, FontSize = 14, FontWeight = FontWeight.SemiBold },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsExpanded = true
                };
                var stackPanel = new StackPanel();

                stackPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                category.Content = stackPanel;
                nowControl = stackPanel;
                container.Children.Add(category);
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

                        SettingsExpander.Footer = new Button()
                        {
                            Command = command,
                            Content = configField.ActionName
                        };
                        break;
                    }
                  
                    default:
                        throw new ArgumentOutOfRangeException();
                }


                nowControl.Children.Add(SettingsExpander);
            }
        }
    }

    private void ObservableCollectionChange(object? sender,
        NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs, ConfigBase configBase, string fieldName)
    {
        configBase.OnConfigChanged(this, fieldName, notifyCollectionChangedEventArgs.NewItems);
        ConfigManger.Save(configBase.Name);
        if (configBase is not KitopiaConfig)
        {
            return;
        }

        var maintenanceService = ServiceManager.Services.GetService<IIndexMaintenanceService>();
        if (fieldName == nameof(KitopiaConfig.everythingSearchExtensions))
        {
            _ = maintenanceService?.RefreshEverythingFilesAsync();
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

        await maintenanceService.RefreshManagedFilesAsync();
        var index = ServiceManager.Services.GetService<IIndexService>();
        if (index is null)
        {
            return;
        }

        await index.IndexIncrementalAsync(IndexRebuildScope.Documents);
        await index.IndexIncrementalAsync(IndexRebuildScope.Images);
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

public class AnonymousDisposable : IDisposable
{
    private readonly Action _onDispose;

    public AnonymousDisposable(Action onDispose)
    {
        _onDispose = onDispose;
    }

    public void Dispose()
    {
        _onDispose.Invoke();
    }
}
