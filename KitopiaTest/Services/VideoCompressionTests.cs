using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless;
using KitopiaEx.ImageCompression;
using KitopiaEx.SearchWindow.InputDataAnalyzer;
using KitopiaEx.VideoCompression;
using PluginCore.Media;
using PluginCore.SearchWindow.InputData;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class VideoCompressionTests
{
    private string _directory = null!;
    private string _source = null!;
    private readonly VideoCompressor _compressor = new();

    [TestInitialize]
    public async Task Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Kitopia video tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _source = Path.Combine(_directory, "source video.mkv");
        await RunToolAsync("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=15",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "2", "-c:v", "ffv1", "-c:a", "pcm_s16le", _source);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    [DataRow(VideoCompressionFormat.MP4, VideoCompressionCodec.H264, "h264", "aac")]
    [DataRow(VideoCompressionFormat.MP4, VideoCompressionCodec.HEVC, "hevc", "aac")]
    [DataRow(VideoCompressionFormat.MP4, VideoCompressionCodec.VP9, "vp9", "aac")]
    [DataRow(VideoCompressionFormat.MP4, VideoCompressionCodec.AV1, "av1", "aac")]
    [DataRow(VideoCompressionFormat.WebM, VideoCompressionCodec.VP9, "vp9", "opus")]
    [DataRow(VideoCompressionFormat.WebM, VideoCompressionCodec.AV1, "av1", "opus")]
    [DataRow(VideoCompressionFormat.MKV, VideoCompressionCodec.H264, "h264", "opus")]
    [DataRow(VideoCompressionFormat.MKV, VideoCompressionCodec.VP9, "vp9", "opus")]
    [DataRow(VideoCompressionFormat.MKV, VideoCompressionCodec.AV1, "av1", "opus")]
    [DataRow(VideoCompressionFormat.MKV, VideoCompressionCodec.HEVC, "hevc", "opus")]
    [DataRow(VideoCompressionFormat.MOV, VideoCompressionCodec.H264, "h264", "aac")]
    [DataRow(VideoCompressionFormat.MOV, VideoCompressionCodec.HEVC, "hevc", "aac")]
    public async Task CompressAsync_OutputFormatsAndResize_PreservesVideoAndAudio(VideoCompressionFormat format,
        VideoCompressionCodec codec, string videoCodec, string audioCodec)
    {
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(_source));
        var result = await _compressor.CompressAsync(_source, new VideoCompressionOptions
        { Format = format, Codec = codec, ResizePercent = 75, MaxDimension = 120, SkipLarger = false });
        Assert.IsNotNull(result.OutputPath);
        Assert.IsTrue(result.OutputBytes < result.OriginalBytes);
        using var probe = await ProbeAsync(result.OutputPath);
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video");
        Assert.AreEqual(videoCodec, video.GetProperty("codec_name").GetString());
        Assert.AreEqual(120, video.GetProperty("width").GetInt32());
        Assert.AreEqual(66, video.GetProperty("height").GetInt32());
        Assert.AreEqual(audioCodec, streams.Single(stream => stream.GetProperty("codec_type").GetString() == "audio").GetProperty("codec_name").GetString());
        Assert.AreEqual(2, double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture), 0.15);
        await RunToolAsync("ffmpeg", "-v", "error", "-i", result.OutputPath, "-f", "null", "-");
        CollectionAssert.AreEqual(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(_source)));
        Assert.AreEqual(0, Directory.GetFiles(_directory, ".kitopia-*").Length);
    }

    [TestMethod]
    [DataRow(VideoCompressionCodec.H264)]
    [DataRow(VideoCompressionCodec.VP9)]
    [DataRow(VideoCompressionCodec.AV1)]
    [DataRow(VideoCompressionCodec.HEVC)]
    public async Task CompressAsync_Quality_LowerQualityProducesSmallerOutput(VideoCompressionCodec codec)
    {
        var options = new VideoCompressionOptions { Codec = codec, KeepAudio = false, SkipLarger = false };
        var low = await _compressor.CompressAsync(_source, options with { Quality = 10 });
        var high = await _compressor.CompressAsync(_source, options with { Quality = 90 });
        Assert.IsTrue(low.OutputBytes < high.OutputBytes, $"Low {low.OutputBytes}, high {high.OutputBytes}");
        using var probe = await ProbeAsync(low.OutputPath!);
        Assert.AreEqual(1, probe.RootElement.GetProperty("streams").GetArrayLength());
        Assert.AreNotEqual(low.OutputPath, high.OutputPath);
    }

    [TestMethod]
    [DataRow(VideoCompressionFormat.MP4, VideoCompressionCodec.H264)]
    [DataRow(VideoCompressionFormat.WebM, VideoCompressionCodec.VP9)]
    [DataRow(VideoCompressionFormat.MKV, VideoCompressionCodec.AV1)]
    [DataRow(VideoCompressionFormat.MOV, VideoCompressionCodec.HEVC)]
    public async Task CompressAsync_TargetSize_ReportsActualBudgetResult(VideoCompressionFormat format, VideoCompressionCodec codec)
    {
        var options = new VideoCompressionOptions
        {
            Format = format,
            Codec = codec,
            Mode = VideoCompressionMode.TargetSize,
            TargetPercent = 30,
            SkipLarger = false
        };
        var result = await _compressor.CompressAsync(_source, options);
        Assert.IsTrue(result.TargetMet, $"Output {result.OutputBytes}, original {result.OriginalBytes}, target {options.TargetPercent}%");
        Assert.IsTrue(result.OutputBytes <= result.OriginalBytes * 0.3);
        var tiny = await _compressor.CompressAsync(result.OutputPath!, options with { TargetPercent = 1 });
        Assert.IsFalse(tiny.TargetMet);
        Assert.IsTrue(File.Exists(tiny.OutputPath));
        Assert.AreEqual(0, Directory.GetFiles(_directory, ".kitopia-*").Length);
    }

    [TestMethod]
    [DataRow(VideoCompressionFormat.MP4, VideoCompressionCodec.H264)]
    [DataRow(VideoCompressionFormat.WebM, VideoCompressionCodec.VP9)]
    public async Task CompressAsync_MultipleAudioTracks_PreservesTracksLanguagesAndChannels(VideoCompressionFormat format, VideoCompressionCodec codec)
    {
        var multiAudio = Path.Combine(_directory, "multiple audio.mkv");
        await RunToolAsync("ffmpeg", "-v", "error", "-i", _source, "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000",
            "-t", "2", "-map", "0:V:0", "-map", "0:a", "-map", "1:a", "-c:v", "copy", "-c:a", "pcm_s16le",
            "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=zho", multiAudio);
        var result = await _compressor.CompressAsync(multiAudio, new VideoCompressionOptions
        { Format = format, Codec = codec, Mode = VideoCompressionMode.TargetSize, TargetPercent = 30, SkipLarger = false });
        using var probe = JsonDocument.Parse(await RunToolAsync("ffprobe", "-v", "error", "-select_streams", "a",
            "-show_entries", "stream=channels:stream_tags=language", "-of", "json", result.OutputPath!));
        var audio = probe.RootElement.GetProperty("streams");
        Assert.AreEqual(2, audio.GetArrayLength());
        Assert.AreEqual("eng", audio[0].GetProperty("tags").GetProperty("language").GetString());
        Assert.AreEqual("zho", audio[1].GetProperty("tags").GetProperty("language").GetString());
        Assert.IsTrue(audio.EnumerateArray().All(stream => stream.GetProperty("channels").GetInt32() == 1));
        Assert.IsTrue(result.TargetMet);
    }

    [TestMethod]
    public async Task CompressAsync_ConcurrentOutputs_DoesNotOverwriteFiles()
    {
        var existing = Path.Combine(_directory, "source video_compressed.mp4");
        await File.WriteAllTextAsync(existing, "keep existing output");
        var options = new VideoCompressionOptions { SkipLarger = false, OutputDirectory = _directory };
        var results = await Task.WhenAll(_compressor.CompressAsync(_source, options), _compressor.CompressAsync(_source, options));
        Assert.AreEqual("keep existing output", await File.ReadAllTextAsync(existing));
        Assert.AreNotEqual(results[0].OutputPath, results[1].OutputPath);
        Assert.IsTrue(results.All(result => File.Exists(result.OutputPath)));
    }

    [TestMethod]
    public async Task CompressAsync_RotatedSource_AppliesRotationBeforeScaling()
    {
        var original = await _compressor.CompressAsync(_source, new VideoCompressionOptions { SkipLarger = false });
        var rotated = Path.Combine(_directory, "rotated.mp4");
        await RunToolAsync("ffmpeg", "-v", "error", "-display_rotation", "90", "-i", original.OutputPath!, "-c", "copy", rotated);
        using var inputProbe = JsonDocument.Parse(await RunToolAsync("ffprobe", "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream_side_data=rotation", "-of", "json", rotated));
        Assert.AreEqual(90, inputProbe.RootElement.GetProperty("streams")[0].GetProperty("side_data_list")[0].GetProperty("rotation").GetInt32());
        var result = await _compressor.CompressAsync(rotated, new VideoCompressionOptions { MaxDimension = 160, SkipLarger = false });
        using var probe = await ProbeAsync(result.OutputPath!);
        var video = probe.RootElement.GetProperty("streams").EnumerateArray().Single(stream => stream.GetProperty("codec_type").GetString() == "video");
        Assert.AreEqual(90, video.GetProperty("width").GetInt32());
        Assert.AreEqual(160, video.GetProperty("height").GetInt32());
    }

    [TestMethod]
    public async Task CompressAsync_AnamorphicSource_PreservesDisplayAspectRatio()
    {
        var anamorphic = Path.Combine(_directory, "anamorphic.mkv");
        await RunToolAsync("ffmpeg", "-v", "error", "-i", _source, "-vf", "setsar=2/1", "-c:v", "ffv1", "-an", anamorphic);
        var result = await _compressor.CompressAsync(anamorphic, new VideoCompressionOptions { ResizePercent = 50, SkipLarger = false });
        using var probe = JsonDocument.Parse(await RunToolAsync("ffprobe", "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream=display_aspect_ratio", "-of", "json", result.OutputPath!));
        Assert.AreEqual("32:9", probe.RootElement.GetProperty("streams")[0].GetProperty("display_aspect_ratio").GetString());
    }

    [TestMethod]
    [DataRow(VideoCompressionFormat.MP4, VideoCompressionCodec.H264, "smpte2084")]
    [DataRow(VideoCompressionFormat.WebM, VideoCompressionCodec.VP9, "arib-std-b67")]
    [DataRow(VideoCompressionFormat.MKV, VideoCompressionCodec.AV1, "smpte2084")]
    [DataRow(VideoCompressionFormat.MOV, VideoCompressionCodec.HEVC, "smpte2084")]
    public async Task CompressAsync_HdrSource_ToneMapsToSdr(VideoCompressionFormat format, VideoCompressionCodec codec, string transfer)
    {
        var hdr = Path.Combine(_directory, "hdr.mkv");
        await RunToolAsync("ffmpeg", "-v", "error", "-i", _source, "-vf",
            $"format=yuv420p10le,setparams=color_primaries=bt2020:color_trc={transfer}:colorspace=bt2020nc", "-c:v", "libvpx-vp9", "-lossless", "1",
            "-color_primaries", "bt2020", "-color_trc", transfer, "-colorspace", "bt2020nc", "-an", hdr);
        using var inputProbe = JsonDocument.Parse(await RunToolAsync("ffprobe", "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream=color_transfer", "-of", "json", hdr));
        Assert.AreEqual(transfer, inputProbe.RootElement.GetProperty("streams")[0].GetProperty("color_transfer").GetString());
        var result = await _compressor.CompressAsync(hdr, new VideoCompressionOptions { Format = format, Codec = codec, SkipLarger = false });
        using var probe = JsonDocument.Parse(await RunToolAsync("ffprobe", "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream=color_primaries,color_transfer,color_space,pix_fmt", "-of", "json", result.OutputPath!));
        var video = probe.RootElement.GetProperty("streams")[0];
        Assert.AreEqual("bt709", video.GetProperty("color_primaries").GetString());
        Assert.AreEqual("bt709", video.GetProperty("color_transfer").GetString());
        Assert.AreEqual("bt709", video.GetProperty("color_space").GetString());
        Assert.AreEqual("yuv420p", video.GetProperty("pix_fmt").GetString());
        await RunToolAsync("ffmpeg", "-v", "error", "-i", result.OutputPath!, "-f", "null", "-");
    }

    [TestMethod]
    [DataRow(VideoCompressionCodec.VP9, "smpte2084")]
    [DataRow(VideoCompressionCodec.VP9, "arib-std-b67")]
    [DataRow(VideoCompressionCodec.AV1, "smpte2084")]
    [DataRow(VideoCompressionCodec.AV1, "arib-std-b67")]
    [DataRow(VideoCompressionCodec.HEVC, "smpte2084")]
    [DataRow(VideoCompressionCodec.HEVC, "arib-std-b67")]
    public async Task CompressAsync_HdrConversionDisabled_PreservesTenBitHdr(VideoCompressionCodec codec, string transfer)
    {
        var hdr = Path.Combine(_directory, "hdr.mkv");
        await RunToolAsync("ffmpeg", "-v", "error", "-i", _source, "-vf",
            $"format=yuv420p10le,setparams=color_primaries=bt2020:color_trc={transfer}:colorspace=bt2020nc",
            "-c:v", "libvpx-vp9", "-lossless", "1", "-an", hdr);
        VideoCompressionResult result;
        try
        {
            result = await _compressor.CompressAsync(hdr, new VideoCompressionOptions
            { Codec = codec, ConvertHdrToSdr = false, ResizePercent = 50, SkipLarger = false });
        }
        catch (NotSupportedException exception) when (codec == VideoCompressionCodec.HEVC && exception.Message.StartsWith(
            PluginCore.Localization.Lang.Get("lang.kitopiaex.video_compression.hevc_hdr_unavailable"), StringComparison.Ordinal))
        {
            Assert.Inconclusive(exception.Message);
            return;
        }
        using var probe = JsonDocument.Parse(await RunToolAsync("ffprobe", "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream=color_primaries,color_transfer,color_space,pix_fmt,width,height", "-of", "json", result.OutputPath!));
        var video = probe.RootElement.GetProperty("streams")[0];
        Assert.AreEqual("bt2020", video.GetProperty("color_primaries").GetString());
        Assert.AreEqual(transfer, video.GetProperty("color_transfer").GetString());
        Assert.AreEqual("bt2020nc", video.GetProperty("color_space").GetString());
        Assert.AreEqual("yuv420p10le", video.GetProperty("pix_fmt").GetString());
        Assert.AreEqual(160, video.GetProperty("width").GetInt32());
        Assert.AreEqual(90, video.GetProperty("height").GetInt32());
        await RunToolAsync("ffmpeg", "-v", "error", "-i", result.OutputPath!, "-f", "null", "-");
        await Assert.ThrowsAsync<NotSupportedException>(() => _compressor.CompressAsync(hdr,
            new VideoCompressionOptions { ConvertHdrToSdr = false }));
        Assert.AreEqual(0, Directory.GetFiles(_directory, ".kitopia-*").Length);
    }

    [TestMethod]
    [DataRow(VideoCompressionFormat.WebM, VideoCompressionCodec.H264)]
    [DataRow(VideoCompressionFormat.WebM, VideoCompressionCodec.HEVC)]
    [DataRow(VideoCompressionFormat.MOV, VideoCompressionCodec.VP9)]
    [DataRow(VideoCompressionFormat.MOV, VideoCompressionCodec.AV1)]
    public async Task CompressAsync_IncompatibleContainer_RejectsCodec(VideoCompressionFormat format, VideoCompressionCodec codec)
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _compressor.CompressAsync(_source,
            new VideoCompressionOptions { Format = format, Codec = codec }));
        Assert.AreEqual(1, Directory.GetFiles(_directory).Length);
    }

    [TestMethod]
    [DataRow(VideoCompressionCodec.H264, "h264")]
    [DataRow(VideoCompressionCodec.HEVC, "hevc")]
    [DataRow(VideoCompressionCodec.VP9, "vp9")]
    [DataRow(VideoCompressionCodec.AV1, "av1")]
    public async Task CompressAsync_CompressionWithUnchangedCodec_ReencodesOriginalCodec(VideoCompressionCodec codec, string name)
    {
        var source = await _compressor.CompressAsync(_source, new VideoCompressionOptions
        { Codec = codec, Quality = 90, SkipLarger = false });
        var result = await _compressor.CompressAsync(source.OutputPath!, new VideoCompressionOptions
        { Format = VideoCompressionFormat.KeepOriginal, Codec = VideoCompressionCodec.KeepOriginal, ResizePercent = 50, Quality = 10, SkipLarger = false });
        Assert.AreEqual(Path.GetExtension(source.OutputPath), Path.GetExtension(result.OutputPath));
        using var probe = await ProbeAsync(result.OutputPath!);
        Assert.AreEqual(name, probe.RootElement.GetProperty("streams")[0].GetProperty("codec_name").GetString());
        Assert.AreEqual(160, probe.RootElement.GetProperty("streams")[0].GetProperty("width").GetInt32());
        Assert.AreEqual(90, probe.RootElement.GetProperty("streams")[0].GetProperty("height").GetInt32());
        Assert.IsTrue(result.OutputBytes < result.OriginalBytes);
    }

    [TestMethod]
    [DataRow(VideoCompressionFormat.MOV, VideoCompressionCodec.H264, VideoCompressionFormat.KeepOriginal, VideoCompressionCodec.HEVC, "mov,mp4,m4a,3gp,3g2,mj2", "hevc", "aac")]
    [DataRow(VideoCompressionFormat.MP4, VideoCompressionCodec.H264, VideoCompressionFormat.WebM, VideoCompressionCodec.VP9, "matroska,webm", "vp9", "opus")]
    [DataRow(VideoCompressionFormat.MOV, VideoCompressionCodec.HEVC, VideoCompressionFormat.MP4, VideoCompressionCodec.H264, "mov,mp4,m4a,3gp,3g2,mj2", "h264", "aac")]
    public async Task CompressAsync_Conversion_ChangesSelectedFormatAndCodec(VideoCompressionFormat sourceFormat, VideoCompressionCodec sourceCodec,
        VideoCompressionFormat format, VideoCompressionCodec codec, string formatName, string videoCodec, string audioCodec)
    {
        var source = await _compressor.CompressAsync(_source, new VideoCompressionOptions
        { Format = sourceFormat, Codec = sourceCodec, SkipLarger = false });
        var result = await _compressor.CompressAsync(source.OutputPath!, new VideoCompressionOptions
        {
            Mode = VideoCompressionMode.Conversion,
            Format = format,
            Codec = codec,
            SkipLarger = true,
            ResizePercent = 10,
            MaxDimension = 16,
            KeepAudio = false,
            TargetPercent = 1
        });
        Assert.IsNotNull(result.OutputPath);
        Assert.IsTrue(result.TargetMet);
        if (format == VideoCompressionFormat.KeepOriginal)
            Assert.AreEqual(Path.GetExtension(source.OutputPath), Path.GetExtension(result.OutputPath));
        using var probe = await ProbeAsync(result.OutputPath);
        var streams = probe.RootElement.GetProperty("streams");
        Assert.AreEqual(videoCodec, streams[0].GetProperty("codec_name").GetString());
        Assert.AreEqual(320, streams[0].GetProperty("width").GetInt32());
        Assert.AreEqual(180, streams[0].GetProperty("height").GetInt32());
        Assert.AreEqual(audioCodec, streams[1].GetProperty("codec_name").GetString());
        Assert.AreEqual(formatName, probe.RootElement.GetProperty("format").GetProperty("format_name").GetString());
        if (audioCodec == "aac")
        {
            var inputHash = await RunToolAsync("ffmpeg", "-v", "error", "-i", source.OutputPath!, "-map", "0:a", "-c", "copy", "-f", "streamhash", "-hash", "sha256", "-");
            var outputHash = await RunToolAsync("ffmpeg", "-v", "error", "-i", result.OutputPath, "-map", "0:a", "-c", "copy", "-f", "streamhash", "-hash", "sha256", "-");
            Assert.AreEqual(inputHash, outputHash);
        }
        await RunToolAsync("ffmpeg", "-v", "error", "-i", result.OutputPath, "-f", "null", "-");
    }

    [TestMethod]
    [DataRow(VideoCompressionFormat.MOV, VideoCompressionCodec.HEVC)]
    [DataRow(VideoCompressionFormat.WebM, VideoCompressionCodec.VP9)]
    public async Task CompressAsync_ConversionWithBothUnchanged_PreservesContainerAndPackets(VideoCompressionFormat format, VideoCompressionCodec codec)
    {
        var source = await _compressor.CompressAsync(_source, new VideoCompressionOptions
        { Format = format, Codec = codec, SkipLarger = false });
        var result = await _compressor.CompressAsync(source.OutputPath!, new VideoCompressionOptions
        { Mode = VideoCompressionMode.Conversion, Format = VideoCompressionFormat.KeepOriginal, Codec = VideoCompressionCodec.KeepOriginal });
        Assert.IsNotNull(result.OutputPath);
        Assert.AreEqual(Path.GetExtension(source.OutputPath), Path.GetExtension(result.OutputPath));
        var inputHash = await RunToolAsync("ffmpeg", "-v", "error", "-i", source.OutputPath!, "-map", "0", "-c", "copy", "-f", "streamhash", "-hash", "sha256", "-");
        var outputHash = await RunToolAsync("ffmpeg", "-v", "error", "-i", result.OutputPath, "-map", "0", "-c", "copy", "-f", "streamhash", "-hash", "sha256", "-");
        Assert.AreEqual(inputHash, outputHash);
    }

    [TestMethod]
    public async Task CompressAsync_KeepM4vFormat_UsesMp4ContainerInsteadOfRawVideo()
    {
        var source = await _compressor.CompressAsync(_source, new VideoCompressionOptions { SkipLarger = false });
        var m4v = Path.Combine(_directory, "video.m4v");
        File.Move(source.OutputPath!, m4v);
        var result = await _compressor.CompressAsync(m4v, new VideoCompressionOptions
        { Mode = VideoCompressionMode.Conversion, Format = VideoCompressionFormat.KeepOriginal, Codec = VideoCompressionCodec.KeepOriginal });
        Assert.AreEqual(".m4v", Path.GetExtension(result.OutputPath));
        using var probe = await ProbeAsync(result.OutputPath!);
        Assert.AreEqual("mov,mp4,m4a,3gp,3g2,mj2", probe.RootElement.GetProperty("format").GetProperty("format_name").GetString());
        Assert.AreEqual(2, probe.RootElement.GetProperty("streams").GetArrayLength());
    }

    [TestMethod]
    public async Task CompressAsync_UnsupportedOriginalCodec_ReportsErrorWithoutChangingCodec()
    {
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => _compressor.CompressAsync(_source,
            new VideoCompressionOptions { Codec = VideoCompressionCodec.KeepOriginal }));
        Assert.AreEqual(PluginCore.Localization.Lang.Format("lang.kitopiaex.video_compression.original_codec_unsupported", "ffv1"), exception.Message);
        Assert.AreEqual(1, Directory.GetFiles(_directory).Length);
    }

    [TestMethod]
    public async Task Batch_UnchangedFormatAndCodec_ResolvesEachFileIndependently()
    {
        var mov = await _compressor.CompressAsync(_source, new VideoCompressionOptions
        { Format = VideoCompressionFormat.MOV, Codec = VideoCompressionCodec.HEVC, SkipLarger = false });
        var webm = await _compressor.CompressAsync(_source, new VideoCompressionOptions
        { Format = VideoCompressionFormat.WebM, Codec = VideoCompressionCodec.VP9, SkipLarger = false });
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
        await session.Dispatch(async () =>
        {
            using var viewModel = new VideoCompressionViewModel(new Ffmpeg())
            {
                Format = VideoCompressionFormat.KeepOriginal,
                Codec = VideoCompressionCodec.KeepOriginal,
                ResizePercent = 50,
                SkipLarger = false
            };
            await viewModel.AddPathsAsync([mov.OutputPath!, webm.OutputPath!]);
            await viewModel.StartCommand.ExecuteAsync(null);
            foreach (var item in viewModel.Items)
            {
                Assert.AreEqual(ImageCompressionStatus.Completed, item.Status, item.Error);
                Assert.AreEqual(Path.GetExtension(item.SourcePath), Path.GetExtension(item.OutputPath));
                using var sourceProbe = await ProbeAsync(item.SourcePath);
                using var outputProbe = await ProbeAsync(item.OutputPath!);
                Assert.AreEqual(sourceProbe.RootElement.GetProperty("streams")[0].GetProperty("codec_name").GetString(),
                    outputProbe.RootElement.GetProperty("streams")[0].GetProperty("codec_name").GetString());
                Assert.AreEqual(160, outputProbe.RootElement.GetProperty("streams")[0].GetProperty("width").GetInt32());
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(VideoCompressionFormat.MP4, VideoCompressionFormat.MKV, VideoCompressionCodec.H264)]
    [DataRow(VideoCompressionFormat.WebM, VideoCompressionFormat.MP4, VideoCompressionCodec.VP9)]
    [DataRow(VideoCompressionFormat.MP4, VideoCompressionFormat.WebM, VideoCompressionCodec.AV1)]
    [DataRow(VideoCompressionFormat.MP4, VideoCompressionFormat.MOV, VideoCompressionCodec.HEVC)]
    public async Task CompressAsync_FormatOnly_CopiesOriginalPackets(VideoCompressionFormat sourceFormat,
        VideoCompressionFormat outputFormat, VideoCompressionCodec codec)
    {
        var source = await _compressor.CompressAsync(_source, new VideoCompressionOptions
        { Format = sourceFormat, Codec = codec, KeepAudio = false, Quality = 10, SkipLarger = false });
        var result = await _compressor.CompressAsync(source.OutputPath!, new VideoCompressionOptions
        {
            Mode = VideoCompressionMode.Conversion,
            Format = outputFormat,
            Codec = VideoCompressionCodec.KeepOriginal,
            Quality = 1,
            ResizePercent = 10,
            MaxDimension = 16,
            SkipLarger = true
        });
        Assert.IsNotNull(result.OutputPath);
        StringAssert.Contains(result.OutputPath, "_converted");
        Assert.IsTrue(result.TargetMet);
        foreach (var path in new[] { source.OutputPath!, result.OutputPath })
        {
            using var probe = await ProbeAsync(path);
            var video = probe.RootElement.GetProperty("streams")[0];
            Assert.AreEqual(320, video.GetProperty("width").GetInt32());
            Assert.AreEqual(180, video.GetProperty("height").GetInt32());
        }
        var inputPackets = await RunToolAsync("ffprobe", "-v", "error", "-show_packets", "-show_data_hash", "sha256",
            "-show_entries", "packet=stream_index,data_hash", "-of", "json", source.OutputPath!);
        var outputPackets = await RunToolAsync("ffprobe", "-v", "error", "-show_packets", "-show_data_hash", "sha256",
            "-show_entries", "packet=stream_index,data_hash", "-of", "json", result.OutputPath);
        Assert.AreEqual(inputPackets, outputPackets);
        if (sourceFormat == VideoCompressionFormat.WebM)
            Assert.IsTrue(result.OutputBytes > result.OriginalBytes, "The larger MP4 must still be published.");
        Assert.AreEqual(0, Directory.GetFiles(_directory, ".kitopia-*").Length);
    }

    [TestMethod]
    [DataRow(VideoCompressionCodec.KeepOriginal)]
    [DataRow(VideoCompressionCodec.AV1)]
    public async Task CompressAsync_Conversion_PreservesOtherVideoAudioSubtitlesMetadataAndChapters(VideoCompressionCodec codec)
    {
        var subtitles = Path.Combine(_directory, "subtitles.srt");
        var metadata = Path.Combine(_directory, "chapters.txt");
        await File.WriteAllTextAsync(subtitles, "1\n00:00:00,000 --> 00:00:01,000\nTest subtitle\n");
        await File.WriteAllTextAsync(metadata, ";FFMETADATA1\ntitle=HDR clip\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1000\ntitle=First chapter\n");
        var hdr = Path.Combine(_directory, "all streams.mkv");
        await RunToolAsync("ffmpeg", "-v", "error", "-i", _source, "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000",
            "-i", subtitles, "-i", metadata, "-t", "2", "-map", "0:V:0", "-map", "0:V:0", "-map", "0:a", "-map", "1:a", "-map", "2:s",
            "-map_metadata", "3", "-map_chapters", "3", "-vf",
            "format=yuv420p10le,setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc",
            "-c:v", "libvpx-vp9", "-lossless", "1", "-c:a", "pcm_s16le", "-c:s", "srt",
            "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=zho", hdr);
        var existing = Path.Combine(_directory, "all streams_converted.mkv");
        await File.WriteAllTextAsync(existing, "keep existing conversion");
        var result = await _compressor.CompressAsync(hdr, new VideoCompressionOptions
        {
            Mode = VideoCompressionMode.Conversion,
            Format = VideoCompressionFormat.KeepOriginal,
            Codec = codec,
            KeepAudio = false,
            ConvertHdrToSdr = false,
            ResizePercent = 10,
            SkipLarger = true
        });
        Assert.AreNotEqual(existing, result.OutputPath);
        Assert.AreEqual("keep existing conversion", await File.ReadAllTextAsync(existing));
        using var probe = JsonDocument.Parse(await RunToolAsync("ffprobe", "-v", "error", "-show_streams", "-show_format",
            "-show_chapters", "-of", "json", result.OutputPath!));
        var streams = probe.RootElement.GetProperty("streams");
        Assert.AreEqual(5, streams.GetArrayLength());
        Assert.AreEqual(codec == VideoCompressionCodec.KeepOriginal ? "vp9" : "av1", streams[0].GetProperty("codec_name").GetString());
        Assert.AreEqual("yuv420p10le", streams[0].GetProperty("pix_fmt").GetString());
        Assert.AreEqual("smpte2084", streams[0].GetProperty("color_transfer").GetString());
        Assert.AreEqual(320, streams[0].GetProperty("width").GetInt32());
        Assert.AreEqual("vp9", streams[1].GetProperty("codec_name").GetString());
        Assert.AreEqual("yuv420p10le", streams[1].GetProperty("pix_fmt").GetString());
        Assert.AreEqual("pcm_s16le", streams[2].GetProperty("codec_name").GetString());
        Assert.AreEqual("pcm_s16le", streams[3].GetProperty("codec_name").GetString());
        Assert.AreEqual("eng", streams[2].GetProperty("tags").GetProperty("language").GetString());
        Assert.AreEqual("zho", streams[3].GetProperty("tags").GetProperty("language").GetString());
        Assert.AreEqual("subrip", streams[4].GetProperty("codec_name").GetString());
        Assert.AreEqual("HDR clip", probe.RootElement.GetProperty("format").GetProperty("tags").GetProperty("title").GetString());
        Assert.AreEqual("First chapter", probe.RootElement.GetProperty("chapters")[0].GetProperty("tags").GetProperty("title").GetString());
        foreach (var type in new[] { "V:1", "a", "s" })
        {
            var inputHash = await RunToolAsync("ffmpeg", "-v", "error", "-i", hdr, "-map", "0:" + type, "-c", "copy", "-f", "streamhash", "-hash", "sha256", "-");
            var outputHash = await RunToolAsync("ffmpeg", "-v", "error", "-i", result.OutputPath!, "-map", "0:" + type, "-c", "copy", "-f", "streamhash", "-hash", "sha256", "-");
            Assert.AreEqual(inputHash, outputHash);
        }
    }

    [TestMethod]
    public async Task CompressAsync_FormatOnlyWithIncompatibleStreams_FailsAndRemovesPartialOutput()
    {
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _compressor.CompressAsync(_source,
            new VideoCompressionOptions { Mode = VideoCompressionMode.Conversion, Format = VideoCompressionFormat.WebM, Codec = VideoCompressionCodec.KeepOriginal }));
        StringAssert.Contains(exception.Message, PluginCore.Localization.Lang.Get("lang.kitopiaex.video_compression.convert_failed"));
        Assert.AreEqual(1, Directory.GetFiles(_directory).Length);
    }

    [TestMethod]
    [DataRow(VideoCompressionMode.Quality, VideoCompressionFormat.WebM)]
    [DataRow(VideoCompressionMode.Conversion, VideoCompressionFormat.MKV)]
    public async Task CompressAsync_CancelDuringProcessing_RemovesPartialOutput(VideoCompressionMode mode, VideoCompressionFormat format)
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cancellation.Cancel());
        await Assert.ThrowsAsync<OperationCanceledException>(() => _compressor.CompressAsync(_source,
            new VideoCompressionOptions
            {
                Mode = mode,
                Format = format,
                Codec = mode == VideoCompressionMode.Conversion ? VideoCompressionCodec.KeepOriginal : VideoCompressionCodec.VP9,
                SkipLarger = false
            }, progress, cancellation.Token));
        Assert.AreEqual(1, Directory.GetFiles(_directory).Length);
        using var probe = await ProbeAsync(_source);
        Assert.IsTrue(probe.RootElement.GetProperty("streams").GetArrayLength() > 0);
    }

    [TestMethod]
    public async Task CompressAsync_SkipLarger_KeepsSourceAndDiscardsOutput()
    {
        var first = await _compressor.CompressAsync(_source, new VideoCompressionOptions { Quality = 1, KeepAudio = false, SkipLarger = false });
        var result = await _compressor.CompressAsync(first.OutputPath!, new VideoCompressionOptions { Quality = 100 });
        Assert.IsTrue(result.OutputBytes >= result.OriginalBytes);
        Assert.IsNull(result.OutputPath);
        Assert.IsTrue(File.Exists(first.OutputPath));
        Assert.AreEqual(0, Directory.GetFiles(_directory, ".kitopia-*").Length);
    }

    [TestMethod]
    [DataRow(VideoCompressionMode.Quality)]
    [DataRow(VideoCompressionMode.Conversion)]
    public async Task Batch_InvalidVideoAndOverlappingImports_ContinuesAndDeduplicates(VideoCompressionMode mode)
    {
        var broken = Path.Combine(_directory, "broken.mp4");
        await File.WriteAllTextAsync(broken, "invalid video");
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
        await session.Dispatch(async () =>
        {
            using var viewModel = new VideoCompressionViewModel(new Ffmpeg())
            {
                Mode = mode,
                Format = VideoCompressionFormat.MKV,
                Codec = mode == VideoCompressionMode.Conversion ? VideoCompressionCodec.KeepOriginal : VideoCompressionCodec.AV1,
                SkipLarger = false
            };
            Assert.IsTrue(viewModel.ToolsAvailable);
            await Task.WhenAll(viewModel.AddPathsAsync([broken, _source]), viewModel.AddPathsAsync([_source]));
            Assert.AreEqual(2, viewModel.Items.Count);
            await viewModel.StartCommand.ExecuteAsync(null);
            Assert.AreEqual(ImageCompressionStatus.Failed, viewModel.Items[0].Status);
            Assert.IsFalse(string.IsNullOrWhiteSpace(viewModel.Items[0].Error));
            Assert.AreEqual(ImageCompressionStatus.Completed, viewModel.Items[1].Status, viewModel.Items[1].Error);
            using var probe = await ProbeAsync(viewModel.Items[1].OutputPath!);
            Assert.AreEqual(mode == VideoCompressionMode.Conversion ? "ffv1" : "av1",
                probe.RootElement.GetProperty("streams")[0].GetProperty("codec_name").GetString());
            Assert.AreEqual(2, viewModel.ProcessedCount);
            Assert.IsFalse(viewModel.IsBusy);
            viewModel.ClearCommand.Execute(null);
            Assert.IsFalse(viewModel.StartCommand.CanExecute(null));
            viewModel.Dispose();
            await viewModel.AddPathsAsync([_source]);
            Assert.IsTrue(viewModel.IsEmpty);
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task Batch_DisposeWhileRunning_CancelsRemainingFilesAndQueuedImport()
    {
        var next = Path.Combine(_directory, "next.mkv");
        var queued = Path.Combine(_directory, "queued.mkv");
        File.Copy(_source, next);
        File.Copy(_source, queued);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
        await session.Dispatch(async () =>
        {
            using var viewModel = new VideoCompressionViewModel(new Ffmpeg()) { SkipLarger = false };
            await viewModel.AddPathsAsync([_source, next]);
            var running = viewModel.StartCommand.ExecuteAsync(null);
            var import = viewModel.AddPathsAsync([queued]);
            viewModel.Dispose();
            await Task.WhenAll(running, import);
            Assert.AreEqual(2, viewModel.Items.Count);
            Assert.IsTrue(viewModel.Items.All(item => item.Status == ImageCompressionStatus.Cancelled));
            Assert.IsFalse(viewModel.StartCommand.CanExecute(null));
            Assert.IsFalse(viewModel.IsBusy);
            Assert.AreEqual(3, Directory.GetFiles(_directory).Length);
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public void Analyzer_MixedFiles_OffersSingleVideoBatch()
    {
        var analyzer = new VideoAnalyzer();
        var actions = analyzer.AnalyzeInputData(new[] { _source, _source, "missing.mp4", "image.png" }
            .Select(path => new InputData { InputType = InputType.文件, Data = path })).ToArray();
        Assert.AreEqual(1, actions.Length);
        Assert.IsTrue(actions[0].ShowAsMiniApp);
        Assert.AreEqual(0, analyzer.AnalyzeInputData([new InputData { InputType = InputType.文件, Data = "missing.mp4" }]).Count());
    }

    private static async Task<JsonDocument> ProbeAsync(string path) => JsonDocument.Parse(await RunToolAsync("ffprobe",
        "-v", "error", "-show_entries", "stream=codec_type,codec_name,width,height:format=duration,format_name", "-of", "json", "-i", path));

    private static async Task<string> RunToolAsync(string tool, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(VideoCompressor.ToolDirectory, tool + ".exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, await errors);
        return await output;
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
