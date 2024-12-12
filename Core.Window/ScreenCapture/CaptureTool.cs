using PluginCore;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Core.Window;

public static class CaptureTool
{
    static float LogNormalize(float value, float maxHDR, float k = 1)
    {
        if (value < 0) value = 0;
        return (float)(Math.Log(1 + k * value) / Math.Log(1 + k * maxHDR));
    }
    public static unsafe byte[] GetBytesSpan(MappedSubresource mappedSubresource, OutputDesc1 outputDesc,ScreenCaptureInfo screenCaptureInfo)
    {
        int startX = Math.Clamp(screenCaptureInfo.X, 0, outputDesc.DesktopCoordinates.Size.X - 1);
        int startY = Math.Clamp(screenCaptureInfo.Y, 0, outputDesc.DesktopCoordinates.Size.Y - 1);
        int endX = Math.Clamp(screenCaptureInfo.X + screenCaptureInfo.Width, 0, outputDesc.DesktopCoordinates.Size.X);
        int endY = Math.Clamp(screenCaptureInfo.Y + screenCaptureInfo.Height, 0, outputDesc.DesktopCoordinates.Size.Y);

        // 结果数组：区域宽 * 区域高 * 4（RGBA）
        int regionWidth = endX - startX;
        int regionHeight = endY - startY;
        byte[] result = new byte[regionWidth * regionHeight * 4];

        
        
        if (!outputDesc.ColorSpace.ToString().EndsWith("2020"))
        {
            var span = new ReadOnlySpan<uint>(mappedSubresource.PData,
                (int)mappedSubresource.DepthPitch / 4);
            
            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    int sourceIndex = (y *  outputDesc.DesktopCoordinates.Size.X + x) * 4;
                    int targetIndex = ((y - startY) * regionWidth + (x - startX)) * 4;

                    // 读取原始像素并复制到结果
                    uint value = span[sourceIndex / 4];
                    result[targetIndex+ 2] = (byte)(value & 0xFF);        // R
                    result[targetIndex + 1] = (byte)((value >> 8) & 0xFF); // G
                    result[targetIndex ] = (byte)((value >> 16) & 0xFF); // B
                    result[targetIndex + 3] = (byte)((value >> 24) & 0xFF); // A
                }
            }
        }
        else
        {
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
                    .640f, .330f, .300f, .600f, .150f, .060f, outputDesc.WhitePoint[0],
                    outputDesc.WhitePoint[1]
                ]
            );
            var span = new ReadOnlySpan<Half>(mappedSubresource.PData,
                (int)mappedSubresource.DepthPitch / 2);

           

            var maxHdr = 4.75f;
            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    int sourceIndex = (y *  outputDesc.DesktopCoordinates.Size.X + x) * 4;
                    int targetIndex = ((y - startY) * regionWidth + (x - startX)) * 4;

                    // 读取并归一化 RGBA 值
                    float r = LogNormalize((float)span[sourceIndex], maxHdr);
                    float g = LogNormalize((float)span[sourceIndex + 1], maxHdr);
                    float b = LogNormalize((float)span[sourceIndex + 2], maxHdr);
                    float a = LogNormalize((float)span[sourceIndex + 3], maxHdr);

                    // 应用色彩转换矩阵
                    float bt2020R = matrix[0, 0] * r + matrix[0, 1] * g + matrix[0, 2] * b;
                    float bt2020G = matrix[1, 0] * r + matrix[1, 1] * g + matrix[1, 2] * b;
                    float bt2020B = matrix[2, 0] * r + matrix[2, 1] * g + matrix[2, 2] * b;

                    // 转换并填充结果
                    result[targetIndex+ 2] = (byte)Math.Clamp(bt2020R * 255, 0, 255);
                    result[targetIndex + 1] = (byte)Math.Clamp(bt2020G * 255, 0, 255);
                    result[targetIndex ] = (byte)Math.Clamp(bt2020B * 255, 0, 255);
                    result[targetIndex + 3] = 255; // Alpha 固定为 255
                }
            }
        }

        return result;
    }
}