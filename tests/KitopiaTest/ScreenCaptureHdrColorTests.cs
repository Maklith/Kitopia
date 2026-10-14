using Kitopia.Desktop.Platform.Windows.ScreenCapture;
using OpenCvSharp;
using PluginCore;
using Silk.NET.DXGI;
using Silk.NET.Direct3D11;
using CaptureRect = PluginCore.Rect;

namespace KitopiaTest;

[TestClass]
public sealed class ScreenCaptureHdrColorTests
{
    [TestMethod]
    public unsafe void ConvertSubresourceToSdrMat_HdrWhite_RemainsWhite()
    {
        var pixels = new Half[] { (Half)1, (Half)1, (Half)1, (Half)1 };
        var outputDesc = new OutputDesc1
        {
            ColorSpace = ColorSpaceType.RgbFullG2084NoneP2020
        };
        outputDesc.RedPrimary[0] = 0.708f;
        outputDesc.RedPrimary[1] = 0.292f;
        outputDesc.GreenPrimary[0] = 0.170f;
        outputDesc.GreenPrimary[1] = 0.797f;
        outputDesc.BluePrimary[0] = 0.131f;
        outputDesc.BluePrimary[1] = 0.046f;
        outputDesc.WhitePoint[0] = 0.3127f;
        outputDesc.WhitePoint[1] = 0.329f;

        var captureInfo = new ScreenCaptureInfo
        {
            ScreenCaptureType = ScreenCaptureType.屏幕,
            RequestRect = new CaptureRect(0, 0, 1, 1),
            ScreenInfo = new CaptureRect(0, 0, 1, 1),
            SdrWhiteLevelScale = 1f
        };

        fixed (Half* pixel = pixels)
        {
            var mapped = new MappedSubresource
            {
                PData = pixel,
                RowPitch = 8,
                DepthPitch = 8
            };

            using var result = ScreenCaptureByWgc.ConvertSubresourceToSdrMat(mapped, outputDesc, ref captureInfo);

            Assert.IsNotNull(result);
            var value = result!.At<Vec4b>(0, 0);
            Assert.IsTrue(value[0] >= 254 && value[1] >= 254 && value[2] >= 254 && value[3] >= 254,
                $"Expected white BGRA pixel, got {value[0]},{value[1]},{value[2]},{value[3]}.");
        }
    }
}
