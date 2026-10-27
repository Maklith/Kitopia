using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ICommand = System.Windows.Input.ICommand;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Kitopia.Desktop.Features.Search;
using PluginCore;

namespace Kitopia.Desktop.Controls;

public partial class EverythingFileListControl : UserControl
{
    // Sort descriptions drive header indicators; incremental inserts must retain Everything's order too.
    private static readonly IComparer ServerOrderComparer = Comparer<object>.Create(static (_, _) => 0);

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<EverythingFileListControl, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<SearchViewItem?> SelectedItemProperty =
        AvaloniaProperty.Register<EverythingFileListControl, SearchViewItem?>(
            nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> OpenFileCommandProperty =
        AvaloniaProperty.Register<EverythingFileListControl, ICommand?>(nameof(OpenFileCommand));

    public static readonly StyledProperty<ICommand?> RunAsAdminCommandProperty =
        AvaloniaProperty.Register<EverythingFileListControl, ICommand?>(nameof(RunAsAdminCommand));

    public static readonly StyledProperty<ICommand?> OpenFolderCommandProperty =
        AvaloniaProperty.Register<EverythingFileListControl, ICommand?>(nameof(OpenFolderCommand));

    public static readonly StyledProperty<ICommand?> OpenFolderInTerminalCommandProperty =
        AvaloniaProperty.Register<EverythingFileListControl, ICommand?>(nameof(OpenFolderInTerminalCommand));

    public static readonly StyledProperty<ICommand?> PinCommandProperty =
        AvaloniaProperty.Register<EverythingFileListControl, ICommand?>(nameof(PinCommand));

    public static readonly StyledProperty<ICommand?> StarCommandProperty =
        AvaloniaProperty.Register<EverythingFileListControl, ICommand?>(nameof(StarCommand));

    public static readonly StyledProperty<ICommand?> IgnoreItemCommandProperty =
        AvaloniaProperty.Register<EverythingFileListControl, ICommand?>(nameof(IgnoreItemCommand));

    public static readonly StyledProperty<ICommand?> LoadMoreCommandProperty =
        AvaloniaProperty.Register<EverythingFileListControl, ICommand?>(nameof(LoadMoreCommand));

    public static readonly StyledProperty<bool> ShowStatusBarProperty =
        AvaloniaProperty.Register<EverythingFileListControl, bool>(nameof(ShowStatusBar), defaultValue: false);

    public static readonly StyledProperty<string> StatusTextProperty =
        AvaloniaProperty.Register<EverythingFileListControl, string>(nameof(StatusText), defaultValue: string.Empty);

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public SearchViewItem? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public ICommand? OpenFileCommand
    {
        get => GetValue(OpenFileCommandProperty);
        set => SetValue(OpenFileCommandProperty, value);
    }

    public ICommand? RunAsAdminCommand
    {
        get => GetValue(RunAsAdminCommandProperty);
        set => SetValue(RunAsAdminCommandProperty, value);
    }

    public ICommand? OpenFolderCommand
    {
        get => GetValue(OpenFolderCommandProperty);
        set => SetValue(OpenFolderCommandProperty, value);
    }

    public ICommand? OpenFolderInTerminalCommand
    {
        get => GetValue(OpenFolderInTerminalCommandProperty);
        set => SetValue(OpenFolderInTerminalCommandProperty, value);
    }

    public ICommand? PinCommand
    {
        get => GetValue(PinCommandProperty);
        set => SetValue(PinCommandProperty, value);
    }

    public ICommand? StarCommand
    {
        get => GetValue(StarCommandProperty);
        set => SetValue(StarCommandProperty, value);
    }

    public ICommand? IgnoreItemCommand
    {
        get => GetValue(IgnoreItemCommandProperty);
        set => SetValue(IgnoreItemCommandProperty, value);
    }

    public ICommand? LoadMoreCommand
    {
        get => GetValue(LoadMoreCommandProperty);
        set => SetValue(LoadMoreCommandProperty, value);
    }

    public bool ShowStatusBar
    {
        get => GetValue(ShowStatusBarProperty);
        set => SetValue(ShowStatusBarProperty, value);
    }

    public string StatusText
    {
        get => GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public event EventHandler<SearchViewItem>? ItemActivated;
    public event EventHandler? NavigateUpRequested;
    public event EventHandler? LoadMoreRequested;
    public event EventHandler<EverythingSortOrder>? SortChanged;
    public event EventHandler<KeyEventArgs>? KeyDownRedirected;
    public event EventHandler<TextInputEventArgs>? TextInputRedirected;

    public EverythingFileListControl()
    {
        InitializeComponent();

        FileListGrid.AddHandler(PointerPressedEvent, (sender, e) =>
        {
            var point = e.GetCurrentPoint(FileListGrid);
            if (point.Properties.IsRightButtonPressed)
            {
                var row = (e.Source as Visual)?.FindAncestorOfType<DataGridRow>();
                if (row?.DataContext is SearchViewItem item)
                {
                    FileListGrid.SelectedItem = item;
                }
            }
        }, RoutingStrategies.Tunnel);

        FileListGrid.AddHandler(TextInputEvent, (sender, e) =>
        {
            TextInputRedirected?.Invoke(this, e);
        }, RoutingStrategies.Tunnel);

        FileListGrid.AddHandler(KeyDownEvent, FileListGrid_OnKeyDown, RoutingStrategies.Tunnel);
        FileListGrid.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<ScrollBar>("PART_VerticalScrollbar") is { } scrollBar)
            {
                scrollBar.ValueChanged += FileListGrid_OnScrollValueChanged;
            }
        };
        FileListGrid.Sorting += FileListGrid_OnSorting;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            var sort = FileListGrid.CollectionView?.SortDescriptions.FirstOrDefault() ??
                       DataGridSortDescription.FromPath(nameof(SearchViewItem.ItemDisplayName),
                           ListSortDirection.Ascending, ServerOrderComparer);
            // Everything owns the result order; the view only maintains the header sort indicator.
            var view = new DataGridCollectionView(ItemsSource ?? Array.Empty<SearchViewItem>(),
                isDataSorted: true, isDataInGroupOrder: false);
            view.SortDescriptions.Add(sort);
            FileListGrid.ItemsSource = view;
        }
    }

    public void FocusFirstItem()
    {
        if (FileListGrid.ItemsSource is IEnumerable items && items.Cast<object>().Any())
        {
            FileListGrid.SelectedIndex = 0;
            FileListGrid.ScrollIntoView(FileListGrid.SelectedItem, null);
            FileListGrid.Focus();
        }
    }

    public void ClearSelection()
    {
        FileListGrid.SelectedIndex = -1;
    }

    private void FileListGrid_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedItem is { } item)
        {
            OpenFileCommand?.Execute(item);
            ItemActivated?.Invoke(this, item);
            e.Handled = true;
        }
    }

    private void FileListGrid_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (SelectedItem is { } item)
            {
                OpenFileCommand?.Execute(item);
                ItemActivated?.Invoke(this, item);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Up && FileListGrid.SelectedIndex <= 0 && e.KeyModifiers == KeyModifiers.None)
        {
            NavigateUpRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift))
        {
            if (FileListGrid.ContextMenu != null && SelectedItem != null)
            {
                FileListGrid.ContextMenu.Open(FileListGrid);
                e.Handled = true;
                return;
            }
        }

        if (e.Key is Key.Down or Key.PageDown)
        {
            var count = GetItemCount();
            if (count > 0 && FileListGrid.SelectedIndex >= count - 5)
            {
                TriggerLoadMore();
            }
        }

        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.Tab
            or Key.PageDown or Key.PageUp or Key.Escape or Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            or >= Key.F1 and <= Key.F24)
        {
            return;
        }

        // Forward other keyboard input to external search box
        KeyDownRedirected?.Invoke(this, e);
    }

    private void FileListGrid_OnScrollValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is ScrollBar scrollBar && e.NewValue > e.OldValue &&
            scrollBar.Maximum - e.NewValue <= 200)
        {
            TriggerLoadMore();
        }
    }

    private void FileListGrid_OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;
        var sorts = FileListGrid.CollectionView.SortDescriptions;
        var previous = sorts.FirstOrDefault();
        var descending = previous?.PropertyPath == e.Column.SortMemberPath &&
                         previous.Direction == ListSortDirection.Ascending;
        var sortOrder = e.Column.SortMemberPath switch
        {
            nameof(SearchViewItem.ItemDisplayName) => descending ? EverythingSortOrder.NameDescending : EverythingSortOrder.NameAscending,
            nameof(SearchViewItem.StartDirectory) => descending ? EverythingSortOrder.PathDescending : EverythingSortOrder.PathAscending,
            nameof(SearchViewItem.FileSize) => descending ? EverythingSortOrder.SizeDescending : EverythingSortOrder.SizeAscending,
            nameof(SearchViewItem.DateModified) => descending ? EverythingSortOrder.DateModifiedDescending : EverythingSortOrder.DateModifiedAscending,
            _ => (EverythingSortOrder?)null
        };
        if (sortOrder == null) return;

        using (FileListGrid.CollectionView.DeferRefresh())
        {
            sorts.Clear();
            sorts.Add(DataGridSortDescription.FromPath(e.Column.SortMemberPath,
                descending ? ListSortDirection.Descending : ListSortDirection.Ascending, ServerOrderComparer));
        }
        SortChanged?.Invoke(this, sortOrder.Value);
    }

    private void TriggerLoadMore()
    {
        if (LoadMoreCommand != null)
        {
            if (LoadMoreCommand.CanExecute(null))
            {
                LoadMoreCommand.Execute(null);
                LoadMoreRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            LoadMoreRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private int GetItemCount()
    {
        if (FileListGrid.ItemsSource is ICollection col) return col.Count;
        if (FileListGrid.ItemsSource is IEnumerable en) return en.Cast<object>().Count();
        return 0;
    }

    private async void CopyPathMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem is { } item && !string.IsNullOrEmpty(item.OnlyKey))
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(item.OnlyKey);
            }
        }
    }
}
