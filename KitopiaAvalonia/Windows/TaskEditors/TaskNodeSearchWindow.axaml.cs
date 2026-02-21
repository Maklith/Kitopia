using Avalonia.Controls;
using Core.ViewModel.TaskEditor;
using System;

namespace KitopiaAvalonia.Windows.TaskEditors;

public partial class TaskNodeSearchWindow : Window
{
    public TaskNodeSearchWindow()
    {
        InitializeComponent();
        this.Opened += (s, e) => SearchBox?.Focus();
        this.Deactivated += (s, e) => Close();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TaskNodeSearchViewModel vm)
        {
             vm.CloseAction = Close;
        }
    }
}
