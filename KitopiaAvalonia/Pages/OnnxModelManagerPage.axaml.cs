#region

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Core.ViewModel.Pages;

#endregion

namespace KitopiaAvalonia.Pages;

public partial class OnnxModelManagerPage : UserControl
{
    public OnnxModelManagerPage()
    {
        InitializeComponent();
    }

    private void HorizonScroll(object? sender, PointerWheelEventArgs pointerWheelEventArgs)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            // 获取滚轮滚动的增量值
            var delta = pointerWheelEventArgs.Delta.Y;

            // 调整滚动条的横向偏移
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X - delta * 20, scrollViewer.Offset.Y);

            // 标记事件为已处理，防止默认的垂直滚动
            pointerWheelEventArgs.Handled = true;
        }
    }

    private void R_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radioButton)
            if (radioButton.DataContext is OnnxModelRuntimeChangerHelper onnxModelRuntimeChangerHelper)
            {
                onnxModelRuntimeChangerHelper.CurrentDevice = onnxModelRuntimeChangerHelper.TargetDevice;
                var itemCollection = ((ItemsControl)radioButton.Parent.Parent).Items;
                foreach (var o in itemCollection)
                    if (o is OnnxModelRuntimeChangerHelper helper)
                        helper.CurrentDevice = onnxModelRuntimeChangerHelper.TargetDevice;
            }
    }
}