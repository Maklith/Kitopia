using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Kitopia.Desktop.Features.Imaging;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal static class EmbeddingGemmaImageProcessor
{
    public const int MaximumPatches = 280 * 9;
    private const int PatchSize = 16;
    private const int PoolingSize = 3;
    private const long MaximumDecodedPixels = 64L * 1024 * 1024;

    internal static (int Width, int Height) GetTargetSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        const int multiple = PatchSize * PoolingSize;
        var factor = Math.Sqrt(MaximumPatches * PatchSize * PatchSize / ((double)width * height));
        var targetHeight = (int)Math.Floor(factor * height / multiple) * multiple;
        var targetWidth = (int)Math.Floor(factor * width / multiple) * multiple;
        const int maximumSide = 280 * multiple;
        if (targetHeight == 0)
        {
            targetHeight = multiple;
            targetWidth = Math.Min((int)Math.Floor((double)width / height) * multiple, maximumSide);
        }
        else if (targetWidth == 0)
        {
            targetWidth = multiple;
            targetHeight = Math.Min((int)Math.Floor((double)height / width) * multiple, maximumSide);
        }
        return (targetWidth, targetHeight);
    }

    public static (float[] Pixels, long[] Positions, int SoftTokens) Process(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new DecoderOptions { Configuration = ImageInputLoader.ImageConfiguration, MaxFrames = 1, SkipMetadata = true };
        using var stream = File.OpenRead(path);
        var info = Image.Identify(options, stream);
        var (width, height) = GetTargetSize(info.Width, info.Height);
        stream.Position = 0;
        Image<Rgb24> decoded;
        if ((long)info.Width * info.Height <= MaximumDecodedPixels)
        {
            decoded = Image.Load<Rgb24>(options, stream);
        }
        else if (info.Metadata.DecodedImageFormat == PngFormat.Instance)
        {
            decoded = ImageInputLoader.LoadPngRows(stream, width, height, cancellationToken);
        }
        else if (info.Metadata.DecodedImageFormat == JpegFormat.Instance
                 && ((long)info.Width + 7) / 8 * (((long)info.Height + 7) / 8) <= MaximumDecodedPixels)
        {
            decoded = JpegDecoder.Instance.Decode<Rgb24>(new JpegDecoderOptions
            {
                GeneralOptions = new DecoderOptions
                {
                    Configuration = ImageInputLoader.ImageConfiguration, MaxFrames = 1, SkipMetadata = true,
                    TargetSize = new Size(width, height)
                },
                ResizeMode = JpegDecoderResizeMode.IdctOnly
            }, stream);
        }
        else
        {
            throw new InvalidDataException($"Image '{path}' ({info.Width}x{info.Height}, {info.Metadata.DecodedImageFormat?.Name}) cannot be decoded within the indexing memory budget.");
        }
        using var image = decoded;
        cancellationToken.ThrowIfCancellationRequested();
        // The official uint8 processor rounds and clips between separable resize passes.
        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(width, image.Height),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic,
            Compand = false
        }));
        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(width, height),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic,
            Compand = false
        }));
        cancellationToken.ThrowIfCancellationRequested();
        var patchWidth = width / PatchSize;
        var patchHeight = height / PatchSize;
        var pixels = new float[MaximumPatches * PatchSize * PatchSize * 3];
        var positions = new long[MaximumPatches * 2];
        Array.Fill(positions, -1L);
        image.ProcessPixelRows(accessor =>
        {
            for (var patchY = 0; patchY < patchHeight; patchY++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var patchX = 0; patchX < patchWidth; patchX++)
                {
                    var patch = patchY * patchWidth + patchX;
                    positions[patch * 2] = patchX;
                    positions[patch * 2 + 1] = patchY;
                    for (var y = 0; y < PatchSize; y++)
                    {
                        var row = accessor.GetRowSpan(patchY * PatchSize + y);
                        for (var x = 0; x < PatchSize; x++)
                        {
                            var pixel = row[patchX * PatchSize + x];
                            var offset = patch * 768 + (y * PatchSize + x) * 3;
                            pixels[offset] = pixel.R / 255f;
                            pixels[offset + 1] = pixel.G / 255f;
                            pixels[offset + 2] = pixel.B / 255f;
                        }
                    }
                }
            }
        });
        return (pixels, positions, patchWidth * patchHeight / (PoolingSize * PoolingSize));
    }
}
