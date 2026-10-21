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
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
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

    private readonly Dictionary<string, Border> _categoryCards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StackPanel> _categoryPanels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _categoryHeaders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FontIcon> _categoryIcons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Control>> _categoryItems = new(StringComparer.Ordinal);
    private readonly Dictionary<Control, bool> _itemOriginalVisibility = new();
    private readonly List<Button> _pillButtons = new();
    private string? _activeCategory;

    public SettingPage()
    {
        InitializeComponent();
        _saveTimer.Tick += OnSaveTimerTick;
        AttachedToVisualTree += (_, _) => ScheduleRequestedFieldScroll();

        SearchBox.GetObservable(TextBox.TextProperty).Subscribe(query =>
        {
            ApplySearchFilter(query);
        });

        ClearSearchButton.Click += (_, _) =>
        {
            SearchBox.Text = string.Empty;
        };
    }

    public void ChangeConfig(ConfigBase configBase)
    {
        FlushPendingConfigSaves();
        disposables.Clear();
        _fieldControls.Clear();
        _categoryCards.Clear();
        _categoryPanels.Clear();
        _categoryHeaders.Clear();
        _categoryIcons.Clear();
        _categoryItems.Clear();
        _itemOriginalVisibility.Clear();
        _pillButtons.Clear();
        _activeCategory = null;
        _requestedFieldName = null;
        _requestedFieldContainer = null;
        _configBase = configBase;

        SearchBox.IsVisible = false;
        CategoryTabsPanel.IsVisible = false;
        CategoryTabsPanel.Children.Clear();
        EmptyResultPanel.IsVisible = false;

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
        _categoryCards.Clear();
        _categoryPanels.Clear();
        _categoryHeaders.Clear();
        _categoryIcons.Clear();
        _categoryItems.Clear();
        _itemOriginalVisibility.Clear();
        _pillButtons.Clear();
        _activeCategory = null;
        _requestedFieldName = requestedFieldName;
        _requestedFieldContainer = null;

        SearchBox.IsVisible = true;
        SearchBox.Text = string.Empty;
        EmptyResultPanel.IsVisible = false;

        StackPanel.Children.Clear();
        CategoryTabsPanel.Children.Clear();

        disposables.Add(TextBlock.Bind(TextBlock.TextProperty, new Binding("[lang.kitopia.settings]") { Source = Lang.Current }));

        // Main Config
        var mainStackPanel = new StackPanel { Spacing = 14 };
        StackPanel.Children.Add(mainStackPanel);

        LoadConfig(mainStackPanel, ConfigManger.Config);

        // Plugin Configs
        var pluginConfigs = ConfigManger.AllConfigs.Values.Where(c => c != ConfigManger.Config).ToList();
        foreach (var config in pluginConfigs)
        {
            var title = config.GetType().GetCustomAttribute<ConfigName>()?.Name ?? config.Name;
            var expander = new Expander { IsVisible = false, Width = 0, Height = 0 };
            var header = new TextBlock();
            disposables.Add(header.Bind(TextBlock.TextProperty, (BindingBase)new LangExtension(title).ProvideValue(null!)));
            expander.Header = header;
            mainStackPanel.Children.Add(expander);

            LoadConfig(mainStackPanel, config);
        }

        BuildCategoryTabs();
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
        _categoryCards.Clear();
        _categoryPanels.Clear();
        _categoryHeaders.Clear();
        _categoryIcons.Clear();
        _categoryItems.Clear();
        _itemOriginalVisibility.Clear();
        _pillButtons.Clear();
        _activeCategory = null;
        _requestedFieldName = null;
        _requestedFieldContainer = null;
        nowControl = null!;
        StackPanel.Children.Clear();
        CategoryTabsPanel.Children.Clear();
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
                // Keep the request for a later edit or final flush.
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
                // Keep navigation usable.
            }
        }
    }

    private (Border Card, StackPanel ItemsPanel, TextBlock HeaderBlock, FontIcon Icon) CreateCategoryCard(Panel container, string categoryKey, int symbol = 0, string? fallbackSubtitleKey = null)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KitopiaCardBackgroundBrush"),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("KitopiaCardBorderBrush"),
            BorderThickness = new Thickness(1),
            [!Border.BoxShadowProperty] = new DynamicResourceExtension("KitopiaCardBoxShadow"),
            Padding = new Thickness(18, 16),
            Margin = new Thickness(0, 0, 0, 14)
        };

        var cardStack = new StackPanel { Spacing = 10 };

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KitopiaAvatarBackground"),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var fontIcon = new FontIcon
        {
            Glyph = symbol != 0 ? Convert.ToChar(symbol).ToString() : "\uF6AA",
            FontSize = 18,
            [!FontIcon.ForegroundProperty] = new DynamicResourceExtension("KitopiaPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBorder.Child = fontIcon;
        Grid.SetColumn(iconBorder, 0);

        var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var headerBlock = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SemiColorText0")
        };
        disposables.Add(headerBlock.Bind(TextBlock.TextProperty, (BindingBase)new LangExtension(categoryKey).ProvideValue(null!)));
        textStack.Children.Add(headerBlock);

        var descKey = GetCategoryDescriptionKey(categoryKey);
        if (!string.IsNullOrEmpty(descKey))
        {
            var subtitleBlock = new TextBlock
            {
                FontSize = 12,
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SemiColorText2"),
                TextWrapping = TextWrapping.Wrap
            };
            disposables.Add(subtitleBlock.Bind(TextBlock.TextProperty, (BindingBase)new LangExtension(descKey).ProvideValue(null!)));
            textStack.Children.Add(subtitleBlock);
        }
        else if (!string.IsNullOrEmpty(fallbackSubtitleKey) && fallbackSubtitleKey != categoryKey)
        {
            var subtitleBlock = new TextBlock
            {
                FontSize = 12,
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SemiColorText2"),
                TextWrapping = TextWrapping.Wrap
            };
            disposables.Add(subtitleBlock.Bind(TextBlock.TextProperty, (BindingBase)new LangExtension(fallbackSubtitleKey).ProvideValue(null!)));
            textStack.Children.Add(subtitleBlock);
        }
        Grid.SetColumn(textStack, 1);

        headerGrid.Children.Add(iconBorder);
        headerGrid.Children.Add(textStack);

        var divider = new Rectangle
        {
            Height = 1,
            [!Rectangle.FillProperty] = new DynamicResourceExtension("SemiColorBorder"),
            Margin = new Thickness(0, 2, 0, 4)
        };

        var itemsPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Spacing = 4
        };

        cardStack.Children.Add(headerGrid);
        cardStack.Children.Add(divider);
        cardStack.Children.Add(itemsPanel);
        card.Child = cardStack;

        container.Children.Add(card);
        _categoryCards[categoryKey] = card;
        _categoryPanels[categoryKey] = itemsPanel;
        _categoryHeaders[categoryKey] = headerBlock;
        _categoryItems[categoryKey] = new List<Control>();

        return (card, itemsPanel, headerBlock, fontIcon);
    }

    private void BuildCategoryTabs()
    {
        CategoryTabsPanel.Children.Clear();
        _pillButtons.Clear();

        if (_categoryCards.Count <= 1)
        {
            CategoryTabsPanel.IsVisible = false;
            return;
        }

        CategoryTabsPanel.IsVisible = true;

        var allButton = CreatePillButton(null);
        _pillButtons.Add(allButton);
        CategoryTabsPanel.Children.Add(allButton);

        foreach (var categoryKey in _categoryCards.Keys)
        {
            var btn = CreatePillButton(categoryKey);
            _pillButtons.Add(btn);
            CategoryTabsPanel.Children.Add(btn);
        }

        UpdatePillButtonStyles();
    }

    private Button CreatePillButton(string? categoryKey)
    {
        var button = new Button
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 5),
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = categoryKey
        };

        if (categoryKey is null)
        {
            disposables.Add(button.Bind(ContentControl.ContentProperty, new Binding("[lang.kitopia.all_settings]") { Source = Lang.Current }));
        }
        else
        {
            disposables.Add(button.Bind(ContentControl.ContentProperty, (BindingBase)new LangExtension(categoryKey).ProvideValue(null!)));
        }

        button.Click += (_, _) =>
        {
            SelectCategory(categoryKey);
        };

        return button;
    }

    private void SelectCategory(string? categoryKey)
    {
        _activeCategory = categoryKey;
        SearchBox.Text = string.Empty;
        UpdatePillButtonStyles();

        foreach (var (key, card) in _categoryCards)
        {
            var visible = categoryKey == null || key == categoryKey;
            card.IsVisible = visible;
        }
    }

    private void UpdatePillButtonStyles()
    {
        foreach (var btn in _pillButtons)
        {
            var cat = btn.Tag as string;
            var isSelected = (cat == null && _activeCategory == null) || (cat != null && cat == _activeCategory);

            if (isSelected)
            {
                btn[!Button.BackgroundProperty] = new DynamicResourceExtension("KitopiaPrimaryLightBrush");
                btn[!Button.ForegroundProperty] = new DynamicResourceExtension("KitopiaPrimaryBrush");
                btn.FontWeight = FontWeight.SemiBold;
            }
            else
            {
                btn[!Button.BackgroundProperty] = new DynamicResourceExtension("SemiColorFill0");
                btn[!Button.ForegroundProperty] = new DynamicResourceExtension("SemiColorText0");
                btn.FontWeight = FontWeight.Normal;
            }
        }
    }

    private void ApplySearchFilter(string? query)
    {
        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var search = query?.Trim();
        var anyMatch = false;

        foreach (var (categoryKey, card) in _categoryCards)
        {
            var categoryMatches = false;
            if (_categoryItems.TryGetValue(categoryKey, out var items))
            {
                foreach (var item in items)
                {
                    var matches = !hasQuery || MatchesSearch(item, search!);
                    item.IsVisible = matches;
                    if (matches)
                    {
                        categoryMatches = true;
                        anyMatch = true;
                    }
                }
            }

            if (hasQuery)
            {
                card.IsVisible = categoryMatches;
            }
            else
            {
                card.IsVisible = _activeCategory == null || _activeCategory == categoryKey;
                if (_categoryItems.TryGetValue(categoryKey, out var catItems))
                {
                    foreach (var itm in catItems)
                    {
                        if (_itemOriginalVisibility.TryGetValue(itm, out var originalVis))
                            itm.IsVisible = originalVis;
                        else
                            itm.IsVisible = true;
                    }
                }
            }
        }

        EmptyResultPanel.IsVisible = hasQuery && !anyMatch;
    }

    private static bool MatchesSearch(Control control, string query)
    {
        if (control is SettingsExpander expander)
        {
            var header = expander.Header?.ToString();
            var desc = expander.Description;
            if (!string.IsNullOrEmpty(header) && header.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(desc) && desc.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }


    private static string? GetCategoryDescriptionKey(string? categoryKey)
    {
        return categoryKey switch
        {
            "lang.kitopia.general" => "lang.kitopia.category.general.desc",
            "lang.kitopia.search_window" => "lang.kitopia.category.search.desc",
            "lang.kitopia.semantic_search" => "lang.kitopia.category.semantic.desc",
            "lang.kitopia.indexing" => "lang.kitopia.category.indexing.desc",
            "lang.kitopia.selection_translation" => "lang.kitopia.category.translation.desc",
            "lang.kitopia.file_preview" => "lang.kitopia.category.preview.desc",
            "lang.kitopia.screenshot" => "lang.kitopia.category.screenshot.desc",
            "lang.kitopia.windows_enhancements" => "lang.kitopia.category.windows.desc",
            "lang.kitopia.device_sharing" => "lang.kitopia.category.device.desc",
            "lang.kitopia.more" => "lang.kitopia.category.more.desc",
            "lang.kitopia.extensions_and_plugins" => "lang.kitopia.category.plugins.desc",
            _ => null
        };
    }

    private void LoadConfig(Panel container, ConfigBase configBase)
    {
        nowControl = (StackPanel)container;
        _configBase = configBase;

        Application.Current.TryGetResource("FluentFont", null, out var _);
        if (configBase is null) return;
        if (string.IsNullOrWhiteSpace(configBase.Name))
        {
            configBase.Name = ConfigManger.AllConfigs.FirstOrDefault(x => x.Value == configBase).Key
                              ?? (configBase is KitopiaConfig ? "KitopiaConfig" : string.Empty);
        }

        var pluginTitle = configBase.GetType().GetCustomAttribute<ConfigName>()?.Name ?? configBase.Name;
        var isPlugin = configBase != ConfigManger.Config;

        TextBlock? categoryHeader = null;
        Border? currentCard = null;
        string? currentCategoryKey = null;

        void EnsureCategory(string categoryKey, int symbol = 0)
        {
            currentCategoryKey = categoryKey;
            if (_categoryCards.TryGetValue(currentCategoryKey, out var existingCard))
            {
                currentCard = existingCard;
                nowControl = _categoryPanels[currentCategoryKey];
                categoryHeader = _categoryHeaders[currentCategoryKey];
                if (symbol != 0 && _categoryIcons.TryGetValue(currentCategoryKey, out var icon))
                {
                    icon.Glyph = Convert.ToChar(symbol).ToString();
                }
            }
            else
            {
                var (card, itemsPanel, headerBlock, icon) = CreateCategoryCard(container, currentCategoryKey, symbol, isPlugin ? pluginTitle : null);
                categoryHeader = headerBlock;
                nowControl = itemsPanel;
                currentCard = card;
                _categoryIcons[currentCategoryKey] = icon;
            }
        }

        var fields = configBase.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
        var methods = configBase.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);

        // If configBase is KitopiaConfig, we handle language picker inside the General category or first
        SettingsExpander? languageRow = null;
        if (configBase is KitopiaConfig kitopiaCfg)
        {
            var languagePicker = new ComboBox
            {
                ItemsSource = Lang.Current.Languages.Prepend(string.Empty),
                SelectedItem = Lang.Current.Languages.Contains(kitopiaCfg.language) ? kitopiaCfg.language : string.Empty,
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
            languageRow = new SettingsExpander
            {
                Footer = languagePicker,
                IconSource = new FontIcon { Glyph = "\uF45B" },
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            disposables.Add(languageRow.Bind(SettingsExpander.HeaderProperty, new Binding("[lang.kitopia.display_language]") { Source = Lang.Current }));
            disposables.Add(languageRow.Bind(SettingsExpander.DescriptionProperty, new Binding("[lang.kitopia.changes_apply_immediately]") { Source = Lang.Current }));
            _fieldControls[nameof(KitopiaConfig.language)] = languageRow;
            disposables.Add(languagePicker.GetObservable(ComboBox.SelectedItemProperty).Skip(1).Subscribe(value =>
            {
                if (value is not string code || kitopiaCfg.language == code) return;
                kitopiaCfg.language = code;
                Lang.Current.UseLanguage(code);
                kitopiaCfg.OnConfigChanged(this, nameof(KitopiaConfig.language), code);
                ScheduleConfigSave(kitopiaCfg);
            }));
        }

        var languageRowAdded = false;

        foreach (var fieldInfo in fields)
        {
            var configFieldCategory = fieldInfo.GetCustomAttribute<ConfigFieldCategory>();
            if (configFieldCategory is not null && configFieldCategory.Category != currentCategoryKey)
            {
                EnsureCategory(configFieldCategory.Category, configFieldCategory.Symbol);

                if (!languageRowAdded && languageRow != null && currentCategoryKey == "lang.kitopia.general")
                {
                    nowControl.Children.Add(languageRow);
                    _categoryItems[currentCategoryKey].Add(languageRow);
                    languageRowAdded = true;
                }
            }

            if (fieldInfo.GetCustomAttribute<ConfigField>() is { } configField)
            {
                if (currentCategoryKey == null)
                {
                    EnsureCategory(isPlugin ? pluginTitle : "lang.kitopia.general", isPlugin ? 0xE9EA : 0xF6AA);
                }

                var SettingsExpander = new SettingsExpander
                {
                    Header = configField.Tittle,
                    Description = configField.Description,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    IconSource = new FontIcon
                    {
                        Glyph = Convert.ToChar(configField.Symbol).ToString()
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
                    case ConfigFieldType.目录:
                        {
                            if (fieldInfo.FieldType != typeof(string))
                                throw new InvalidOperationException($"Directory setting {fieldInfo.Name} must be a string field.");
                            var pathText = new TextBox
                            {
                                Text = selectedValue as string,
                                IsReadOnly = true,
                                FontSize = 13,
                                Height = 32,
                                CornerRadius = new CornerRadius(6),
                                VerticalContentAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 8, 0)
                            };
                            disposables.Add(pathText.Bind(ToolTip.TipProperty,
                                new Binding(nameof(TextBox.Text)) { Source = pathText }));
                            var folderButton = new Button
                            {
                                Content = new FontIcon { Glyph = "\uF419", FontSize = 16 },
                                Width = 32,
                                Height = 32,
                                Padding = new Thickness(0),
                                CornerRadius = new CornerRadius(6),
                                HorizontalContentAlignment = HorizontalAlignment.Center,
                                VerticalContentAlignment = VerticalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            disposables.Add(folderButton.Bind(ToolTip.TipProperty,
                                new Binding("[lang.kitopia.select_folder]") { Source = Lang.Current }));
                            folderButton.Command = new AsyncRelayCommand(async () =>
                            {
                                var picker = ServiceManager.Services.GetService<IFeatureFilePicker>();
                                if (picker is null) return;
                                var paths = await picker.PickFoldersAsync(Lang.Get("lang.kitopia.select_folder"), false);
                                if (folderButton.Command is null || paths.Count == 0) return;
                                var path = paths[0];
                                if (Equals(fieldInfo.GetValue(configBase), path)) return;
                                fieldInfo.SetValue(configBase, path);
                                pathText.Text = path;
                                configBase.OnConfigChanged(this, fieldInfo.Name, path);
                                ScheduleConfigSave(configBase);
                            });
                            disposables.Add(Disposable.Create(() => folderButton.Command = null));
                            var directoryPicker = new Grid
                            {
                                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                                Width = 320,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            Grid.SetColumn(folderButton, 1);
                            directoryPicker.Children.Add(pathText);
                            directoryPicker.Children.Add(folderButton);
                            SettingsExpander.Footer = directoryPicker;
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
                                        var val = (int)d;
                                        if (Equals(fieldInfo.GetValue(configBase), val)) return;
                                        fieldInfo.SetValue(configBase, val);
                                        configBase.OnConfigChanged(this, fieldInfo.Name, val);
                                        ScheduleConfigSave(configBase);
                                    }));
                            stackPanel.Children.Add(slider);
                            stackPanel.Children.Add(textBox);

                            SettingsExpander.Footer = stackPanel;
                            break;
                        }

                    case ConfigFieldType.浮点数:
                        {
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
                        }
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
                            if (configField.GetType().IsGenericType)
                            {
                                var typeArguments = configField.GetType().GetGenericArguments();
                                if (typeArguments[0].IsEnum)
                                {
                                    var comboBox = new ComboBox
                                    {
                                        ItemsSource = typeArguments[0].GetEnumValues(),
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

                            if (configField.ActionName != null && configBase.invokes.TryGetValue(configField.ActionName, out var actVal))
                            {
                                if (actVal is Delegate func)
                                {
                                    var result = func.DynamicInvoke();
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
                                disposables.Add(Disposable.Create(() =>
                                {
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
                if (currentCategoryKey != null && _categoryItems.TryGetValue(currentCategoryKey, out var itemList))
                {
                    itemList.Add(SettingsExpander);
                }
                BindConfigVisibility(SettingsExpander, configBase, configField, categoryHeader, currentCard);
            }
        }

        // If languageRow was not added yet (e.g. no General category), add it to container
        if (!languageRowAdded && languageRow != null)
        {
            nowControl.Children.Insert(0, languageRow);
            languageRowAdded = true;
        }

        foreach (var methodInfo in methods)
        {
            var configFieldCategory = methodInfo.GetCustomAttribute<ConfigFieldCategory>();
            if (configFieldCategory is not null && configFieldCategory.Category != currentCategoryKey)
            {
                EnsureCategory(configFieldCategory.Category, configFieldCategory.Symbol);
            }

            if (methodInfo.GetCustomAttribute<ConfigField>() is { } configField)
            {
                if (currentCategoryKey == null)
                {
                    EnsureCategory(isPlugin ? pluginTitle : "lang.kitopia.general", isPlugin ? 0xE9EA : 0xF6AA);
                }

                var SettingsExpander = new SettingsExpander
                {
                    Header = configField.Tittle,
                    Description = configField.Description,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    IconSource = new FontIcon
                    {
                        Glyph = Convert.ToChar(configField.Symbol).ToString()
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
                if (currentCategoryKey != null && _categoryItems.TryGetValue(currentCategoryKey, out var methodItemList))
                {
                    methodItemList.Add(SettingsExpander);
                }
                BindConfigVisibility(SettingsExpander, configBase, configField, categoryHeader, currentCard);
            }
        }
    }

    private void BindConfigVisibility(Control control, ConfigBase configBase, ConfigField configField, TextBlock? categoryHeader, Border? card = null)
    {
        var category = (Panel)control.Parent!;
        void UpdateVisibility(bool visible)
        {
            control.IsVisible = visible;
            _itemOriginalVisibility[control] = visible;
            if (categoryHeader is not null)
            {
                var hasVisible = category.Children.Any(item => item.IsVisible);
                categoryHeader.IsVisible = hasVisible;
                category.IsVisible = hasVisible;
                if (card is not null)
                {
                    card.IsVisible = hasVisible;
                }
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
