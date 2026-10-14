using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PluginCore.Localization;

namespace KitopiaEx.VideoCompression;

public sealed class VideoCompressor
{
    public static string ToolDirectory => Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");

    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".m4v" or ".wmv" or ".flv" or ".ts" or ".mts" or ".m2ts";

    public async Task<VideoCompressionResult> CompressAsync(string sourcePath, VideoCompressionOptions options,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);
        var isConversion = options.Mode == VideoCompressionMode.Conversion;
        var copyVideo = isConversion && options.Codec == VideoCompressionCodec.KeepOriginal;
        if (!Enum.IsDefined(options.Mode) || !Enum.IsDefined(options.Format) || !Enum.IsDefined(options.Codec) ||
            (!copyVideo && options.Quality is < 1 or > 100) ||
            (!isConversion && (options.TargetPercent is < 1 or > 100 ||
                options.ResizePercent is < 1 or > 100 || options.MaxDimension is < 0 or > 32768)))
            throw new ArgumentOutOfRangeException(nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        sourcePath = Path.GetFullPath(sourcePath);
        if (!IsSupported(sourcePath)) throw new NotSupportedException(Lang.Get("lang.kitopiaex.video_compression.unsupported"));
        var originalBytes = new FileInfo(sourcePath).Length;
        var probe = await RunToolAsync("ffprobe", ["-v", "error", "-show_entries",
            "stream=index,codec_type,codec_name,width,height,duration,color_transfer,color_primaries,color_space,color_range:stream_side_data=rotation:stream_disposition=attached_pic:format=duration", "-of", "json", "-i", sourcePath],
            null, cancellationToken);
        using var document = JsonDocument.Parse(probe);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray();
        var video = streams.FirstOrDefault(stream => stream.GetProperty("codec_type").GetString() == "video" &&
            (!stream.TryGetProperty("disposition", out var disposition) || disposition.GetProperty("attached_pic").GetInt32() == 0));
        if (video.ValueKind == JsonValueKind.Undefined || !video.TryGetProperty("width", out var width) ||
            !video.TryGetProperty("height", out var height) || width.GetInt32() < 2 || height.GetInt32() < 2)
            throw new InvalidDataException(Lang.Get("lang.kitopiaex.video_compression.invalid_video"));
        var durationText = video.TryGetProperty("duration", out var videoDuration) ? videoDuration.GetString() : null;
        if ((!double.TryParse(durationText, CultureInfo.InvariantCulture, out var duration) || duration <= 0) &&
            document.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var formatDuration))
            double.TryParse(formatDuration.GetString(), CultureInfo.InvariantCulture, out duration);
        if (!double.IsFinite(duration) || duration <= 0)
            throw new InvalidDataException(Lang.Get("lang.kitopiaex.video_compression.invalid_video"));
        var codec = options.Codec;
        if (!copyVideo && codec == VideoCompressionCodec.KeepOriginal)
            codec = video.GetProperty("codec_name").GetString() switch
            {
                "h264" => VideoCompressionCodec.H264,
                "hevc" => VideoCompressionCodec.HEVC,
                "vp9" => VideoCompressionCodec.VP9,
                "av1" => VideoCompressionCodec.AV1,
                var name => throw new NotSupportedException(Lang.Format("lang.kitopiaex.video_compression.original_codec_unsupported", name))
            };
        var extension = options.Format switch
        {
            VideoCompressionFormat.MP4 => ".mp4",
            VideoCompressionFormat.WebM => ".webm",
            VideoCompressionFormat.MKV => ".mkv",
            VideoCompressionFormat.MOV => ".mov",
            _ => Path.GetExtension(sourcePath).ToLowerInvariant()
        };
        var muxer = extension switch
        {
            ".mp4" or ".m4v" => "mp4",
            ".mov" => "mov",
            ".mkv" => "matroska",
            ".webm" => "webm",
            ".avi" => "avi",
            ".wmv" => "asf",
            ".flv" => "flv",
            _ => "mpegts"
        };
        if (!copyVideo && ((muxer == "webm" && codec is VideoCompressionCodec.H264 or VideoCompressionCodec.HEVC) ||
            (muxer == "mov" && codec is VideoCompressionCodec.VP9 or VideoCompressionCodec.AV1)))
            throw new NotSupportedException(Lang.Format("lang.kitopiaex.video_compression.incompatible_codec", muxer, codec));
        var audioStreamCount = isConversion || options.KeepAudio
            ? streams.Count(stream => stream.GetProperty("codec_type").GetString() == "audio") : 0;
        var copyAudio = isConversion && (options.Format == VideoCompressionFormat.KeepOriginal ||
            extension == Path.GetExtension(sourcePath).ToLowerInvariant() || muxer == "matroska" ||
            streams.Where(stream => stream.GetProperty("codec_type").GetString() == "audio")
            .All(stream => muxer switch
            {
                "webm" => stream.GetProperty("codec_name").GetString() is "opus" or "vorbis",
                "mp4" or "mov" => stream.GetProperty("codec_name").GetString() is "aac" or "mp3" or "ac3" or "eac3" or "alac",
                _ => options.Format == VideoCompressionFormat.KeepOriginal
            }));
        var transfer = video.TryGetProperty("color_transfer", out var colorTransfer) ? colorTransfer.GetString() : null;
        var isHdr = transfer is "smpte2084" or "arib-std-b67";
        if (!copyVideo && isHdr && !options.ConvertHdrToSdr && codec == VideoCompressionCodec.H264)
            throw new NotSupportedException(Lang.Get("lang.kitopiaex.video_compression.hdr_codec_required"));
        var hevcEncoder = "libkvazaar";
        if (!copyVideo && codec == VideoCompressionCodec.HEVC && isHdr && !options.ConvertHdrToSdr)
        {
            var diagnostics = new StringBuilder();
            foreach (var encoder in new[] { "hevc_nvenc", "hevc_qsv", "hevc_amf" })
            {
                try
                {
                    await RunToolAsync("ffmpeg", ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i",
                        "color=size=192x192:rate=1", "-frames:v", "1", "-pix_fmt", "p010le", "-c:v", encoder,
                        "-profile:v", "main10", "-f", "null", "-"], null, cancellationToken);
                    hevcEncoder = encoder;
                    break;
                }
                catch (InvalidDataException exception) { diagnostics.AppendLine(exception.Message); }
            }
            if (hevcEncoder == "libkvazaar")
                throw new NotSupportedException(Lang.Get("lang.kitopiaex.video_compression.hevc_hdr_unavailable") +
                    Environment.NewLine + diagnostics);
        }
        var directory = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.GetDirectoryName(sourcePath)! : Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".kitopia-{Guid.NewGuid():N}{extension}");
        var targetBytes = Math.Max(1, (long)(originalBytes * (options.TargetPercent / 100.0)));
        var totalBitrate = targetBytes * 8.0 / duration * 0.94;
        var audioBitrate = audioStreamCount > 0 ? options.Mode == VideoCompressionMode.TargetSize
            ? (int)Math.Clamp(totalBitrate * 0.15 / audioStreamCount, 16000, 128000) : 128000 : 0;
        var videoBitrate = Math.Max(1000, totalBitrate - audioBitrate * audioStreamCount);
        try
        {
            // A bounded retry compensates for container overhead and short clips without repeated quality searches.
            var attempts = options.Mode == VideoCompressionMode.TargetSize ? 2 : 1;
            long outputBytes = 0;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var arguments = new List<string>
                {
                    "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", sourcePath, "-map_metadata", "0"
                };
                if (isConversion)
                {
                    // Encode the primary video first while copying the remaining streams in their original order.
                    if (copyVideo) arguments.AddRange(["-map", "0"]);
                    else
                    {
                        arguments.AddRange(["-map", "0:V:0"]);
                        foreach (var stream in streams)
                            if (stream.GetProperty("index").GetInt32() != video.GetProperty("index").GetInt32())
                                arguments.AddRange(["-map", "0:" + stream.GetProperty("index").GetInt32().ToString(CultureInfo.InvariantCulture)]);
                    }
                    arguments.AddRange(["-map_chapters", "0", "-c", "copy"]);
                }
                else arguments.AddRange(["-map", "0:V:0", "-sn", "-dn"]);
                if (!copyVideo)
                {
                    arguments.AddRange(["-threads:v:0", Math.Min(Environment.ProcessorCount, 8).ToString(CultureInfo.InvariantCulture)]);
                    // Scale after FFmpeg's autorotation, preserve aspect ratio, and use even dimensions for 4:2:0.
                    var displayWidth = width.GetInt32();
                    var displayHeight = height.GetInt32();
                    if (video.TryGetProperty("side_data_list", out var sideData) && sideData.EnumerateArray()
                        .Any(data => data.TryGetProperty("rotation", out var rotation) && Math.Abs(rotation.GetDouble()) % 180 == 90))
                        (displayWidth, displayHeight) = (displayHeight, displayWidth);
                    var ratio = isConversion ? 1 : options.ResizePercent / 100.0;
                    if (!isConversion && options.MaxDimension > 0)
                        ratio = Math.Min(ratio, (double)options.MaxDimension / Math.Max(displayWidth, displayHeight));
                    var outputWidth = Math.Max(2, (int)(displayWidth * ratio / 2) * 2);
                    var outputHeight = Math.Max(2, (int)(displayHeight * ratio / 2) * 2);
                    var filter = $"scale={outputWidth}:{outputHeight}";
                    if (isHdr && options.ConvertHdrToSdr)
                    {
                        filter += $",zscale=pin=bt2020:tin={transfer}:min=bt2020nc:t=linear:npl=100,format=gbrpf32le," +
                            "tonemap=hable,zscale=p=bt709:t=bt709:m=bt709:r=tv";
                        arguments.AddRange(["-color_primaries:v:0", "bt709", "-color_trc:v:0", "bt709", "-colorspace:v:0", "bt709", "-color_range:v:0", "tv"]);
                    }
                    else if (isHdr)
                    {
                        arguments.AddRange(["-color_trc:v:0", transfer!]);
                        foreach (var (property, argument) in new[] { ("color_primaries", "-color_primaries"),
                        ("color_space", "-colorspace"), ("color_range", "-color_range") })
                            if (video.TryGetProperty(property, out var value) && value.GetString() is { } color && color != "unknown")
                                arguments.AddRange([argument + ":v:0", color]);
                    }
                    if (codec == VideoCompressionCodec.HEVC && hevcEncoder == "libkvazaar")
                    {
                        // Kvazaar requires multiples of 8; crop the padding in the HEVC bitstream to retain display dimensions.
                        filter += ",pad=ceil(iw/8)*8:ceil(ih/8)*8";
                        arguments.AddRange(["-bsf:v:0", $"hevc_metadata=crop_right={(8 - outputWidth % 8) % 8}:crop_bottom={(8 - outputHeight % 8) % 8}"]);
                    }
                    arguments.AddRange(["-filter:v:0", filter, "-pix_fmt:v:0", isHdr && !options.ConvertHdrToSdr
                        ? codec == VideoCompressionCodec.HEVC ? "p010le" : "yuv420p10le" : "yuv420p"]);
                    if (codec == VideoCompressionCodec.H264)
                    {
                        arguments.AddRange(["-c:v:0", "libopenh264", "-profile:v:0", "high"]);
                        if (options.Mode != VideoCompressionMode.TargetSize)
                        {
                            var qp = (int)Math.Round(51 - (options.Quality - 1) * 41 / 99.0);
                            arguments.AddRange(["-rc_mode:v:0", "quality", "-qmin:v:0", qp.ToString(CultureInfo.InvariantCulture),
                            "-qmax:v:0", qp.ToString(CultureInfo.InvariantCulture)]);
                        }
                        else arguments.AddRange(["-rc_mode:v:0", "bitrate", "-b:v:0", ((long)videoBitrate).ToString(CultureInfo.InvariantCulture)]);
                    }
                    else if (codec == VideoCompressionCodec.HEVC)
                    {
                        var qp = ((int)Math.Round(51 - (options.Quality - 1) * 41 / 99.0)).ToString(CultureInfo.InvariantCulture);
                        arguments.AddRange(["-c:v:0", hevcEncoder]);
                        if (hevcEncoder == "libkvazaar")
                            arguments.AddRange(["-kvazaar-params:v:0", options.Mode == VideoCompressionMode.TargetSize
                                ? "preset=fast" : "preset=fast,qp=" + qp]);
                        else
                        {
                            arguments.AddRange(["-profile:v:0", "main10"]);
                            switch (hevcEncoder)
                            {
                                case "hevc_nvenc":
                                    arguments.AddRange(["-preset:v:0", "p4", "-rc:v:0",
                                        options.Mode == VideoCompressionMode.TargetSize ? "vbr" : "constqp"]);
                                    if (options.Mode != VideoCompressionMode.TargetSize) arguments.AddRange(["-qp:v:0", qp]);
                                    break;
                                case "hevc_qsv":
                                    arguments.AddRange(["-preset:v:0", "fast"]);
                                    if (options.Mode != VideoCompressionMode.TargetSize) arguments.AddRange(["-global_quality:v:0", qp]);
                                    break;
                                case "hevc_amf":
                                    arguments.AddRange(["-quality:v:0", "balanced", "-rc:v:0",
                                        options.Mode == VideoCompressionMode.TargetSize ? "vbr_peak" : "cqp"]);
                                    if (options.Mode != VideoCompressionMode.TargetSize) arguments.AddRange(["-qp_i:v:0", qp, "-qp_p:v:0", qp]);
                                    break;
                            }
                        }
                        if (options.Mode == VideoCompressionMode.TargetSize)
                            arguments.AddRange(["-b:v:0", ((long)videoBitrate).ToString(CultureInfo.InvariantCulture)]);
                        if (muxer is "mp4" or "mov") arguments.AddRange(["-tag:v:0", "hvc1"]);
                    }
                    else if (codec == VideoCompressionCodec.VP9)
                    {
                        arguments.AddRange(["-c:v:0", "libvpx-vp9", "-deadline:v:0", "good", "-cpu-used:v:0", "4", "-row-mt:v:0", "1"]);
                        if (options.Mode != VideoCompressionMode.TargetSize)
                            arguments.AddRange(["-b:v:0", "0", "-crf:v:0", ((int)Math.Round(63 - (options.Quality - 1) * 53 / 99.0)).ToString(CultureInfo.InvariantCulture)]);
                        else arguments.AddRange(["-b:v:0", ((long)videoBitrate).ToString(CultureInfo.InvariantCulture)]);
                    }
                    else
                    {
                        // SVT's CBR requires low-delay prediction to avoid GOP-based overshoot on short clips.
                        arguments.AddRange(["-c:v:0", "libsvtav1", "-preset:v:0", "8", "-svtav1-params:v:0",
                            options.Mode != VideoCompressionMode.TargetSize ? "lp=3" : "lp=3:rc=2:pred-struct=1"]);
                        if (options.Mode != VideoCompressionMode.TargetSize)
                            arguments.AddRange(["-crf:v:0", ((int)Math.Round(63 - (options.Quality - 1) * 53 / 99.0)).ToString(CultureInfo.InvariantCulture)]);
                        else arguments.AddRange(["-b:v:0", ((long)videoBitrate).ToString(CultureInfo.InvariantCulture)]);
                    }
                    if (audioStreamCount > 0)
                    {
                        if (!isConversion) arguments.AddRange(["-map", "0:a"]);
                        if (!copyAudio) arguments.AddRange(["-c:a", muxer is "webm" or "matroska" ? "libopus" : "aac",
                            "-b:a", audioBitrate.ToString(CultureInfo.InvariantCulture)]);
                    }
                    else if (!isConversion) arguments.Add("-an");
                }
                if (muxer is "mp4" or "mov")
                    arguments.AddRange(["-movflags", isConversion ? "+faststart+use_metadata_tags" : "+faststart"]);
                arguments.AddRange(["-progress", "pipe:1", "-f", muxer, temporary]);
                var currentAttempt = attempt;
                try
                {
                    await RunToolAsync("ffmpeg", arguments, line =>
                    {
                        const string prefix = "out_time_us=";
                        if (line.StartsWith(prefix, StringComparison.Ordinal) &&
                            long.TryParse(line.AsSpan(prefix.Length), CultureInfo.InvariantCulture, out var microseconds))
                            progress?.Report(Math.Clamp((currentAttempt + microseconds / 1000000.0 / duration) / attempts, 0, 0.99));
                    }, cancellationToken);
                }
                catch (InvalidDataException exception) when (isConversion)
                {
                    throw new InvalidDataException(Lang.Get("lang.kitopiaex.video_compression.convert_failed") +
                        Environment.NewLine + exception.Message, exception);
                }
                outputBytes = new FileInfo(temporary).Length;
                if (outputBytes == 0) throw new InvalidDataException(Lang.Get("lang.kitopiaex.video_compression.invalid_video"));
                if (options.Mode != VideoCompressionMode.TargetSize || outputBytes <= targetBytes) break;
                videoBitrate = Math.Max(1000, videoBitrate * targetBytes / outputBytes * 0.9);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var targetMet = options.Mode != VideoCompressionMode.TargetSize || outputBytes <= targetBytes;
            if (!isConversion && options.SkipLarger && outputBytes >= originalBytes)
                return new VideoCompressionResult(null, originalBytes, outputBytes, targetMet);
            var name = Path.GetFileNameWithoutExtension(sourcePath) + (isConversion ? "_converted" : "_compressed");
            var outputPath = Path.Combine(directory, name + extension);
            for (var suffix = 2; ; suffix++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { File.Move(temporary, outputPath); break; }
                catch (IOException) when (File.Exists(outputPath))
                {
                    outputPath = Path.Combine(directory, $"{name}_{suffix}{extension}");
                }
            }
            progress?.Report(1);
            return new VideoCompressionResult(outputPath, originalBytes, outputBytes, targetMet);
        }
        finally { File.Delete(temporary); }
    }

    private static async Task<string> RunToolAsync(string tool, IReadOnlyList<string> arguments,
        Action<string>? onOutput, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(Path.Combine(ToolDirectory, tool + ".exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start bundled FFmpeg.");
        using var registration = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var output = new StringBuilder();
        var errors = new Queue<string>();
        var readOutput = ReadOutputAsync();
        var readErrors = ReadErrorsAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(readOutput, readErrors);
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidDataException($"FFmpeg ({process.ExitCode}): {string.Join(Environment.NewLine, errors)}");
        return output.ToString();

        async Task ReadOutputAsync()
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
                if (onOutput == null) output.AppendLine(line);
                else onOutput(line);
        }

        async Task ReadErrorsAsync()
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                if (errors.Count == 32) errors.Dequeue();
                errors.Enqueue(line);
            }
        }
    }
}
