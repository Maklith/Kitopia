using System;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace KitopiaAvalonia.Controls;

public class MosaicIcon : Control
{
    private Timer _timer;

    public MosaicIcon()
    {
        _timer = new Timer(TimeSpan.FromSeconds(1));
        _timer.AutoReset = true;
        _timer.Elapsed += Tick;
    }

    private void Tick(object? sender, EventArgs e)
    {
        // 每次计时器触发时，执行无效化
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }


    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _timer.Stop();
        _timer.Elapsed -= Tick;
        _timer.Dispose();
        base.OnUnloaded(e);
    }

    private IBrush? _brush;

    public override void Render(DrawingContext context)
    {
        // 设置绘制马赛克的颜色
        //var brush = new SolidColorBrush(Color.Parse("#0078D7"));
        if (_brush is null)
        {
            Application.Current!.Styles.TryGetResource("SemiColorText0", null, out var brush);
            _brush = (IBrush?)brush;
        }

        var pen = new Pen(_brush, 2d);

        // 计算每个像素方块的大小
        var blockSize = Math.Min(Bounds.Width, Bounds.Height) / 2; // 假设图标由8x8的格子组成

        // 绘制像素方块
        for (var x = 0; x < 2; x++)
        for (var y = 0; y < 2; y++)
            // 简化的示例：我们随机地绘制方块来模拟马赛克效果
            if (new Random().Next(2) == 0)
            {
                var rect = new Rect(x * blockSize, y * blockSize, blockSize, blockSize);
                // 填充方块
                context.FillRectangle(_brush, rect);
                // 绘制方块边框
                context.DrawRectangle(pen, rect);
            }
    }
}