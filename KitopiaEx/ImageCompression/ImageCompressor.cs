using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PluginCore.Localization;
using PluginCore.Media;

namespace KitopiaEx.ImageCompression;

public sealed class ImageCompressor(Ffmpeg ffmpeg)
{
    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".avif";

    public Task<ImageCompressionResult> CompressAsync(string sourcePath, ImageCompressionOptions options,
        CancellationToken cancellationToken = default) => Task.Run(() => Compress(sourcePath, options, cancellationToken), cancellationToken);

    private ImageCompressionResult Compress(string sourcePath, ImageCompressionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Mode) || !Enum.IsDefined(options.Format) ||
            options.Quality is < 1 or > 100 || options.TargetPercent is < 1 or > 100 ||
            options.ResizePercent is < 1 or > 100 || options.MaxDimension is < 0 or > 32768)
            throw new ArgumentOutOfRangeException(nameof(options));
        sourcePath = Path.GetFullPath(sourcePath);
        if (!IsSupported(sourcePath)) throw new NotSupportedException(Lang.Get("lang.kitopiaex.compression.unsupported"));
        cancellationToken.ThrowIfCancellationRequested();
        var originalBytes = new FileInfo(sourcePath).Length;
        var format = options.Format switch
        {
            ImageCompressionFormat.Original => FfmpegImageFormat.Original,
            ImageCompressionFormat.WebP => FfmpegImageFormat.WebP,
            ImageCompressionFormat.JPEG => FfmpegImageFormat.JPEG,
            ImageCompressionFormat.PNG => FfmpegImageFormat.PNG,
            _ => throw new ArgumentOutOfRangeException(nameof(options))
        };
        using var image = ffmpeg.OpenImage(sourcePath, format, options.ResizePercent, options.MaxDimension, cancellationToken);
        var directory = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.GetDirectoryName(sourcePath)! : Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(directory);
        var extension = image.Format switch
        {
            FfmpegImageFormat.WebP => ".webp",
            FfmpegImageFormat.JPEG => options.Format == ImageCompressionFormat.Original &&
                Path.GetExtension(sourcePath).Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpeg" : ".jpg",
            FfmpegImageFormat.PNG => ".png",
            FfmpegImageFormat.BMP => ".bmp",
            FfmpegImageFormat.AVIF => ".avif",
            _ => throw new NotSupportedException(Lang.Get("lang.kitopiaex.compression.unsupported"))
        };
        var temporary = Path.Combine(directory, $".kitopia-{Guid.NewGuid():N}{extension}");
        var candidate = Path.Combine(directory, $".kitopia-{Guid.NewGuid():N}{extension}");
        var targetBytes = Math.Max(1, (long)(originalBytes * (options.TargetPercent / 100.0)));
        try
        {
            if (options.Mode == ImageCompressionMode.Quality || image.Format == FfmpegImageFormat.BMP ||
                options.Lossless && image.Format is FfmpegImageFormat.PNG or FfmpegImageFormat.WebP)
            {
                image.Encode(temporary, options.Quality, options.Lossless, cancellationToken);
            }
            else
            {
                // Keep the highest tested quality under budget, or quality 1 when the budget is unattainable.
                image.Encode(temporary, 1, options.Lossless, cancellationToken);
                if (new FileInfo(temporary).Length <= targetBytes)
                {
                    var low = 2;
                    var high = 100;
                    while (low <= high)
                    {
                        var quality = (low + high) / 2;
                        image.Encode(candidate, quality, options.Lossless, cancellationToken);
                        if (new FileInfo(candidate).Length <= targetBytes)
                        {
                            File.Move(candidate, temporary, overwrite: true);
                            low = quality + 1;
                        }
                        else high = quality - 1;
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var outputBytes = new FileInfo(temporary).Length;
            var targetMet = options.Mode != ImageCompressionMode.TargetSize || outputBytes <= targetBytes;
            if (options.SkipLarger && outputBytes >= originalBytes)
                return new ImageCompressionResult(null, originalBytes, outputBytes, image.OriginalWidth, image.OriginalHeight, targetMet);

            var name = Path.GetFileNameWithoutExtension(sourcePath) + "_compressed";
            var outputPath = Path.Combine(directory, name + extension);
            for (var suffix = 2; ; suffix++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Move(temporary, outputPath); // Atomic publication, with no overwrite of concurrent outputs.
                    break;
                }
                catch (IOException) when (File.Exists(outputPath))
                {
                    outputPath = Path.Combine(directory, $"{name}_{suffix}{extension}");
                }
            }
            return new ImageCompressionResult(outputPath, originalBytes, outputBytes,
                image.Width, image.Height, targetMet);
        }
        finally
        {
            File.Delete(temporary);
            File.Delete(candidate);
        }
    }
}
