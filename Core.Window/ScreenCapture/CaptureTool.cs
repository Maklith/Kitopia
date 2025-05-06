using System.Runtime.InteropServices;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using OpenCvSharp;
using PluginCore;
using Serilog;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Core.Window;

public static class CaptureTool
{
    private static ILogger Log =   LogManager.Logger.ForContext("SourceContext",typeof(CaptureTool));
    public static unsafe Mat GetMat(MappedSubresource mappedSubresource, OutputDesc1 outputDesc,ref ScreenCaptureInfo screenCaptureInfo)
    {
        var sizeX = outputDesc.DesktopCoordinates.Size.X;
        int startX = Math.Clamp(screenCaptureInfo.X, 0, outputDesc.DesktopCoordinates.Size.X - 1);
        int startY = Math.Clamp(screenCaptureInfo.Y, 0, outputDesc.DesktopCoordinates.Size.Y - 1);
        int endX = Math.Clamp(screenCaptureInfo.X+screenCaptureInfo.Width, 0, outputDesc.DesktopCoordinates.Size.X);
        int endY = Math.Clamp(screenCaptureInfo.Y+screenCaptureInfo.Height, 0, outputDesc.DesktopCoordinates.Size.Y);
        if (screenCaptureInfo.ScreenCaptureType==ScreenCaptureType.窗口)
        {
            sizeX=(screenCaptureInfo.WindowInfo.Rect.Width + 3) & ~3;
            startX = 0;
            startY = 0;
            endX =  (screenCaptureInfo.WindowInfo.Rect.Width + 3) & ~3;
            endY = (int)screenCaptureInfo.WindowInfo.Rect.Height;
        }
        
        screenCaptureInfo.Height = endY - startY;
        screenCaptureInfo.Width = endX - startX;
        screenCaptureInfo.X = startX;   
        screenCaptureInfo.Y = startY;
        // 结果数组：区域宽 * 区域高 * 4（RGBA）
        int regionWidth = endX - startX;
        int regionHeight = endY - startY;
        
        
       
        if (!outputDesc.ColorSpace.ToString().EndsWith("2020"))
        {
            Mat mat=new Mat(endY, endX, MatType.CV_8UC4);
            Buffer.MemoryCopy((void*)mappedSubresource.PData,mat.DataPointer,
                endY * endX * 4, endX * endY * 4);
            Cv2.CvtColor(mat,mat,ColorConversionCodes.RGBA2BGRA);
            var mat1 = mat[startY,endY,startX,endX];
            mat.Dispose();
            return mat1;
        }
        else
        {
            var mat = new Mat(outputDesc.DesktopCoordinates.Size.Y, outputDesc.DesktopCoordinates.Size.X,MatType.MakeType(7,4));
            var destinationSizeInBytes = outputDesc.DesktopCoordinates.Size.Y* outputDesc.DesktopCoordinates.Size.X * 8;
            Buffer.MemoryCopy((void*)mappedSubresource.PData,mat.DataPointer,
                destinationSizeInBytes, destinationSizeInBytes);
            
            mat.ConvertTo(mat, MatType.CV_32FC4);
            //var vec4F = mat.Get<Vec4f>(2);
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGBA2RGB);
            mat = mat[startY,endY,startX,endX];
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
            Cv2.Log(new Scalar(1,1,1)+mat, mat);
            mat/=1.749199854809259f;
            //Cv2.Normalize(mat, mat, 0, 1, NormTypes.MinMax);
            mat *= 255;
            mat.ConvertTo(mat, MatType.CV_8UC3);
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGB2BGRA);
            return mat;

        }

        return new Mat();
    }
}