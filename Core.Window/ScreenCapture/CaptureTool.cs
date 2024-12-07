using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Core.Window;

public static class CaptureTool
{
    public static unsafe byte[] GetBytesSpan(MappedSubresource mappedSubresource, OutputDesc1 outputDesc)
    {
        var re = new byte[(int)mappedSubresource.DepthPitch * 4];

        if (!outputDesc.ColorSpace.ToString().EndsWith("2020"))
        {
            var span = new ReadOnlySpan<uint>(mappedSubresource.PData,
                (int)mappedSubresource.DepthPitch / 4);

            var index = 0;
            foreach (var value in span)
            {
                re[index * 4] = (byte)(value & 0xFF);
                re[index * 4 + 1] = (byte)((value >> 8) & 0xFF);
                re[index * 4 + 2] = (byte)((value >> 16) & 0xFF);
                re[index * 4 + 3] = (byte)((value >> 24) & 0xFF);
                index++;
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

            float LogNormalize(float value, float maxHDR, float k = 1)
            {
                if (value < 0) value = 0;
                return (float)(Math.Log(1 + k * value) / Math.Log(1 + k * maxHDR));
            }

            var maxHdr = 4.75f;
            for (var index = 0; index < span.Length / 4 - 1;)
            {
                var r = LogNormalize((float)span[index * 4], maxHdr); // 获取最低的16位
                var g = LogNormalize((float)span[index * 4 + 1], maxHdr);
                var b = LogNormalize((float)span[index * 4 + 2], maxHdr);
                var a = LogNormalize((float)span[index * 4 + 3], maxHdr);
                var bt2020R = matrix[0, 0] * r + matrix[0, 1] * g +
                              matrix[0, 2] * b;
                var bt2020G = matrix[1, 0] * r + matrix[1, 1] * g +
                              matrix[1, 2] * b;
                var bt2020B = matrix[2, 0] * r + matrix[2, 1] * g +
                              matrix[2, 2] * b;
                bt2020R = Math.Clamp(bt2020R * 255, 0, 255);
                bt2020G = Math.Clamp(bt2020G * 255, 0, 255);
                bt2020B = Math.Clamp(bt2020B * 255, 0, 255);
                re[index * 4] = (byte)bt2020R;
                re[index * 4 + 1] = (byte)bt2020G;
                re[index * 4 + 2] = (byte)bt2020B;
                re[index * 4 + 3] = 255;
                index++;
            }
        }

        return re;
    }
}