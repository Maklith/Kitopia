using System.Security.Cryptography;
using KitopiaEx.ImageCompression;
using PluginCore.Media;
using SkiaSharp;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class ImageCompressionTests
{
    private string _directory = null!;
    private Ffmpeg _ffmpeg = null!;
    private ImageCompressor _compressor = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Kitopia compression tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _ffmpeg = new Ffmpeg();
        _ = _ffmpeg.Version;
        _compressor = new ImageCompressor(_ffmpeg);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Delete(_directory, recursive: true); return; }
            catch (IOException) when (attempt < 4) { await Task.Delay(100); }
        }
    }

    [TestMethod]
    public async Task CompressAsync_QualityAndResize_PreservesOriginalAndProducesReadableImage()
    {
        var path = CreateImage("source image.png");
        var original = SHA256.HashData(File.ReadAllBytes(path));
        var low = await _compressor.CompressAsync(path, new ImageCompressionOptions { Quality = 10, SkipLarger = false });
        var high = await _compressor.CompressAsync(path, new ImageCompressionOptions { Quality = 90, SkipLarger = false });
        Assert.IsTrue(low.OutputBytes < high.OutputBytes);
        Assert.IsTrue(high.OutputBytes < high.OriginalBytes);
        var resized = await _compressor.CompressAsync(path, new ImageCompressionOptions
            { ResizePercent = 75, MaxDimension = 200, SkipLarger = false });
        using var bitmap = SKBitmap.Decode(resized.OutputPath!);
        Assert.AreEqual(200, bitmap.Width);
        Assert.AreEqual(150, bitmap.Height);
        CollectionAssert.AreEqual(original, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.AreEqual(0, Directory.GetFiles(_directory, ".kitopia-*").Length);
    }

    [TestMethod]
    [DataRow(ImageCompressionFormat.WebP)]
    [DataRow(ImageCompressionFormat.PNG)]
    public async Task CompressAsync_TransparentImage_PreservesAlpha(ImageCompressionFormat format)
    {
        var path = CreateImage("alpha.png", transparent: true);
        var result = await _compressor.CompressAsync(path, new ImageCompressionOptions { Format = format, SkipLarger = false });
        using var bitmap = SKBitmap.Decode(result.OutputPath!);
        Assert.IsNotNull(bitmap);
        Assert.AreEqual((byte)0, bitmap.GetPixel(10, 10).Alpha);
        Assert.AreEqual((byte)255, bitmap.GetPixel(400, 200).Alpha);
        if (format == ImageCompressionFormat.PNG)
        {
            using var source = SKBitmap.Decode(path);
            CollectionAssert.AreEqual(source.Bytes, bitmap.Bytes);
        }
    }

    [TestMethod]
    public async Task CompressAsync_Jpeg_FlattensAlphaOnWhite()
    {
        var result = await _compressor.CompressAsync(CreateImage("alpha.png", transparent: true),
            new ImageCompressionOptions { Format = ImageCompressionFormat.JPEG, SkipLarger = false });
        using var bitmap = SKBitmap.Decode(result.OutputPath!);
        Assert.IsNotNull(bitmap);
        var pixel = bitmap.GetPixel(10, 10);
        Assert.IsTrue(pixel.Red > 245 && pixel.Green > 245 && pixel.Blue > 245, pixel.ToString());
        Assert.AreEqual((byte)255, pixel.Alpha);
    }

    [TestMethod]
    [DataRow(ImageCompressionFormat.WebP)]
    [DataRow(ImageCompressionFormat.JPEG)]
    public async Task CompressAsync_TargetSize_ReportsAchievableAndUnattainableBudgets(ImageCompressionFormat format)
    {
        var result = await _compressor.CompressAsync(CreateImage("noise.png"),
            new ImageCompressionOptions { Mode = ImageCompressionMode.TargetSize, Format = format, TargetPercent = 20, SkipLarger = false });
        Assert.IsTrue(result.TargetMet);
        Assert.IsTrue(result.OutputBytes <= result.OriginalBytes * 0.2);
        var tiny = await _compressor.CompressAsync(CreateImage("tiny.png", width: 8, height: 8),
            new ImageCompressionOptions { Mode = ImageCompressionMode.TargetSize, Format = format, TargetPercent = 1, SkipLarger = false });
        Assert.IsFalse(tiny.TargetMet);
        Assert.IsTrue(File.Exists(tiny.OutputPath));
    }

    [TestMethod]
    public async Task CompressAsync_ConcurrentDuplicateNames_NeverOverwritesFiles()
    {
        var path = CreateImage("duplicate.png");
        var existing = Path.Combine(_directory, "duplicate_compressed.webp");
        File.WriteAllText(existing, "existing output");
        var results = await Task.WhenAll(_compressor.CompressAsync(path, new ImageCompressionOptions { SkipLarger = false }),
            _compressor.CompressAsync(path, new ImageCompressionOptions { SkipLarger = false }));
        Assert.AreNotEqual(results[0].OutputPath, results[1].OutputPath);
        Assert.AreEqual("existing output", File.ReadAllText(existing));
        Assert.IsTrue(results.All(result => File.Exists(result.OutputPath)));
        Assert.AreEqual(0, Directory.GetFiles(_directory, ".kitopia-*").Length);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    public async Task CompressAsync_ExifOrientation_RotatesAndReflectsPixels(int orientation)
    {
        var path = Path.Combine(_directory, "oriented.jpg");
        using var source = new SKBitmap(80, 40);
        using (var canvas = new SKCanvas(source))
        {
            canvas.Clear(SKColors.Blue);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 40, 20, paint);
            paint.Color = SKColors.Green;
            canvas.DrawRect(40, 0, 40, 20, paint);
            paint.Color = SKColors.Yellow;
            canvas.DrawRect(40, 20, 40, 20, paint);
        }
        using var image = SKImage.FromBitmap(source);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        var exif = Convert.FromHexString("FFE100224578696600004D4D002A00000008000101120003000000010001000000000000");
        exif[29] = (byte)orientation;
        using (var output = File.Create(path))
        {
            output.Write(encoded.AsSpan()[..2]);
            output.Write(exif);
            output.Write(encoded.AsSpan()[2..]);
        }
        var result = await _compressor.CompressAsync(path,
            new ImageCompressionOptions { Format = ImageCompressionFormat.PNG, SkipLarger = false });
        using var bitmap = SKBitmap.Decode(result.OutputPath!);
        Assert.AreEqual(orientation >= 5 ? 40 : 80, bitmap.Width);
        Assert.AreEqual(orientation >= 5 ? 80 : 40, bitmap.Height);
        SKColor[] corners = [SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow];
        int[] expected = orientation switch
        {
            1 => [0, 1, 2, 3], 2 => [1, 0, 3, 2], 3 => [3, 2, 1, 0], 4 => [2, 3, 0, 1],
            5 => [0, 2, 1, 3], 6 => [2, 0, 3, 1], 7 => [3, 1, 2, 0], _ => [1, 3, 0, 2]
        };
        for (var corner = 0; corner < 4; corner++)
        {
            var pixel = bitmap.GetPixel(corner % 2 == 0 ? 5 : bitmap.Width - 5,
                corner < 2 ? 5 : bitmap.Height - 5);
            var color = corners[expected[corner]];
            Assert.IsTrue(Math.Abs(pixel.Red - color.Red) < 15 && Math.Abs(pixel.Green - color.Green) < 15 &&
                Math.Abs(pixel.Blue - color.Blue) < 15, $"EXIF {orientation}, corner {corner}: {pixel}, expected {color}");
        }
    }

    [TestMethod]
    public void NativeLibraries_VersionAndLicense_MatchBindingsAndLgpl()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(_ffmpeg.Version));
        Assert.AreEqual((uint)FFmpeg.AutoGen.ffmpeg.LIBAVCODEC_VERSION_MAJOR,
            FFmpeg.AutoGen.ffmpeg.avcodec_version() >> 16);
        StringAssert.Contains(FFmpeg.AutoGen.ffmpeg.avcodec_license(), "LGPL");
        var configuration = FFmpeg.AutoGen.ffmpeg.avcodec_configuration();
        Assert.IsFalse(configuration.Contains("--enable-gpl", StringComparison.Ordinal));
        Assert.IsFalse(configuration.Contains("--enable-nonfree", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CompressAsync_CancelDuringEncoding_RemovesTemporaryFiles()
    {
        var path = CreateImage("large.png", width: 2000, height: 1500);
        using var cancellation = new CancellationTokenSource();
        var task = _compressor.CompressAsync(path,
            new ImageCompressionOptions { Mode = ImageCompressionMode.TargetSize, TargetPercent = 20 }, cancellation.Token);
        await Task.Delay(250);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        CollectionAssert.AreEquivalent(new[] { path }, Directory.GetFiles(_directory));
    }

    [TestMethod]
    public async Task CompressAsync_SkipLarger_DoesNotPublishOutput()
    {
        var path = CreateImage("tiny.png", width: 1, height: 1);
        var result = await _compressor.CompressAsync(path,
            new ImageCompressionOptions { Format = ImageCompressionFormat.JPEG, SkipLarger = true });
        Assert.IsNull(result.OutputPath);
        Assert.IsTrue(result.OutputBytes >= result.OriginalBytes);
        CollectionAssert.AreEquivalent(new[] { path }, Directory.GetFiles(_directory));
    }

    [TestMethod]
    public async Task StartAsync_BatchWithInvalidImage_ContinuesAndCanRetry()
    {
        var broken = Path.Combine(_directory, "broken.png");
        File.WriteAllText(broken, "not an image");
        var valid = CreateImage("valid.png");
        using var viewModel = new ImageCompressionViewModel(_ffmpeg);
        viewModel.Items.Add(new ImageCompressionItem(broken, new FileInfo(broken).Length, null));
        viewModel.Items.Add(new ImageCompressionItem(valid, new FileInfo(valid).Length, null));
        await viewModel.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(ImageCompressionStatus.Failed, viewModel.Items[0].Status);
        Assert.IsFalse(string.IsNullOrEmpty(viewModel.Items[0].Error));
        Assert.AreEqual(ImageCompressionStatus.Completed, viewModel.Items[1].Status);
        Assert.AreEqual(2, viewModel.ProcessedCount);
        Assert.IsFalse(viewModel.IsBusy);
        var firstOutput = viewModel.Items[1].OutputPath;
        await viewModel.StartCommand.ExecuteAsync(null);
        Assert.AreNotEqual(firstOutput, viewModel.Items[1].OutputPath);
        Assert.IsTrue(File.Exists(firstOutput));
    }

    [TestMethod]
    public async Task CompressAsync_AnimatedPng_RejectsWithoutExtractingAStillFrame()
    {
        var path = Path.Combine(_directory, "animated.png");
        // Two-frame APNG fixture, independent of an FFmpeg executable.
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAACGFjVEwAAAACAAAAAYSKo+YAAAAaZmNUTAAAAAAAAAACAAAAAgAAAAAAAAAAAAEAAgAA5keNuAAAABBJREFUeJxj/MsAAixgkgEADREBA9S2MkAAAAAaZmNUTAAAAAEAAAABAAAAAQAAAAAAAAAAAAEAAgAAzx+LvAAAABBmZEFUAAAAAnicY/zLwAAAAv8A/0ORBXIAAAAASUVORK5CYII="));
        await Assert.ThrowsAsync<NotSupportedException>(() => _compressor.CompressAsync(path, new ImageCompressionOptions()));
        CollectionAssert.AreEquivalent(new[] { path }, Directory.GetFiles(_directory));
    }

    [TestMethod]
    public async Task CompressAsync_TinyScaleAndSeparateDirectory_ProducesAtLeastOnePixel()
    {
        var path = CreateImage("tiny.png", width: 8, height: 8);
        var destination = Path.Combine(_directory, "output images");
        var result = await _compressor.CompressAsync(path, new ImageCompressionOptions
            { ResizePercent = 1, OutputDirectory = destination, SkipLarger = false });
        Assert.AreEqual(destination, Path.GetDirectoryName(result.OutputPath));
        using var bitmap = SKBitmap.Decode(result.OutputPath!);
        Assert.AreEqual(1, bitmap.Width);
        Assert.AreEqual(1, bitmap.Height);
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public async Task AddPathsAsync_FolderAndRepeatedFiles_DeduplicatesAndAllowsReaddingAfterRemove()
    {
        var path = CreateImage("image.png");
        File.WriteAllText(Path.Combine(_directory, "other.txt"), "ignore");
        using var viewModel = new ImageCompressionViewModel(_ffmpeg);
        await viewModel.AddPathsAsync([_directory, path, path]);
        Assert.AreEqual(1, viewModel.Items.Count);
        Assert.IsFalse(viewModel.IsAdding);
        viewModel.SelectedItem = viewModel.Items[0];
        viewModel.RemoveCommand.Execute(viewModel.SelectedItem);
        Assert.IsNull(viewModel.SelectedItem);
        await viewModel.AddPathsAsync([path]);
        Assert.AreEqual(1, viewModel.Items.Count);
        viewModel.ClearCommand.Execute(null);
        Assert.IsFalse(viewModel.StartCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task StartAsync_CancelBatch_StopsAndMarksRemainingImages()
    {
        var path = CreateImage("large.png", width: 2000, height: 1500);
        using var viewModel = new ImageCompressionViewModel(_ffmpeg) { Mode = ImageCompressionMode.TargetSize };
        viewModel.Items.Add(new ImageCompressionItem(path, new FileInfo(path).Length, null));
        var next = CreateImage("next.png");
        viewModel.Items.Add(new ImageCompressionItem(next, new FileInfo(next).Length, null));
        var running = viewModel.StartCommand.ExecuteAsync(null);
        await Task.Delay(150);
        Assert.IsTrue(viewModel.IsBusy);
        Assert.IsFalse(viewModel.CanEdit);
        viewModel.StartCancelCommand.Execute(null);
        await running;
        Assert.IsTrue(viewModel.Items.All(item => item.Status == ImageCompressionStatus.Cancelled));
        Assert.IsFalse(viewModel.IsBusy);
        CollectionAssert.AreEquivalent(new[] { path, next }, Directory.GetFiles(_directory));
    }

    private string CreateImage(string name, bool transparent = false, int width = 512, int height = 384)
    {
        var path = Path.Combine(_directory, name);
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var random = new Random(42);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, transparent && x < width / 2 ? SKColors.Transparent
                    : new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        encoded.SaveTo(output);
        return path;
    }
}
