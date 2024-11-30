using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
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
        User32.EnumDisplayMonitors(default(HDC),null, (arg1, arg2, arg3, arg4) =>
        {
            screenCaptureInfos.Add(new ScreenCaptureInfo()
            {
                Index = i,
                X = arg3.left,
                Y = arg3.top,
                Width = arg3.right - arg3.left,
                Height = arg3.bottom - arg3.top,
                hMonitor = arg2,
                hdcMonitor = arg4
            });
            i++;
            return true;
        },IntPtr.Zero);
        return screenCaptureInfos;
    }

    public ScreenCaptureInfo GetScreenCaptureInfoByIndex(int index)
    {
        return default;
    }
    static readonly Guid GraphicsCaptureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    interface IGraphicsCaptureItemInterop
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
    static extern UInt32 CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
    ComPtr<ID3D11Resource> CreateSharpDXTexture2D(IDirect3DSurface surface)
    {
        unsafe
        {
            var iInspectable = (IInspectable)(object)surface;
            var queryInterface = iInspectable.ObjRef.AsInterface<IDirect3DDxgiInterfaceAccess>();
            var d3dPointer =(void*)(queryInterface).GetInterface(Silk.NET.Direct3D11.ID3D11Resource.Guid);
            var comPtr = new ComPtr<ID3D11Resource>();
            comPtr.Handle = (ID3D11Resource*)d3dPointer;
            return comPtr;
        }
    }
  
   
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    };
    public unsafe Stack<ScreenCaptureResult> CaptureAllScreenBitmap()
    {
        
        var screenCaptureResults = new Stack<ScreenCaptureResult>();
        var captureAllScreenBytes = CaptureAllScreenBytes();
        while (captureAllScreenBytes.TryPop(out var captureAllScreenInfo))
        {
            var writeableBitmap = new WriteableBitmap(
                new PixelSize(captureAllScreenInfo.Info.Width, captureAllScreenInfo.Info.Height),
                new Vector(96, 96), PixelFormat.Rgba8888);
            using (var l = writeableBitmap.Lock())
            {
                for (var r = 0; r <captureAllScreenInfo.Info.Height; r++)
                {
                    Marshal.Copy(captureAllScreenInfo.Bytes, r * captureAllScreenInfo.Info.Width * 4,
                        new IntPtr(l.Address.ToInt64() + r * l.RowBytes),
                        captureAllScreenInfo.Info.Width * 4);
                }
            }

            captureAllScreenInfo.Bytes = null;
            captureAllScreenInfo.Source = writeableBitmap;
            screenCaptureResults.Push(captureAllScreenInfo);
        }

        return screenCaptureResults;
        
      
    }

    public unsafe Stack<ScreenCaptureResult> CaptureAllScreenBytes()
    {
        var screenCaptureResults = new Stack<ScreenCaptureResult>();
        foreach (var screenCaptureInfo in GetAllScreenInfo())
        {
            var factory2 = WinRT.ActivationFactory.Get(typeof(GraphicsCaptureItem).FullName);
            var interop = factory2.AsInterface<IGraphicsCaptureItemInterop>();
            var itemPointer = interop.CreateForMonitor(screenCaptureInfo.hMonitor, GraphicsCaptureItemGuid);
            var item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
            DXGI dxgi = new DXGI(new DefaultNativeContext("dxgi"));
          
            IDXGIAdapter1* adapter1 = null;
            ID3D11DeviceContext* context = null;
            ID3D11DeviceContext* immediateContext = null;
            ID3D11Device* d3dDevice=null;
            ComPtr<ID3D11Resource> stagingResource = null;
            IDXGIOutput* output=null;
            ComPtr<IDXGIOutput6> output6 = null;
            Direct3D11CaptureFrame direct3D11CaptureFrame = null;
            ID3D11Texture2D* stagingTexture = null;
            GraphicsCaptureSession session=null;
            Direct3D11CaptureFramePool framePool = null;
            D3D11 d3D11 = new D3D11(new DefaultNativeContext("d3d11"));
            try
            {

                using ComPtr<IDXGIFactory1> factory = dxgi.CreateDXGIFactory1<IDXGIFactory1>();
        
     
                if (factory.EnumAdapters1(0, ref adapter1) != 0)
                {
                    throw new Exception("Failed to create DXGI adapter");
                }
                D3DFeatureLevel featureLevel = D3DFeatureLevel.Level110;
                D3DFeatureLevel[] featureLevels =
                [
                    D3DFeatureLevel.Level110
                ];
                fixed (D3DFeatureLevel* pFeatureLevels = &featureLevels[0])
                {
                    if (d3D11.CreateDevice((IDXGIAdapter*)adapter1, D3DDriverType.Unknown, IntPtr.Zero,
                            (uint)CreateDeviceFlag.None, pFeatureLevels, (uint)featureLevels.Length, D3D11.SdkVersion,
                            ref d3dDevice,
                            &featureLevel, ref context) != 0)
                    {
                        throw new Exception("Failed to create D3D11 device");
                    }
                }
                d3dDevice->GetImmediateContext(ref immediateContext);
                IDirect3DDevice CreateDirect3DDeviceFromSharpDXDevice(ID3D11Device* d3dDevice)
                {
                    IDirect3DDevice device = null;

                    // Acquire the DXGI interface for the Direct3D device.
                    using (var dxgiDevice = d3dDevice->QueryInterface<IDXGIDevice3>())
                    {
                        // Wrap the native device using a WinRT interop object.
                        uint hr = CreateDirect3D11DeviceFromDXGIDevice((IntPtr)dxgiDevice.Handle, out IntPtr pUnknown);

                        if (hr == 0)
                        {
                            device =MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown) ;
                            Marshal.Release(pUnknown);
                        }
                    }

                    return device;
                }
                var direct3DDeviceFromSharpDxDevice = CreateDirect3DDeviceFromSharpDXDevice(d3dDevice);
                
                
                adapter1->EnumOutputs(screenCaptureInfo.Index, ref output);
                if (output->QueryInterface<IDXGIOutput6>(out output6) != 0)
                {
                    throw new Exception("Failed to get IDXGIOutput6");
                }
                OutputDesc1 outputDesc=new OutputDesc1() ;
                if (output6.GetDesc1(ref outputDesc)!=0)
                {
                    throw new Exception("Failed to get Desc1");
                }
                framePool = Direct3D11CaptureFramePool.Create(
                    direct3DDeviceFromSharpDxDevice,
                    outputDesc.ColorSpace.ToString().EndsWith("2020")? DirectXPixelFormat.R16G16B16A16Float:DirectXPixelFormat.R8G8B8A8UIntNormalized,
                    2,
                    item.Size);
                session = framePool.CreateCaptureSession(item);
                session.StartCapture();
       
        
                while ((direct3D11CaptureFrame = framePool.TryGetNextFrame())==null)
                { }
                using var bitmap = CreateSharpDXTexture2D(direct3D11CaptureFrame.Surface);
                MappedSubresource mappedSubresource = new MappedSubresource();
                
                Texture2DDesc stagingTextureDesc = new()
                {
                    CPUAccessFlags = (uint)CpuAccessFlag.Read,
                    BindFlags = (uint)(BindFlag.None),
                    Format =outputDesc.ColorSpace.ToString().EndsWith("2020")? Format.FormatR16G16B16A16Float :Format.FormatR8G8B8A8Unorm,
                    Width =(uint)direct3D11CaptureFrame.ContentSize.Width,
                    Height = (uint)direct3D11CaptureFrame.ContentSize.Height,
                    MiscFlags = (uint)ResourceMiscFlag.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDesc = { Count = 1, Quality = 0 },
                    Usage = Usage.Staging
                };

                if (d3dDevice->CreateTexture2D(&stagingTextureDesc, null, ref stagingTexture) != 0)
                {
                    throw new Exception("Failed to create staging texture");
                }
        
                stagingTexture->QueryInterface<ID3D11Resource>(out stagingResource); 
                immediateContext->CopyResource(stagingResource,bitmap);
                if (immediateContext->Map(stagingResource, 0, Map.Read, 0, &mappedSubresource) != 0)
                {
                    throw new Exception("Failed to map staging texture");
                }
               
               
                var bytesSpan = CaptureTool.GetBytesSpan(mappedSubresource,outputDesc);
                screenCaptureResults.Push(new ScreenCaptureResult()
                {
                    Info = screenCaptureInfo,
                    Bytes = bytesSpan.ToArray()
                });
            }
            finally
            {
                if (adapter1!=null)
                {
                    adapter1->Release();
                    adapter1 = null;
                }

                if (context!=null)
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
                

                if (output != null)
                {
                    output->Release();
                    output = null;
                }

                
                
                output6.Release();
                output6 = null;
                

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
        return screenCaptureResults;
    }

    public ScreenCaptureResult CaptureScreenBitmap(ScreenCaptureInfo screenCaptureInfo)
    {
        return default;
    }

    public ScreenCaptureResult CaptureScreenBytes(ScreenCaptureInfo screenCaptureInfo)
    {
        return default;
    }
}