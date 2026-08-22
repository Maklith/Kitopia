using System.Buffers.Binary;
using Kitopia.Desktop.Features.Imaging;
using Kitopia.Desktop.Features.Ocr;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using System.Runtime.InteropServices;

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
    public void TryReadDimensions_ReadsGifHeaderWithoutDecodingPixels()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-image-header-{Guid.NewGuid():N}.gif");
        try
        {
            File.WriteAllBytes(path, CreateGifHeader(320, 240));

            Assert.IsTrue(ImageInputLoader.TryReadDimensions(path, out var size));
            Assert.AreEqual(320, size.Width);
            Assert.AreEqual(240, size.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadBgr_DecodesGifFirstFrame()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-image-loader-{Guid.NewGuid():N}.gif");
        try
        {
            File.WriteAllBytes(path, CreateGif());

            using var image = ImageInputLoader.LoadBgr(path, ImageInputLoader.MaximumEmbeddingPixels);

            Assert.AreEqual(1, image.Cols);
            Assert.AreEqual(1, image.Rows);
            Assert.AreEqual(3, image.Channels());
            var pixel = new byte[3];
            Marshal.Copy(image.Data, pixel, 0, pixel.Length);
            Assert.AreEqual((byte)255, pixel[0]);
            Assert.AreEqual((byte)255, pixel[1]);
            Assert.AreEqual((byte)255, pixel[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TryReadDimensions_ScansJpegMetadataBeyond64KiB()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-image-header-{Guid.NewGuid():N}.jpg");
        try
        {
            File.WriteAllBytes(path, CreateJpegHeaderWithLargeMetadata(4_096, 3_072));

            Assert.IsTrue(ImageInputLoader.TryReadDimensions(path, out var size));
            Assert.AreEqual(4_096, size.Width);
            Assert.AreEqual(3_072, size.Height);
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
    public void BlobFromImagesWithParams_ProducesRgbNchwClipNormalization()
    {
        const double meanR = 0.48145466d;
        const double meanG = 0.4578275d;
        const double meanB = 0.40821073d;
        const double standardDeviationR = 0.26862954d;
        const double standardDeviationG = 0.26130258d;
        const double standardDeviationB = 0.27577711d;
        using var image = new Mat(1, 1, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var parameters = new Image2BlobParams(
            new Scalar(
                1d / (255d * standardDeviationR),
                1d / (255d * standardDeviationG),
                1d / (255d * standardDeviationB)),
            new Size(1, 1),
            new Scalar(255d * meanR, 255d * meanG, 255d * meanB),
            true,
            MatType.CV_32F,
            DataLayout.NCHW,
            ImagePaddingMode.NULL,
            Scalar.All(0));

        using var blob = Cv2.Dnn.BlobFromImagesWithParams([image], parameters);
        var values = new float[3];
        Marshal.Copy(blob.Data, values, 0, values.Length);

        Assert.AreEqual(MatType.CV_32FC1, blob.Type());
        Assert.AreEqual(3L, blob.Total());
        Assert.AreEqual((30d / 255d - meanR) / standardDeviationR, values[0], 0.00001d);
        Assert.AreEqual((20d / 255d - meanG) / standardDeviationG, values[1], 0.00001d);
        Assert.AreEqual((10d / 255d - meanB) / standardDeviationB, values[2], 0.00001d);
    }

    [TestMethod]
    public void PaddleOcrPreprocessToNchw_ProducesBgrNchwNormalization()
    {
        const double meanB = 0.1d;
        const double meanG = 0.2d;
        const double meanR = 0.3d;
        const double standardDeviationB = 0.4d;
        const double standardDeviationG = 0.5d;
        const double standardDeviationR = 0.6d;
        using var image = new Mat(1, 1, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var values = new float[3];

        PaddleOcrService.PreprocessToNchw(
            image,
            values,
            new Scalar(meanB, meanG, meanR),
            new Scalar(standardDeviationB, standardDeviationG, standardDeviationR));

        Assert.AreEqual((10d / 255d - meanB) / standardDeviationB, values[0], 0.00001d);
        Assert.AreEqual((20d / 255d - meanG) / standardDeviationG, values[1], 0.00001d);
        Assert.AreEqual((30d / 255d - meanR) / standardDeviationR, values[2], 0.00001d);
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

    private static byte[] CreateJpegHeaderWithLargeMetadata(ushort width, ushort height)
    {
        const ushort segmentLength = ushort.MaxValue;
        const int metadataLength = segmentLength - 2;
        var header = new byte[2 + 2 * (4 + metadataLength) + 4 + 15];
        var offset = 0;
        header[offset++] = 0xFF;
        header[offset++] = 0xD8;
        for (var index = 0; index < 2; index++)
        {
            header[offset++] = 0xFF;
            header[offset++] = 0xE1;
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(offset, 2), segmentLength);
            offset += 2 + metadataLength;
        }

        header[offset++] = 0xFF;
        header[offset++] = 0xC0;
        header[offset++] = 0x00;
        header[offset++] = 0x11;
        header[offset++] = 0x08;
        header[offset++] = (byte)(height >> 8);
        header[offset++] = (byte)height;
        header[offset++] = (byte)(width >> 8);
        header[offset++] = (byte)width;
        header[offset++] = 0x03;
        header[offset++] = 0x01;
        header[offset++] = 0x11;
        header[offset++] = 0x00;
        header[offset++] = 0x02;
        header[offset++] = 0x11;
        header[offset++] = 0x00;
        header[offset++] = 0x03;
        header[offset++] = 0x11;
        header[offset] = 0x00;
        return header;
    }

    private static byte[] CreateGifHeader(ushort width, ushort height) =>
    [
        (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
        (byte)width, (byte)(width >> 8),
        (byte)height, (byte)(height >> 8),
        0x00, 0x00, 0x00
    ];

    private static byte[] CreateGif() =>
    [
        (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
        0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
        0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
        // White first frame, followed by a black frame.
        0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0x02, 0x02, 0x4C, 0x01, 0x00,
        0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0x02, 0x02, 0x44, 0x01, 0x00, 0x3B
    ];
}
