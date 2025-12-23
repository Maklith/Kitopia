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
        if (!outputDesc.ColorSpace.ToString().EndsWith("2020"))
        {
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
        else
        {
            var mat = new Mat((int)(mappedSubresource.DepthPitch / mappedSubresource.RowPitch),
                (int)(mappedSubresource.RowPitch / 8), MatType.MakeType(7, 4));
            Buffer.MemoryCopy((void*)mappedSubresource.PData, mat.DataPointer,
                mappedSubresource.DepthPitch, mappedSubresource.DepthPitch);

            mat.ConvertTo(mat, MatType.CV_32FC4);
            //var vec4F = mat.Get<Vec4f>(2);
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGBA2RGB);
            var matrix = ColorSpaceCtr.CtrColorSpace([
                    outputDesc.RedPrimary[0],
                    outputDesc.RedPrimary[1],
                    outputDesc.GreenPrimary[0],
                    outputDesc.GreenPrimary[1],
                    outputDesc.BluePrimary[0],
                    outputDesc.BluePrimary[1],
                    outputDesc.WhitePoint[0],
                    outputDesc.WhitePoint[1]
                ],
                [
                    .640f, .330f, .300f, .600f, .150f, .060f, .3127f, .3290f
                ]
            );
            Cv2.Transform(mat, mat, Mat.FromArray(matrix));
            
            // 移除自动曝光逻辑，直接使用Gamma矫正和裁剪
            // 1.0 (SDR White) -> 1.0 (Output White)
            // >1.0 (HDR Highlights) -> Clipped to 255
            Cv2.Pow(mat, 1.0 / 2.2, mat);
            mat *= 255.0;
            
            mat.ConvertTo(mat, MatType.CV_8UC4);
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGB2BGRA);
            if (screenCaptureInfo.ScreenCaptureType != ScreenCaptureType.窗口)
            {
                var mat1 = mat[startY, endY, startX, endX];
                mat.Dispose();
                return mat1;
            }

            return mat;
        }


        return new Mat();
    }

    private static void ApplySrgbGamma(Mat linear, Mat output)
    {
        // 精确sRGB OETF转换
        linear.ConvertTo(linear, MatType.CV_32FC3);
        output.Create(linear.Size(), MatType.CV_32FC3);

        for (int y = 0; y < linear.Rows; y++)
        {
            for (int x = 0; x < linear.Cols; x++)
            {
                Vec3f v = linear.At<Vec3f>(y, x);
                for (int c = 0; c < 3; c++)
                {
                    float val = v[c];
                    if (val <= 0.0031308f)
                        v[c] = 12.92f * val;
                    else
                        v[c] = 1.055f * MathF.Pow(val, 1.0f / 2.4f) - 0.055f;
                }

                output.Set(y, x, v);
            }
        }
    }
}