using System.Drawing;
using System.Drawing.Imaging;
using Avalonia;
using Avalonia.Platform;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Kitopia.Desktop.Features.Utils;

public static class DrawingBitmapToAvaloniaEx
{
    public static Bitmap ToAvaloniaBitmap(this System.Drawing.Bitmap bitmapTmp)
    {
        var bitmapdata = bitmapTmp.LockBits(new Rectangle(0, 0, bitmapTmp.Width, bitmapTmp.Height),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var bitmap1 = new Bitmap(Avalonia.Platform.PixelFormat.Bgra8888,
            AlphaFormat.Unpremul,
            bitmapdata.Scan0,
            new PixelSize(bitmapdata.Width, bitmapdata.Height),
            new Vector(96, 96),
            bitmapdata.Stride);
        bitmapTmp.UnlockBits(bitmapdata);
        bitmapTmp.Dispose();
        return bitmap1;
    }
}