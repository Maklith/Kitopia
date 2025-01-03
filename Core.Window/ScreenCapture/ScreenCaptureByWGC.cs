using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PluginCore;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Vanara.PInvoke;
using WinRT;
using DirectXPixelFormat = Windows.Graphics.DirectX.DirectXPixelFormat;
using IDirect3DDevice = Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice;
using IDirect3DSurface = Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface;
using IInspectable = WinRT.IInspectable;

namespace Core.Window;

public class ScreenCaptureByWGC : IScreenCapture
{
    public List<ScreenCaptureInfo> GetAllScreenInfo()
    {
        var screenCaptureInfos = new List<ScreenCaptureInfo>();
        uint i = 0;
        User32.EnumDisplayMonitors(default, null, (arg1, arg2, arg3, arg4) =>
        {
            screenCaptureInfos.Add(new ScreenCaptureInfo()
            {
                
                X = 0,
                Y = 0,
                Width = arg3.right - arg3.left,
                Height = arg3.bottom - arg3.top,
                ScreenInfo = new ScreenInfo()
                {
                    X = arg3.left,
                    Y = arg3.top,
                    Width = arg3.right - arg3.left,
                    Height = arg3.bottom - arg3.top,
                    hMonitor = arg1,
                    
                }
                
            });
            i++;
            return true;
        }, IntPtr.Zero);
      
        return screenCaptureInfos;
    }
    public const uint WS_POPUP = 0x80000000; // 弹出窗口样式
    public const uint WS_CHILD = 0x40000000; // 子窗口样式
    public List<WindowInfo> GetAllWindowInfo()
    {
        var screenCaptureInfos = new List<WindowInfo>();
        uint i = 0;
        int zIndex = 0;
        User32.EnumWindows((arg1, arg2) =>
        {
            // 忽略有父窗口的和不可见的窗口
            if (!User32.GetParent(arg1).IsNull || !User32.IsWindowVisible(arg1)|| User32.IsIconic(arg1))
            {
                return true;
            }
            int style = User32.GetWindowLong(arg1,User32.WindowLongFlags.GWL_STYLE);
            if ((style & WS_POPUP) != 0 || (style & WS_CHILD) != 0)
            {
                return true; // 跳过弹出窗口或子窗口
            }
            if (!User32.IsWindow(arg1))
            {
                return true; // 跳过无效窗口
            }

            // 获取窗口标题
            StringBuilder stringBuilder = new StringBuilder(100);
            User32.GetWindowText(arg1, stringBuilder, 100);
            var title = stringBuilder.ToString();
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }
            // 获取窗口的位置和大小
            User32.GetWindowRect(arg1, out var rect);

            
            // 按 Z-Order 遍历窗口
            IntPtr hwnd = arg1.DangerousGetHandle();
            HWND currentHwnd = User32.GetTopWindow(IntPtr.Zero);
            int zIndex = 0;

            while (currentHwnd != IntPtr.Zero)
            {
                if (User32.IsWindowVisible(currentHwnd)) // 只考虑可见窗口
                {
                    if (currentHwnd == hwnd)
                    {
                        break;
                    }
                    zIndex++;
                }
                currentHwnd = User32.GetWindow(currentHwnd, User32.GetWindowCmd.GW_HWNDNEXT);
            }

            // 添加到结果列表
            screenCaptureInfos.Add(new WindowInfo()
            {
                Title = title,
                Hwnd = hwnd,
                Rect = new Rect(rect.X, rect.Y, rect.Width, rect.Height),
                ZIndex = zIndex
            });

            return true;
        }, IntPtr.Zero);

      
        return screenCaptureInfos;
    }

    public ScreenCaptureInfo GetScreenCaptureInfoByIndex(int index)
    {
        return default;
    }

    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(
            [In] IntPtr window,
            [In] ref Guid iid);

        IntPtr CreateForMonitor(
            [In] IntPtr monitor,
            [In] ref Guid iid);
    }

    [DllImport(
        "d3d11.dll",
        EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        CallingConvention = CallingConvention.StdCall
    )]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    private ComPtr<ID3D11Resource> CreateSharpDXTexture2D(IDirect3DSurface surface)
    {
        unsafe
        {
            var iInspectable = (IInspectable)(object)surface;
            var queryInterface = iInspectable.ObjRef.AsInterface<IDirect3DDxgiInterfaceAccess>();
            var d3dPointer = (void*)queryInterface.GetInterface(ID3D11Resource.Guid);
            var comPtr = new ComPtr<ID3D11Resource>();
            comPtr.Handle = (ID3D11Resource*)d3dPointer;
            return comPtr;
        }
    }


    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    };

    public Stack<ScreenCaptureResult> CaptureAllScreenBitmap()
    {
        var screenCaptureResults = new Stack<ScreenCaptureResult>();
        var captureAllScreenBytes = CaptureAllScreenBytes();
        while (captureAllScreenBytes.TryPop(out var captureAllScreenInfo))
        {
            screenCaptureResults.Push(CaptureScreenBitmap(captureAllScreenInfo));
        }

        return screenCaptureResults;
    }

    public Stack<ScreenCaptureResult> CaptureAllScreenBytes()
    {
        var screenCaptureResults = new Stack<ScreenCaptureResult>();
        foreach (var screenCaptureInfo in GetAllScreenInfo())
        {
            screenCaptureResults.Push(CaptureScreenBytes(screenCaptureInfo)); 
        }

        return screenCaptureResults;
    }

    public ScreenCaptureResult CaptureScreenBitmap(ScreenCaptureResult captureAllScreenInfo)
    {
        var writeableBitmap = new WriteableBitmap(
            new PixelSize(captureAllScreenInfo.Info.Width, captureAllScreenInfo.Info.Height),
            new Vector(96, 96), PixelFormat.Bgra8888 );
        using (var l = writeableBitmap.Lock())
        {
            unsafe
            {
                var destinationSizeInBytes = captureAllScreenInfo.Info.Width * 4 * captureAllScreenInfo.Info.Height;
                fixed (byte* srcPtr = captureAllScreenInfo.Bytes)
                {
                    Buffer.MemoryCopy(srcPtr,(void*)l.Address,destinationSizeInBytes,destinationSizeInBytes);
                }
                
            }
        }

        captureAllScreenInfo.Bytes = null;
        captureAllScreenInfo.Source = writeableBitmap;
        return captureAllScreenInfo;
    }

    public IntPtr FindHMonitor(ScreenInfo screenInfo)
    {
        IntPtr h=IntPtr.Zero;
        User32.EnumDisplayMonitors(default, null, (arg1, arg2, arg3, arg4) =>
        {
            if (screenInfo.X==arg3.left&&screenInfo.Y==arg3.top&&screenInfo.Width==arg3.right-arg3.left&&screenInfo.Height==arg3.bottom-arg3.top)
            {
                h = arg1;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return h;
    }
    public static (ComPtr<IDXGIAdapter1>,OutputDesc1) GetAdapterForMonitor(ComPtr<IDXGIFactory1> factory,IntPtr hMonitor)
    {
        ComPtr<IDXGIAdapter1> adapter = null;
        while (factory.EnumAdapters1(0, ref adapter) == 0)
        {
            ComPtr<IDXGIOutput> output = null;
            while (adapter.EnumOutputs(0, ref output  ) == 0)
            {
                OutputDesc desc = new OutputDesc();
                output.GetDesc(ref desc);
                if (desc.Monitor == hMonitor)
                {
                    ComPtr<IDXGIOutput6> output6 = null;
                    if (output.QueryInterface<IDXGIOutput6>(out output6) != 0)
                        throw new Exception("Failed to get IDXGIOutput6");
                    var outputDesc = new OutputDesc1();
                    if (output6.GetDesc1(ref outputDesc) != 0) throw new Exception("Failed to get Desc1");
                    return (adapter,outputDesc);
                }
                else
                {
                    output.Release();
                }
            }

            adapter.Release();
        }

        throw new InvalidOperationException("No adapter found for the given monitor.");
    }
    
    public unsafe ScreenCaptureResult CaptureScreenBytes(ScreenCaptureInfo screenCaptureInfo)
    {
        if (screenCaptureInfo.ScreenInfo.hMonitor==null || screenCaptureInfo.ScreenInfo.hMonitor ==IntPtr.Zero)
        {
            screenCaptureInfo.ScreenInfo.hMonitor = FindHMonitor(screenCaptureInfo.ScreenInfo);
            if (screenCaptureInfo.ScreenInfo.hMonitor==IntPtr.Zero)
            {
                throw new Exception("目标显示器不存在");
            }
        }
        var factory2 = ActivationFactory.Get(typeof(GraphicsCaptureItem).FullName);
        var interop = factory2.AsInterface<IGraphicsCaptureItemInterop>();
        var itemPointer = interop.CreateForMonitor(screenCaptureInfo.ScreenInfo.hMonitor, GraphicsCaptureItemGuid);
        var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        var dxgi = new DXGI(new DefaultNativeContext("dxgi"));

        ComPtr<IDXGIAdapter1> adapter1 = null;
        ID3D11DeviceContext* context = null;
        ID3D11DeviceContext* immediateContext = null;
        ID3D11Device* d3dDevice = null;
        ComPtr<ID3D11Resource> stagingResource = null;
        Direct3D11CaptureFrame direct3D11CaptureFrame = null;
        ID3D11Texture2D* stagingTexture = null;
        GraphicsCaptureSession session = null;
        Direct3D11CaptureFramePool framePool = null;
        using var d3D11 = new D3D11(new DefaultNativeContext("d3d11"));
        try
        {
            using var factory = dxgi.CreateDXGIFactory1<IDXGIFactory1>();
            var adapterForMonitor = GetAdapterForMonitor(factory,screenCaptureInfo.ScreenInfo.hMonitor);
            adapter1 = adapterForMonitor.Item1;
            
            var featureLevel = D3DFeatureLevel.Level110;
            D3DFeatureLevel[] featureLevels =
            [
                D3DFeatureLevel.Level110
            ];
            fixed (D3DFeatureLevel* pFeatureLevels = &featureLevels[0])
            {
                if (d3D11.CreateDevice((IDXGIAdapter*)adapter1.Handle, D3DDriverType.Unknown, IntPtr.Zero,
                        (uint)CreateDeviceFlag.None, pFeatureLevels, (uint)featureLevels.Length, D3D11.SdkVersion,
                        ref d3dDevice,
                        &featureLevel, ref context) != 0)
                    throw new Exception("Failed to create D3D11 device");
            }

            d3dDevice->GetImmediateContext(ref immediateContext);

            IDirect3DDevice CreateDirect3DDeviceFromSharpDXDevice(ID3D11Device* d3dDevice)
            {
                IDirect3DDevice device = null;

                // Acquire the DXGI interface for the Direct3D device.
                using (var dxgiDevice = d3dDevice->QueryInterface<IDXGIDevice3>())
                {
                    // Wrap the native device using a WinRT interop object.
                    var hr = CreateDirect3D11DeviceFromDXGIDevice((IntPtr)dxgiDevice.Handle, out var pUnknown);

                    if (hr == 0)
                    {
                        device = MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
                        Marshal.Release(pUnknown);
                    }
                }

                return device;
            }

            var direct3DDeviceFromSharpDxDevice = CreateDirect3DDeviceFromSharpDXDevice(d3dDevice);
            
            framePool = Direct3D11CaptureFramePool.Create(
                direct3DDeviceFromSharpDxDevice,
                adapterForMonitor.Item2.ColorSpace.ToString().EndsWith("2020")
                    ? DirectXPixelFormat.R16G16B16A16Float
                    : DirectXPixelFormat.R8G8B8A8UIntNormalized,
                2,
                item.Size);
            
            session = framePool.CreateCaptureSession(item);
            
            session.IsCursorCaptureEnabled = false;
            session.StartCapture( );
            while ((direct3D11CaptureFrame = framePool.TryGetNextFrame()) == null)
            {
            }
            
            using var bitmap = CreateSharpDXTexture2D(direct3D11CaptureFrame.Surface);
            
            var mappedSubresource = new MappedSubresource();

            Texture2DDesc stagingTextureDesc = new()
            {
                CPUAccessFlags = (uint)CpuAccessFlag.Read,
                BindFlags = (uint)BindFlag.None,
                Format = adapterForMonitor.Item2.ColorSpace.ToString().EndsWith("2020")
                    ? Format.FormatR16G16B16A16Float
                    : Format.FormatR8G8B8A8Unorm,
                Width =  (uint)item.Size.Width,
                Height = (uint)item.Size.Height,
                MiscFlags = (uint)ResourceMiscFlag.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDesc = { Count = 1, Quality = 0 },
                Usage = Usage.Staging
            };

            if (d3dDevice->CreateTexture2D(&stagingTextureDesc, null, ref stagingTexture) != 0)
                throw new Exception("Failed to create staging texture");

            stagingTexture->QueryInterface<ID3D11Resource>(out stagingResource);
            immediateContext->CopyResource(stagingResource, bitmap);
            if (immediateContext->Map(stagingResource, 0, Map.Read, 0, &mappedSubresource) != 0)
                throw new Exception("Failed to map staging texture");
            var bytesSpan = CaptureTool.GetBytesSpan(mappedSubresource,adapterForMonitor.Item2,ref screenCaptureInfo);
           
            return new ScreenCaptureResult()
            {
                Info = screenCaptureInfo,
                Bytes = bytesSpan
            };
        }
        finally
        {
            
            
            adapter1.Release();
            adapter1 = null;
            

            if (context != null)
            {
                context->Release();
                context = null;
            }

            if (immediateContext != null)
            {
                immediateContext->Release();
                immediateContext = null;
            }

            if (d3dDevice != null)
            {
                d3dDevice->Release();
                d3dDevice = null;
            }


            stagingResource.Release();
            stagingResource = null;
            
            if (direct3D11CaptureFrame != null)
            {
                direct3D11CaptureFrame.Dispose();
                direct3D11CaptureFrame = null;
            }

            if (stagingTexture != null)
            {
                stagingTexture->Release();
                stagingTexture = null;
            }

            if (framePool != null)
            {
                framePool.Dispose();
                framePool = null;
            }

            if (session != null)
            {
                
                session.Dispose();
                session = null;
            }
        }
    }
}