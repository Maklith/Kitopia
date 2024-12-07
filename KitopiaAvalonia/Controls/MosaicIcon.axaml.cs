using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace KitopiaAvalonia.Controls;

public class MosaicIcon : Control
{
    private readonly DispatcherTimer _timer;

    public MosaicIcon()
    {
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Render, Tick);
    }

    private void Tick(object? sender, EventArgs e)
    {
        // 每次计时器触发时，执行无效化
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // 设置绘制马赛克的颜色
        //var brush = new SolidColorBrush(Color.Parse("#0078D7"));
        Application.Current.Styles.TryGetResource("SemiColorText0", null, out var brush);
        var pen = new Pen((IBrush?)brush, 2d);

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
                context.FillRectangle((IBrush)brush, rect);
                // 绘制方块边框
                context.DrawRectangle(pen, rect);
            }
    }
}