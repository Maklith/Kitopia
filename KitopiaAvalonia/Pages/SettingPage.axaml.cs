using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Disposables;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Core.Services.Config;
using KitopiaAvalonia.Controls;
using PluginCore;
using PluginCore.Config;
using PluginCore.CustomScenario.Attribute.ConfigField;
using Ursa.Controls;
using FontIcon = KitopiaAvalonia.Controls.FontIcon;
using SettingsExpander = KitopiaAvalonia.Controls.SettingsExpander.SettingsExpander;

namespace KitopiaAvalonia.Pages;

public partial class SettingPage : UserControl
{
    private ConfigBase? _configBase;
    private CompositeDisposable disposables = new();

    private StackPanel nowControl;

    public SettingPage()
    {
        InitializeComponent();
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

    public void LoadAllConfigs()
    {
        disposables.Clear();
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
                    {
                        var listShow = new ListShow();

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
                            NotifyCollectionChangedEventHandler handler = (sender, args) => ObservableCollectionChange(sender, args, configBase);
                            observableCollection.CollectionChanged += handler;
                            disposables.Add(new AnonymousDisposable(() => {
                                observableCollection.CollectionChanged -= handler;
                            }));
                        }

                        listShow.ItemsSource = enumerable;
                        SettingsExpander.ItemsSource = new[] { listShow };
                        break;
                    }
                    case ConfigFieldType.字符串列表支持添加:
                    {
                        var listShow = new ListShow();
                        listShow.WithAdd = true;
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
                            NotifyCollectionChangedEventHandler handler = (sender, args) => ObservableCollectionChange(sender, args, configBase);
                            observableCollection.CollectionChanged += handler;
                            disposables.Add(new AnonymousDisposable(() => {
                                observableCollection.CollectionChanged -= handler;
                            }));
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
        NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs, ConfigBase configBase)
    {
        configBase.OnConfigChanged(this, "", notifyCollectionChangedEventArgs.NewItems);
        ConfigManger.Save(configBase.Name);
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
