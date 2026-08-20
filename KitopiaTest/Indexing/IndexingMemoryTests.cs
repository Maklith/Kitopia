using System.Buffers.Binary;
using Kitopia.Desktop.Features.Imaging;
using Kitopia.Desktop.Features.Ocr;
using OpenCvSharp;

namespace KitopiaTest.Indexing;

[TestClass]
public sealed class IndexingMemoryTests
{
    [TestMethod]
    public void ResizeToMaximumPixels_ReturnsNoCopyWhenAlreadyBounded()
    {
        using var source = new Mat(100, 100, MatType.CV_8UC3);

        using var resized = ImageInputLoader.ResizeToMaximumPixels(source, 10_000);

        Assert.IsNull(resized);
    }

    [TestMethod]
    public void ResizeToMaximumPixels_StaysWithinPixelBudget()
    {
        using var source = new Mat(2_000, 3_000, MatType.CV_8UC3);

        using var resized = ImageInputLoader.ResizeToMaximumPixels(source, 1_000_000);

        Assert.IsNotNull(resized);
        Assert.IsTrue((long)resized.Rows * resized.Cols <= 1_000_000);
    }

    [TestMethod]
    public void TryReadDimensions_ReadsPngHeaderWithoutDecodingPixels()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-image-header-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, CreatePngHeader(12_345, 6_789));

            Assert.IsTrue(ImageInputLoader.TryReadDimensions(path, out var size));
            Assert.AreEqual(12_345, size.Width);
            Assert.AreEqual(6_789, size.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadBgr_RejectsOversizedPngBeforeOpenCvDecode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-image-limit-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, CreatePngHeader(100_000, 100_000));

            Assert.ThrowsExactly<InvalidDataException>(() =>
                ImageInputLoader.LoadBgr(path, ImageInputLoader.MaximumOcrPixels));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadBgr_RejectsOversizedPngWithJpegExtensionBeforeOpenCvDecode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-image-limit-{Guid.NewGuid():N}.jpg");
        try
        {
            File.WriteAllBytes(path, CreatePngHeader(100_000, 100_000));

            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                ImageInputLoader.LoadBgr(path, ImageInputLoader.MaximumEmbeddingPixels));
            StringAssert.Contains(exception.Message, "too large");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadBgr_RejectsOversizedJpegBeforeOpenCvDecode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-image-limit-{Guid.NewGuid():N}.jpg");
        try
        {
            File.WriteAllBytes(path, CreateJpegHeader(65_535, 65_535));

            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                ImageInputLoader.LoadBgr(path, ImageInputLoader.MaximumOcrPixels));
            StringAssert.Contains(exception.Message, "bounded decoding");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void PrepareRecognitionImage_BoundsExtremeAspectRatio()
    {
        using var source = new Mat(1, 1_000_000, MatType.CV_8UC3);

        using var prepared = PaddleOcrService.PrepareRecognitionImage(source);

        Assert.AreEqual(48, prepared.Rows);
        Assert.IsLessThanOrEqualTo(2048, prepared.Cols);
        Assert.AreEqual(0, prepared.Cols % 32);
    }

    [TestMethod]
    public void ScaleBounds_MapsDetectionBoundsBackToTheOriginalImage()
    {
        var scaled = PaddleOcrService.ScaleBounds(
            new Rect(100, 50, 200, 100), 500, 250, 4d, 4d, 2_000, 1_000);

        Assert.AreEqual(new Rect(400, 200, 800, 400), scaled);
    }

    [TestMethod]
    public void ScaleBounds_ExcludesDetectorPadding()
    {
        var scaled = PaddleOcrService.ScaleBounds(
            new Rect(490, 490, 32, 32), 500, 500, 4d, 4d, 2_000, 2_000);

        Assert.AreEqual(new Rect(1_960, 1_960, 40, 40), scaled);
    }

    private static byte[] CreatePngHeader(uint width, uint height)
    {
        var header = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(header, 0);
        "IHDR"u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), height);
        return header;
    }

    private static byte[] CreateJpegHeader(ushort width, ushort height) =>
    [
        0xFF, 0xD8,
        0xFF, 0xC0, 0x00, 0x11, 0x08,
        (byte)(height >> 8), (byte)height,
        (byte)(width >> 8), (byte)width,
        0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00
    ];
}
