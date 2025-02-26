using System.Buffers;
using BenchmarkDotNet.Attributes;
using Core.Window;
using PluginCore;

namespace KitopiaBenchmark;
[MemoryDiagnoser]
public class ScreenCapture
{
    private ScreenCaptureByWGC _screenCaptureByWgc;
    private ScreenCaptureByWGCWithoutPool _screenCaptureByWgcWithoutPool;
    [GlobalSetup]
    public void Setup()
    {
       _screenCaptureByWgc = new ScreenCaptureByWGC();
         _screenCaptureByWgcWithoutPool = new ScreenCaptureByWGCWithoutPool();
    }
    [Benchmark]
    public void WithoutPoolButDontSetNull()
    {
        var captureAllScreenBytes = _screenCaptureByWgcWithoutPool.CaptureAllScreenBytes();
        while (captureAllScreenBytes.TryPop(out var result))
        {
            //result.Bytes = null;
            //  ArrayPool<byte>.Shared.Return(result.Bytes);
        }
    }
    [Benchmark]
    public void WithoutPool()
    {
        var captureAllScreenBytes = _screenCaptureByWgcWithoutPool.CaptureAllScreenBytes();
        while (captureAllScreenBytes.TryPop(out var result))
        {
            result.Bytes = null;
            //  ArrayPool<byte>.Shared.Return(result.Bytes);
        }
    }
    [Benchmark]
    public void PoolButDontReturn()
    {
        var captureAllScreenBytes = _screenCaptureByWgc.CaptureAllScreenBytes();
        while (captureAllScreenBytes.TryPop(out var result))
        {
            //  ArrayPool<byte>.Shared.Return(result.Bytes);
        }
    }
    [Benchmark]
    public void Pool()
    {
        var captureAllScreenBytes = _screenCaptureByWgc.CaptureAllScreenBytes();
        while (captureAllScreenBytes.TryPop(out var result))
        {
              ArrayPool<byte>.Shared.Return(result.Bytes);
        }
    }
}