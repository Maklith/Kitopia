using Hjg.Pngcs;
using Kitopia.Desktop.Features.Imaging;
using Kitopia.Desktop.Features.Search.Semantic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KitopiaTest.Search;

[TestClass]
public sealed class EmbeddingGemmaImageProcessorTests
{
    [TestMethod]
    [DataRow(PngColorType.Rgb, PngBitDepth.Bit8)]
    [DataRow(PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [DataRow(PngColorType.Grayscale, PngBitDepth.Bit8)]
    [DataRow(PngColorType.GrayscaleWithAlpha, PngBitDepth.Bit8)]
    [DataRow(PngColorType.Palette, PngBitDepth.Bit8)]
    [DataRow(PngColorType.Grayscale, PngBitDepth.Bit1)]
    [DataRow(PngColorType.Grayscale, PngBitDepth.Bit2)]
    [DataRow(PngColorType.Grayscale, PngBitDepth.Bit4)]
    [DataRow(PngColorType.Rgb, PngBitDepth.Bit16)]
    [DataRow(PngColorType.RgbWithAlpha, PngBitDepth.Bit16)]
    [DataRow(PngColorType.Grayscale, PngBitDepth.Bit16)]
    public void LoadPngRows_MultipleBlocks_MatchesFullImageHorizontalResize(PngColorType colorType, PngBitDepth bitDepth)
    {
        using var source = new Image<Rgba64>(257, 129);
        source.ProcessPixelRows(accessor =>
        {
            for (var row = 0; row < accessor.Height; row++)
            {
                var pixels = accessor.GetRowSpan(row);
                for (var column = 0; column < pixels.Length; column++)
                    pixels[column] = new Rgba64((ushort)(column * 255), (ushort)(row * 507),
                        (ushort)((column * 317 + row * 73) % 65536), (ushort)((column * 173 + row * 379) % 65536));
            }
        });
        using var stream = new MemoryStream();
        source.Save(stream, new PngEncoder { ColorType = colorType, BitDepth = bitDepth });
        stream.Position = 0;
        using var expected = Image.Load<Rgb24>(stream);
        expected.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(80, expected.Height), Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic, Compand = false
        }));
        stream.Position = 0;

        using var actual = ImageInputLoader.LoadPngRows(stream, 80, 129, CancellationToken.None);

        Assert.AreEqual(expected.Size, actual.Size);
        expected.ProcessPixelRows(actual, (left, right) =>
        {
            for (var row = 0; row < left.Height; row++)
            {
                var expectedPixels = left.GetRowSpan(row);
                var actualPixels = right.GetRowSpan(row);
                for (var column = 0; column < expectedPixels.Length; column++)
                {
                    var expectedPixel = expectedPixels[column];
                    var actualPixel = actualPixels[column];
                    var tolerance = bitDepth == PngBitDepth.Bit16 ? 1 : 0;
                    Assert.IsTrue(Math.Abs(expectedPixel.R - actualPixel.R) <= tolerance
                                  && Math.Abs(expectedPixel.G - actualPixel.G) <= tolerance
                                  && Math.Abs(expectedPixel.B - actualPixel.B) <= tolerance,
                        $"Pixel {column}, {row}: expected {expectedPixel}, actual {actualPixel}");
                }
            }
        });
    }

    [TestMethod]
    [DataRow(8192, 8193, 256)]
    [DataRow(65536, 1025, 266)]
    public void Process_PngOver64MegapixelsWithJpegExtension_ReturnsBoundedTensor(int width, int height, int expectedSoftTokens)
    {
        var path = Path.Combine(Path.GetTempPath(), $"KitopiaLargeImage_{Guid.NewGuid():N}.jpg");
        try
        {
            using (var stream = File.Create(path))
            {
                var writer = new PngWriter(stream, new Hjg.Pngcs.ImageInfo(width, height, 8, false));
                var row = new byte[width * 3];
                for (var column = 0; column < width; column++)
                {
                    row[column * 3] = 255;
                    row[column * 3 + 1] = 64;
                    row[column * 3 + 2] = 128;
                }
                for (var index = 0; index < height; index++) writer.WriteRowByte(row, index);
                writer.End();
            }

            var (pixels, positions, softTokens) = EmbeddingGemmaImageProcessor.Process(path);

            Assert.AreEqual(expectedSoftTokens, softTokens);
            Assert.HasCount(EmbeddingGemmaImageProcessor.MaximumPatches * 768, pixels);
            Assert.HasCount(EmbeddingGemmaImageProcessor.MaximumPatches * 2, positions);
            for (var offset = 0; offset < softTokens * 9 * 768; offset += 3)
            {
                Assert.AreEqual(1f, pixels[offset]);
                Assert.AreEqual(64 / 255f, pixels[offset + 1]);
                Assert.AreEqual(128 / 255f, pixels[offset + 2]);
            }
            Assert.IsTrue(pixels.AsSpan(softTokens * 9 * 768).IndexOfAnyExcept(0f) < 0);
            Assert.IsTrue(positions.AsSpan(softTokens * 9 * 2).IndexOfAnyExcept(-1L) < 0);
            using var ocrImage = ImageInputLoader.LoadBgr(path, ImageInputLoader.MaximumOcrPixels);
            Assert.IsTrue((long)ocrImage.Rows * ocrImage.Cols <= ImageInputLoader.MaximumOcrPixels);
            Assert.AreEqual(new OpenCvSharp.Vec3b(128, 64, 255), ocrImage.At<OpenCvSharp.Vec3b>(0, 0));
            using var singlePixel = ImageInputLoader.LoadBgr(path, 1);
            Assert.AreEqual(1L, singlePixel.Total());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Process_JpegOver64Megapixels_UsesReducedDecodeAndReturnsBoundedTensor()
    {
        var path = Path.Combine(Path.GetTempPath(), $"KitopiaLargeImage_{Guid.NewGuid():N}.jpg");
        try
        {
            using (var source = new Image<Rgb24>(ImageInputLoader.ImageConfiguration, 8193, 8193, new Rgb24(64, 128, 192)))
                source.SaveAsJpeg(path);

            var (pixels, positions, softTokens) = EmbeddingGemmaImageProcessor.Process(path);

            Assert.AreEqual(256, softTokens);
            Assert.HasCount(EmbeddingGemmaImageProcessor.MaximumPatches * 768, pixels);
            Assert.HasCount(EmbeddingGemmaImageProcessor.MaximumPatches * 2, positions);
            Assert.AreEqual(64 / 255f, pixels[0], 2 / 255f);
            Assert.AreEqual(128 / 255f, pixels[1], 2 / 255f);
            Assert.AreEqual(192 / 255f, pixels[2], 2 / 255f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadPngRows_InterlacedOrExcessiveIntermediate_RejectsBeforeDecoding()
    {
        using var source = new Image<Rgb24>(8, 8);
        using var stream = new MemoryStream();
        source.Save(stream, new PngEncoder { InterlaceMethod = PngInterlaceMode.Adam7 });
        stream.Position = 0;
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ImageInputLoader.LoadPngRows(stream, 8, 8, CancellationToken.None));

        stream.SetLength(0);
        stream.Position = 0;
        source.Save(stream, new PngEncoder { InterlaceMethod = PngInterlaceMode.None });
        stream.Position = 0;
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ImageInputLoader.LoadPngRows(stream, int.MaxValue, 8, CancellationToken.None));
    }

    [TestMethod]
    public void LoadPngRows_Canceled_StopsWithoutClosingCallerStream()
    {
        using var source = new Image<Rgb24>(8, 8);
        using var stream = new MemoryStream();
        source.SaveAsPng(stream);
        stream.Position = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            ImageInputLoader.LoadPngRows(stream, 8, 8, cancellation.Token));
        Assert.IsTrue(stream.CanRead);
    }
}
