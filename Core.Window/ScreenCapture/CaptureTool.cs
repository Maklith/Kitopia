using Core.Services;
using OpenCvSharp;
using PluginCore;
using Serilog;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Core.Window;

public static class CaptureTool
{
    private static ILogger Log = LogManager.Logger.ForContext("SourceContext", typeof(CaptureTool));

    public static unsafe Mat GetMat(MappedSubresource mappedSubresource, OutputDesc1 outputDesc,
        ref ScreenCaptureInfo screenCaptureInfo)
    {
        int startX = Math.Clamp(screenCaptureInfo.X, 0, screenCaptureInfo.ScreenInfo.Width - 1);
        int startY = Math.Clamp(screenCaptureInfo.Y, 0, screenCaptureInfo.ScreenInfo.Height - 1);
        int endX = Math.Clamp(screenCaptureInfo.X + screenCaptureInfo.Width, 0, screenCaptureInfo.ScreenInfo.Width);
        int endY = Math.Clamp(screenCaptureInfo.Y + screenCaptureInfo.Height, 0, screenCaptureInfo.ScreenInfo.Height);
        Mat mat = new Mat((int)(mappedSubresource.DepthPitch / mappedSubresource.RowPitch),
            (int)(mappedSubresource.RowPitch / 4), MatType.CV_8UC4);
        Buffer.MemoryCopy(mappedSubresource.PData, mat.DataPointer,
            mappedSubresource.DepthPitch,
            mappedSubresource.DepthPitch);
        Cv2.CvtColor(mat, mat, ColorConversionCodes.RGBA2BGRA);
        if (screenCaptureInfo.ScreenCaptureType != ScreenCaptureType.窗口)
        {
            var mat1 = mat[startY, endY, startX, endX];
            mat.Dispose();
            return mat1;
        }

        return mat;
    }
}