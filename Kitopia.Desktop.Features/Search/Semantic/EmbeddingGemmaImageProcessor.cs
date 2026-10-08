using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal static class EmbeddingGemmaImageProcessor
{
    public const int MaximumPatches = 280 * 9;
    private const int PatchSize = 16;
    private const int PoolingSize = 3;

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

    public static (float[] Pixels, long[] Positions, int SoftTokens) Process(string path)
    {
        var options = new DecoderOptions { MaxFrames = 1 };
        var info = Image.Identify(options, path);
        if ((long)info.Width * info.Height > 64L * 1024 * 1024)
            throw new InvalidDataException("The image exceeds the 64-megapixel decoding limit.");
        using var image = Image.Load<Rgb24>(options, path);
        var (width, height) = GetTargetSize(image.Width, image.Height);
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
        var patchWidth = width / PatchSize;
        var patchHeight = height / PatchSize;
        var pixels = new float[MaximumPatches * PatchSize * PatchSize * 3];
        var positions = new long[MaximumPatches * 2];
        Array.Fill(positions, -1L);
        image.ProcessPixelRows(accessor =>
        {
            for (var patchY = 0; patchY < patchHeight; patchY++)
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
        });
        return (pixels, positions, patchWidth * patchHeight / (PoolingSize * PoolingSize));
    }
}
