using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Kitopia.Desktop.Controls;

public class ListShow : ListBox
{
    //ObservableCollection

    //DelCommand
    public static readonly StyledProperty<ICommand> DelCommandProperty =
        AvaloniaProperty.Register<ListShow, ICommand>(nameof(DelCommand));

    public static readonly StyledProperty<bool> WithAddProperty =
        AvaloniaProperty.Register<ListShow, bool>(nameof(WithAdd));

    public static readonly StyledProperty<ICommand> AddCommandProperty =
        AvaloniaProperty.Register<ListShow, ICommand>(nameof(AddCommand));

    public static readonly StyledProperty<ICommand> InnerAddCommandProperty =
        AvaloniaProperty.Register<ListShow, ICommand>(nameof(InnerAddCommand));

    public static readonly StyledProperty<ICommand> PickFilesCommandProperty =
        AvaloniaProperty.Register<ListShow, ICommand>(nameof(PickFilesCommand));

    public static readonly StyledProperty<ICommand> PickFoldersCommandProperty =
        AvaloniaProperty.Register<ListShow, ICommand>(nameof(PickFoldersCommand));

    public static readonly StyledProperty<bool> ShowFilePickerProperty =
        AvaloniaProperty.Register<ListShow, bool>(nameof(ShowFilePicker));

    public static readonly StyledProperty<bool> ShowFolderPickerProperty =
        AvaloniaProperty.Register<ListShow, bool>(nameof(ShowFolderPicker));

    public static readonly StyledProperty<bool> ShowEmptyHintProperty =
        AvaloniaProperty.Register<ListShow, bool>(nameof(ShowEmptyHint));

    public static readonly StyledProperty<string> TextValueProperty =
        AvaloniaProperty.Register<ListShow, string>(nameof(TextValue));

    //设置默认DelCommand
    public ListShow()
    {
        SetValue(DelCommandProperty, new RelayCommand<string>(OnDel, e => !string.IsNullOrWhiteSpace(e)));
        SetValue(AddCommandProperty, new RelayCommand<string>(OnAdd, e => !string.IsNullOrWhiteSpace(e)));
        SetValue(InnerAddCommandProperty, new RelayCommand<string>(InnerOnAdd, e => !string.IsNullOrWhiteSpace(e)));
    }

    public ICommand DelCommand
    {
        get => GetValue(DelCommandProperty);
        set => SetValue(DelCommandProperty, value);
    }

    public bool WithAdd
    {
        get => GetValue(WithAddProperty);
        set => SetValue(WithAddProperty, value);
    }

    public ICommand AddCommand
    {
        get => GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public ICommand InnerAddCommand
    {
        get => GetValue(InnerAddCommandProperty);
        set => SetValue(InnerAddCommandProperty, value);
    }

    public ICommand PickFilesCommand
    {
        get => GetValue(PickFilesCommandProperty);
        set => SetValue(PickFilesCommandProperty, value);
    }

    public ICommand PickFoldersCommand
    {
        get => GetValue(PickFoldersCommandProperty);
        set => SetValue(PickFoldersCommandProperty, value);
    }

    public bool ShowFilePicker
    {
        get => GetValue(ShowFilePickerProperty);
        set => SetValue(ShowFilePickerProperty, value);
    }

    public bool ShowFolderPicker
    {
        get => GetValue(ShowFolderPickerProperty);
        set => SetValue(ShowFolderPickerProperty, value);
    }

    public bool ShowEmptyHint
    {
        get => GetValue(ShowEmptyHintProperty);
        private set => SetValue(ShowEmptyHintProperty, value);
    }

    public string TextValue
    {
        get => GetValue(TextValueProperty);
        set => SetValue(TextValueProperty, value);
    }

    //AddCommand执行方法
    private void OnAdd(string? obj)
    {
        if (string.IsNullOrWhiteSpace(obj)) return;

        if (ItemsSource is IList list)
        {
            list.Add(obj);
            TextValue = "";
            UpdateEmptyHint();
        }
    }

    private void InnerOnAdd(string? obj)
    {
        if (string.IsNullOrWhiteSpace(obj)) return;

        AddCommand.Execute(obj);
        TextValue = "";
    }

    //DelCommand执行方法
    private void OnDel(string? obj)
    {
        if (obj == null) return;

        if (ItemsSource is IList list)
        {
            list.Remove(obj);
            UpdateEmptyHint();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ItemsSourceProperty)
        {
            return;
        }

        if (change.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= OnItemsCollectionChanged;
        }

        if (change.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += OnItemsCollectionChanged;
        }

        UpdateEmptyHint();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyHint();

    private void UpdateEmptyHint() => ShowEmptyHint = (ItemsSource as IEnumerable)?.Cast<object>().Any() != true;
}
