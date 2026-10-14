using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Hjg.Pngcs;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Size = OpenCvSharp.Size;

namespace Kitopia.Desktop.Features.Imaging;

/// <summary>
/// Loads images for indexing without allowing compressed files to turn into unbounded
/// OpenCV and ONNX tensors.
/// </summary>
internal static class ImageInputLoader
{
    public const int MaximumEmbeddingPixels = 4 * 1024 * 1024;
    // PaddleOCR detector workspace grows with the complete image area. One megapixel is enough
    // for document text while keeping a single inference bounded on CPU runtimes that retain
    // dynamic-shape buffers internally.
    public const int MaximumOcrPixels = 1024 * 1024;

    private const long MaximumDecodedPixels = 64L * 1024 * 1024;
    private const int HeaderProbeBytes = 1024 * 1024;
    private const ulong GifSignatureMask = 0x0000_FFFF_FFFF_FFFFUL;
    private const ulong Gif87aSignatureLittleEndian = 0x0000_6137_3846_4947UL;
    private const ulong Gif89aSignatureLittleEndian = 0x0000_6139_3846_4947UL;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    internal static readonly Configuration ImageConfiguration = Configuration.Default.Clone();

    static ImageInputLoader()
    {
        ImageConfiguration.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
        {
            AllocationLimitMegabytes = 256,
            MaximumPoolSizeMegabytes = 16
        });
    }

    public static Mat LoadBgr(string path, int maximumPixels, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPixels, 1);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadDimensions(path, out var dimensions, out var isJpeg, out var isPng))
        {
            throw new InvalidDataException($"Unable to read supported image dimensions from '{path}'.");
        }

        if ((long)dimensions.Width * dimensions.Height > MaximumDecodedPixels && !isJpeg)
        {
            var scale = Math.Sqrt(maximumPixels / ((double)dimensions.Width * dimensions.Height));
            var width = Math.Max(1, (int)Math.Floor(dimensions.Width * scale));
            var height = Math.Max(1, (int)Math.Floor(dimensions.Height * scale));
            if ((long)width * height > maximumPixels)
            {
                if (width >= height) width = maximumPixels / height;
                else height = maximumPixels / width;
            }
            if (!isPng || (long)width * dimensions.Height > MaximumDecodedPixels)
                throw new InvalidDataException(
                    $"Image '{path}' is too large for bounded indexing ({dimensions.Width}x{dimensions.Height}).");
            using var stream = File.OpenRead(path);
            using var rgb = LoadPngRows(stream, width, height, cancellationToken);
            var bgr = new Mat(height, width, MatType.CV_8UC3);
            try
            {
                unsafe
                {
                    rgb.ProcessPixelRows(accessor =>
                    {
                        for (var row = 0; row < height; row++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            accessor.GetRowSpan(row).CopyTo(new Span<Rgb24>((void*)bgr.Ptr(row), width));
                        }
                    });
                }
                Cv2.CvtColor(bgr, bgr, ColorConversionCodes.RGB2BGR);
                return bgr;
            }
            catch
            {
                bgr.Dispose();
                throw;
            }
        }

        var readMode = SelectReadMode(isJpeg, dimensions, maximumPixels);
        if (isJpeg && EstimateDecodedPixels(dimensions, readMode) >= MaximumDecodedPixels)
        {
            throw new InvalidDataException(
                $"Image '{path}' is too large for bounded decoding ({dimensions.Width}x{dimensions.Height}).");
        }

        // OpenCvSharp's string overload marshals the path through an ANSI P/Invoke on
        // Windows, which fails for valid Unicode paths. Decode the file bytes instead.
        var image = Cv2.ImDecode(File.ReadAllBytes(path), readMode);
        if (image.Empty())
        {
            image.Dispose();
            throw new InvalidDataException($"Unable to decode image '{path}'.");
        }

        try
        {
            var pixels = checked((long)image.Rows * image.Cols);
            if (pixels > MaximumDecodedPixels)
            {
                throw new InvalidDataException(
                    $"Decoded image '{path}' exceeds the {MaximumDecodedPixels:N0}-pixel safety limit.");
            }

            var resized = ResizeToMaximumPixels(image, maximumPixels);
            if (resized is null)
            {
                return image;
            }

            image.Dispose();
            return resized;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Returns an owned resized image only when a resize is needed; callers keep the source
    /// alive when this returns null.
    /// </summary>
    public static Mat? ResizeToMaximumPixels(Mat source, int maximumPixels)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPixels, 1);
        if (source.Empty())
        {
            return null;
        }

        var pixels = checked((long)source.Rows * source.Cols);
        if (pixels <= maximumPixels)
        {
            return null;
        }

        var scale = Math.Sqrt(maximumPixels / (double)pixels);
        var width = Math.Max(1, (int)Math.Floor(source.Cols * scale));
        var height = Math.Max(1, (int)Math.Floor(source.Rows * scale));
        while ((long)width * height > maximumPixels)
        {
            if (width >= height)
            {
                width--;
            }
            else
            {
                height--;
            }
        }

        var result = new Mat();
        Cv2.Resize(source, result, new Size(width, height), 0d, 0d, InterpolationFlags.Area);
        return result;
    }

    internal static bool TryReadDimensions(string path, out Size size) =>
        TryReadDimensions(path, out size, out _, out _);

    private static bool TryReadDimensions(string path, out Size size, out bool isJpeg, out bool isPng)
    {
        size = default;
        isJpeg = false;
        isPng = false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: false);
            Span<byte> signature = stackalloc byte[12];
            if (stream.Length < signature.Length)
            {
                return false;
            }

            stream.ReadExactly(signature);
            if (signature[..8].SequenceEqual(PngSignature))
            {
                isPng = true;
                stream.Position = 0;
                return TryReadPngDimensions(stream, out size);
            }

            if (signature[0] == (byte)'G')
            {
                var gifSignature = BinaryPrimitives.ReadUInt64LittleEndian(signature) & GifSignatureMask;
                if (gifSignature == Gif87aSignatureLittleEndian || gifSignature == Gif89aSignatureLittleEndian)
                {
                    var width = BinaryPrimitives.ReadUInt16LittleEndian(signature[6..8]);
                    var height = BinaryPrimitives.ReadUInt16LittleEndian(signature[8..10]);
                    return TryCreateSize(width, height, out size);
                }
            }

            stream.Position = 0;
            if (signature[0] == 0xFF && signature[1] == 0xD8)
            {
                isJpeg = true;
                return TryReadJpegDimensions(stream, out size);
            }

            if (signature[0] == (byte)'B' && signature[1] == (byte)'M')
            {
                return TryReadBmpDimensions(stream, out size);
            }

            return signature[..4].SequenceEqual("RIFF"u8)
                   && signature[8..12].SequenceEqual("WEBP"u8)
                   && TryReadWebpDimensions(stream, out size);
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException
                                         or ArgumentException
                                         or System.Security.SecurityException)
        {
            return false;
        }
    }

    internal static Image<Rgb24> LoadPngRows(Stream stream, int targetWidth, int targetHeight, CancellationToken cancellationToken)
    {
        PngReader? reader = null;
        Image<Rgb24>? resized = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            reader = new PngReader(stream) { ShouldCloseStream = false, MaxTotalBytesRead = stream.Length };
            reader.SetUnpackedMode(true);
            var info = reader.ImgInfo;
            if (reader.IsInterlaced() || (long)targetWidth * info.Rows > MaximumDecodedPixels)
                throw new InvalidDataException($"PNG ({info.Cols}x{info.Rows}) cannot be streamed within the indexing memory budget (interlaced: {reader.IsInterlaced()}).");
            resized = new Image<Rgb24>(ImageConfiguration, targetWidth, info.Rows);
            var blockRows = Math.Min(info.Rows, Math.Min(64, Math.Max(1, 1024 * 1024 / info.Cols)));
            var buffer = new Rgb24[info.Cols * blockRows];
            var palette = info.Indexed ? reader.GetMetadata().GetPLTE() : null;
            var maximumSample = (1 << Math.Min(8, info.BitDepth)) - 1;
            // Horizontal resize is row-independent; streaming blocks preserves the official two-pass rounding.
            for (var startRow = 0; startRow < info.Rows; startRow += blockRows)
            {
                var count = Math.Min(blockRows, info.Rows - startRow);
                for (var row = 0; row < count; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var samples = reader.ReadRowByte(startRow + row).ScanlineB;
                    var pixels = buffer.AsSpan(row * info.Cols, info.Cols);
                    if (!info.Indexed && !info.Greyscale && !info.Alpha)
                    {
                        samples.AsSpan().CopyTo(MemoryMarshal.AsBytes(pixels));
                        continue;
                    }
                    for (var column = 0; column < info.Cols; column++)
                    {
                        var offset = column * info.Channels;
                        if (info.Indexed)
                        {
                            var color = palette!.GetEntry(samples[offset]);
                            pixels[column] = new Rgb24((byte)(color >> 16), (byte)(color >> 8), (byte)color);
                        }
                        else if (info.Greyscale)
                        {
                            var value = (byte)(samples[offset] * 255 / maximumSample);
                            pixels[column] = new Rgb24(value, value, value);
                        }
                        else
                        {
                            pixels[column] = new Rgb24(samples[offset], samples[offset + 1], samples[offset + 2]);
                        }
                    }
                }
                using var block = Image.WrapMemory<Rgb24>(ImageConfiguration, buffer.AsMemory(0, info.Cols * count), info.Cols, count);
                using var scaledBlock = block.Clone(context => context.Resize(new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(targetWidth, count), Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.Bicubic, Compand = false
                }));
                scaledBlock.ProcessPixelRows(resized, (source, destination) =>
                {
                    for (var row = 0; row < count; row++)
                        source.GetRowSpan(row).CopyTo(destination.GetRowSpan(startRow + row));
                });
            }
            cancellationToken.ThrowIfCancellationRequested();
            resized.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(targetWidth, targetHeight), Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Bicubic, Compand = false
            }));
            return resized;
        }
        catch (PngjException exception)
        {
            resized?.Dispose();
            throw new InvalidDataException("Unable to stream the PNG image.", exception);
        }
        catch
        {
            resized?.Dispose();
            throw;
        }
        finally
        {
            reader?.End();
        }
    }

    private static ImreadModes SelectReadMode(bool isJpeg, Size dimensions, int maximumPixels)
    {
        if (!isJpeg || dimensions.Width <= 0 || dimensions.Height <= 0)
        {
            return ImreadModes.Color;
        }

        var pixels = (long)dimensions.Width * dimensions.Height;
        var limit = (long)maximumPixels;
        return pixels > limit * 16
            ? ImreadModes.ReducedColor8
            : pixels > limit * 4
                ? ImreadModes.ReducedColor4
                : pixels > limit
                    ? ImreadModes.ReducedColor2
                    : ImreadModes.Color;
    }

    private static long EstimateDecodedPixels(Size dimensions, ImreadModes mode)
    {
        var divisor = mode switch
        {
            ImreadModes.ReducedColor2 => 2,
            ImreadModes.ReducedColor4 => 4,
            ImreadModes.ReducedColor8 => 8,
            _ => 1
        };
        var width = ((long)dimensions.Width + divisor - 1) / divisor;
        var height = ((long)dimensions.Height + divisor - 1) / divisor;
        return checked(width * height);
    }

    private static bool TryReadPngDimensions(FileStream stream, out Size size)
    {
        size = default;
        Span<byte> header = stackalloc byte[24];
        if (stream.Length < header.Length)
        {
            return false;
        }

        stream.ReadExactly(header);
        if (!header[..8].SequenceEqual(PngSignature)
            || !header[12..16].SequenceEqual("IHDR"u8))
        {
            return false;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        return TryCreateSize(width, height, out size);
    }

    private static bool TryReadBmpDimensions(FileStream stream, out Size size)
    {
        size = default;
        Span<byte> header = stackalloc byte[26];
        if (stream.Length < header.Length)
        {
            return false;
        }

        stream.ReadExactly(header);
        if (header[0] != (byte)'B' || header[1] != (byte)'M')
        {
            return false;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(header[18..22]);
        var height = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(header[22..26]));
        return width > 0 && height <= int.MaxValue && TryCreateSize((uint)width, (uint)height, out size);
    }

    private static bool TryReadJpegDimensions(FileStream stream, out Size size)
    {
        size = default;
        if (stream.Length < 4 || stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
        {
            return false;
        }

        while (stream.Position < Math.Min(stream.Length, HeaderProbeBytes))
        {
            if (ReadNextJpegMarker(stream) is not { } marker)
            {
                return false;
            }

            if (marker == 0xD8)
            {
                // Some camera/MJPEG files contain a second SOI marker before the
                // actual frame header. Keep scanning for the first valid SOF.
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                return false;
            }

            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                continue;
            }

            var high = stream.ReadByte();
            var low = stream.ReadByte();
            var segmentLength = high < 0 || low < 0 ? 0 : (high << 8) | low;
            if (segmentLength < 2 || stream.Position + segmentLength - 2 > stream.Length)
            {
                return false;
            }

            if (IsJpegStartOfFrame(marker))
            {
                if (segmentLength < 7 || stream.ReadByte() < 0)
                {
                    return false;
                }

                var frameHeight = (stream.ReadByte() << 8) | stream.ReadByte();
                var frameWidth = (stream.ReadByte() << 8) | stream.ReadByte();
                return frameWidth > 0 && frameHeight > 0
                       && TryCreateSize((uint)frameWidth, (uint)frameHeight, out size);
            }

            stream.Position += segmentLength - 2;
        }

        return false;
    }

    private static bool TryReadWebpDimensions(FileStream stream, out Size size)
    {
        size = default;
        Span<byte> header = stackalloc byte[30];
        if (stream.Length < 30)
        {
            return false;
        }

        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WEBP"u8))
        {
            return false;
        }

        if (header[12..16].SequenceEqual("VP8X"u8))
        {
            var width = 1u + (uint)(header[24] | header[25] << 8 | header[26] << 16);
            var height = 1u + (uint)(header[27] | header[28] << 8 | header[29] << 16);
            return TryCreateSize((uint)width, (uint)height, out size);
        }

        if (header[12..16].SequenceEqual("VP8L"u8) && header[20] == 0x2F)
        {
            var width = 1u + (uint)(header[21] | (header[22] & 0x3F) << 8);
            var height = 1u + (uint)((header[22] >> 6 & 0x03) | header[23] << 2 | (header[24] & 0x0F) << 10);
            return TryCreateSize(width, height, out size);
        }

        if (!header[12..16].SequenceEqual("VP8 "u8))
        {
            return false;
        }

        var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(header[16..20]);
        var probeLength = (int)Math.Min(chunkLength, HeaderProbeBytes - 20);
        if (probeLength < 10 || stream.Length < 20 + probeLength)
        {
            return false;
        }

        var payload = new byte[probeLength];
        stream.Position = 20;
        stream.ReadExactly(payload);
        for (var index = 0; index + 9 < payload.Length; index++)
        {
            if (payload[index] is not 0x9D || payload[index + 1] != 0x01 || payload[index + 2] != 0x2A)
            {
                continue;
            }

            var width = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(index + 3, 2)) & 0x3FFF;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(index + 5, 2)) & 0x3FFF;
            return TryCreateSize((uint)width, (uint)height, out size);
        }

        return false;
    }

    private static int? ReadNextJpegMarker(FileStream stream)
    {
        int value;
        do
        {
            value = stream.ReadByte();
        } while (value >= 0 && value != 0xFF);

        if (value < 0)
        {
            return null;
        }

        do
        {
            value = stream.ReadByte();
        } while (value == 0xFF);

        return value <= 0 ? null : value;
    }

    private static bool IsJpegStartOfFrame(int marker) =>
        marker is >= 0xC0 and <= 0xC3
            or >= 0xC5 and <= 0xC7
            or >= 0xC9 and <= 0xCB
            or >= 0xCD and <= 0xCF;

    private static bool TryCreateSize(uint width, uint height, out Size size)
    {
        size = default;
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
        {
            return false;
        }

        size = new Size((int)width, (int)height);
        return true;
    }
}
