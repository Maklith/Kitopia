using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace KitopiaAvalonia.Controls;

public class ListShow : ListBox
{
    //ObservableCollection

    //DelCommand
    public static readonly StyledProperty<ICommand> DelCommandProperty =
        AvaloniaProperty.Register<ListShow, ICommand>(nameof(DelCommand));

    public static readonly StyledProperty<bool> WithAddProperty =
        AvaloniaProperty.Register<ListShow, bool>(nameof(WithAdd), false);

    public static readonly StyledProperty<ICommand> AddCommandProperty =
        AvaloniaProperty.Register<ListShow, ICommand>(nameof(AddCommand));

    public static readonly StyledProperty<ICommand> InnerAddCommandProperty =
        AvaloniaProperty.Register<ListShow, ICommand>(nameof(AddCommand));

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

        if (ItemsSource is IList list) list.Remove(obj);
    }
}