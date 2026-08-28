using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace KitopiaBenchmark;

[Config(typeof(InProcessConfig))]
[MemoryDiagnoser]
[InvocationCount(256)]
public class ImageSignatureBenchmark
{
    private const int SignatureCount = 1_000_000;
    private const int SignatureByteCount = 12;
    private const ulong GifSignatureMask = 0x0000_FFFF_FFFF_FFFFUL;
    private const ulong Gif87aSignatureLittleEndian = 0x0000_6137_3846_4947UL;
    private const ulong Gif89aSignatureLittleEndian = 0x0000_6139_3846_4947UL;

    private byte[] _signatures = null!;

    [Params(SignatureKind.Gif87a, SignatureKind.Gif89a, SignatureKind.Other, SignatureKind.Mixed)]
    public SignatureKind Kind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _signatures = new byte[SignatureCount * SignatureByteCount];
        for (var index = 0; index < SignatureCount; index++)
        {
            var signature = _signatures.AsSpan(index * SignatureByteCount, SignatureByteCount);
            var kind = Kind == SignatureKind.Mixed
                ? (index & 3) switch
                {
                    2 => SignatureKind.Gif87a,
                    3 => SignatureKind.Gif89a,
                    _ => SignatureKind.Other
                }
                : Kind;
            switch (kind)
            {
                case SignatureKind.Gif87a:
                    WriteGifSignature(signature, (byte)'7', (byte)index);
                    break;
                case SignatureKind.Gif89a:
                    WriteGifSignature(signature, (byte)'9', (byte)index);
                    break;
                default:
                    WriteOtherSignature(signature, (byte)index);
                    break;
            }
        }

        var expected = OriginalSequenceEqualCore(_signatures);
        if (StartsWithCore(_signatures) != expected
            || DirectBytesCore(_signatures) != expected
            || UInt64MaskCore(_signatures) != expected
            || HybridCore(_signatures) != expected)
        {
            throw new InvalidOperationException("Image signature implementations disagree.");
        }
    }

    [Benchmark(Baseline = true, Description = "Original: sliced SequenceEqual", OperationsPerInvoke = SignatureCount)]
    public int OriginalSequenceEqual() => OriginalSequenceEqualCore(_signatures);

    [Benchmark(Description = "StartsWith: no slice", OperationsPerInvoke = SignatureCount)]
    public int StartsWith() => StartsWithCore(_signatures);

    [Benchmark(Description = "Direct: byte comparisons", OperationsPerInvoke = SignatureCount)]
    public int DirectBytes() => DirectBytesCore(_signatures);

    [Benchmark(Description = "Alternative: UInt64 mask", OperationsPerInvoke = SignatureCount)]
    public int UInt64Mask() => UInt64MaskCore(_signatures);

    [Benchmark(Description = "Production: first byte + UInt64 mask", OperationsPerInvoke = SignatureCount)]
    public int Hybrid() => HybridCore(_signatures);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int OriginalSequenceEqualCore(byte[] signatures)
    {
        var result = 0;
        for (var offset = 0; offset < signatures.Length; offset += SignatureByteCount)
        {
            ReadOnlySpan<byte> signature = signatures.AsSpan(offset, SignatureByteCount);
            result += signature[..6].SequenceEqual("GIF87a"u8)
                      || signature[..6].SequenceEqual("GIF89a"u8)
                ? 1
                : 0;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int StartsWithCore(byte[] signatures)
    {
        var result = 0;
        for (var offset = 0; offset < signatures.Length; offset += SignatureByteCount)
        {
            ReadOnlySpan<byte> signature = signatures.AsSpan(offset, SignatureByteCount);
            result += signature.StartsWith("GIF87a"u8)
                      || signature.StartsWith("GIF89a"u8)
                ? 1
                : 0;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int DirectBytesCore(byte[] signatures)
    {
        var result = 0;
        for (var offset = 0; offset < signatures.Length; offset += SignatureByteCount)
        {
            ReadOnlySpan<byte> signature = signatures.AsSpan(offset, SignatureByteCount);
            result += signature[0] == (byte)'G'
                      && signature[1] == (byte)'I'
                      && signature[2] == (byte)'F'
                      && signature[3] == (byte)'8'
                      && (signature[4] == (byte)'7' || signature[4] == (byte)'9')
                      && signature[5] == (byte)'a'
                ? 1
                : 0;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int UInt64MaskCore(byte[] signatures)
    {
        var result = 0;
        for (var offset = 0; offset < signatures.Length; offset += SignatureByteCount)
        {
            ReadOnlySpan<byte> signature = signatures.AsSpan(offset, SignatureByteCount);
            var value = BinaryPrimitives.ReadUInt64LittleEndian(signature) & GifSignatureMask;
            result += value == Gif87aSignatureLittleEndian || value == Gif89aSignatureLittleEndian
                ? 1
                : 0;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int HybridCore(byte[] signatures)
    {
        var result = 0;
        for (var offset = 0; offset < signatures.Length; offset += SignatureByteCount)
        {
            if (signatures[offset] != (byte)'G')
            {
                continue;
            }

            ReadOnlySpan<byte> signature = signatures.AsSpan(offset, SignatureByteCount);
            var value = BinaryPrimitives.ReadUInt64LittleEndian(signature) & GifSignatureMask;
            result += value == Gif87aSignatureLittleEndian || value == Gif89aSignatureLittleEndian
                ? 1
                : 0;
        }

        return result;
    }

    private static void WriteGifSignature(Span<byte> signature, byte version, byte value)
    {
        signature[0] = (byte)'G';
        signature[1] = (byte)'I';
        signature[2] = (byte)'F';
        signature[3] = (byte)'8';
        signature[4] = version;
        signature[5] = (byte)'a';
        signature[6] = 0x01;
        signature[7] = 0x00;
        signature[8] = 0x01;
        signature[9] = 0x00;
        signature[10] = value;
        signature[11] = 0x00;
    }

    private static void WriteOtherSignature(Span<byte> signature, byte value)
    {
        signature[0] = (byte)'P';
        signature[1] = (byte)'N';
        signature[2] = (byte)'G';
        signature[3] = 0x0D;
        signature[4] = 0x0A;
        signature[5] = 0x1A;
        signature[6] = 0x0A;
        signature[7] = value;
        signature[8..].Clear();
    }
}

public enum SignatureKind
{
    Gif87a,
    Gif89a,
    Other,
    Mixed
}
