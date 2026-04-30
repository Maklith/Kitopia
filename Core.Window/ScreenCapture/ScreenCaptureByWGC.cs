using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Capture;
using OpenCvSharp;
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
using Rect = PluginCore.Rect;

namespace Core.Window.ScreenCapture;

public class ScreenCaptureByWgc : IScreenCapture {
    public List<WindowInfo> GetAllWindowInfo() {
        return ScreenCaptureInfoEx.GetAllWindowInfo().ToList();
    }

    public ScreenCaptureInfo GetScreenCaptureInfoByIndex(int index) {
        return default;
    }

    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    private interface IGraphicsCaptureItemInterop {
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

    private ComPtr<ID3D11Resource> CreateSharpDxTexture2D(IDirect3DSurface surface) {
        unsafe {
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
    private interface IDirect3DDxgiInterfaceAccess {
        IntPtr GetInterface([In] ref Guid iid);
    };

    public Stack<ScreenCaptureResult> CaptureAllScreenMat() {
        var screenCaptureResults = new Stack<ScreenCaptureResult>();
        foreach (var screenCaptureInfo in GetAllScreenInfo()) {
            screenCaptureResults.Push(CaptureScreenMat(screenCaptureInfo));
        }

        return screenCaptureResults;
    }


    public static (ComPtr<IDXGIAdapter1>, OutputDesc1) GetAdapterForMonitor(ComPtr<IDXGIFactory1> factory,
        IntPtr hMonitor) {
        ComPtr<IDXGIAdapter1> adapter = null;
        uint i = 0;
        while (factory.EnumAdapters1(i, ref adapter) == 0) {
            uint j = 0;
            ComPtr<IDXGIOutput> output = null;
            while (adapter.EnumOutputs(j, ref output) == 0) {
                OutputDesc desc = new OutputDesc();
                output.GetDesc(ref desc);
                if (desc.Monitor == hMonitor) {
                    ComPtr<IDXGIOutput6> output6 = null;
                    if (output.QueryInterface(out output6) != 0)
                        throw new Exception("Failed to get IDXGIOutput6");
                    var outputDesc = new OutputDesc1();
                    if (output6.GetDesc1(ref outputDesc) != 0) throw new Exception("Failed to get Desc1");
                    return (adapter, outputDesc);
                }
                else {
                    output.Release();
                }

                j++;
            }

            adapter.Release();
            i++;
        }

        throw new InvalidOperationException("No adapter found for the given monitor.");
    }

    public List<ScreenCaptureInfo> GetAllScreenInfo() {
        var screenCaptureInfos = new List<ScreenCaptureInfo>();
        User32.EnumDisplayMonitors(default, null, (arg1, _, arg3, _) => {
            if (arg3 == null || arg3.IsEmpty) {
                return true;
            }

            screenCaptureInfos.Add(new ScreenCaptureInfo() {
                HMonitor = arg1.DangerousGetHandle(),
                SdrWhiteLevelScale = DisplayConfigHelper.GetSdrWhiteLevel(arg1.DangerousGetHandle()),
                ScreenInfo = new Rect(arg3.left, arg3.top, arg3.right - arg3.left, arg3.bottom - arg3.top),
                RequestRect = new Rect(0, 0, arg3.right - arg3.left, arg3.bottom - arg3.top)
            });
            return true;
        }, IntPtr.Zero);

        return screenCaptureInfos;
    }

    public unsafe ScreenCaptureResult CaptureScreenMat(ScreenCaptureInfo screenCaptureInfo) {
        switch (screenCaptureInfo.ScreenCaptureType) {
            case ScreenCaptureType.屏幕: {
                screenCaptureInfo.ThrowIfCantGetValidScreenIntptr();
                break;
            }
            case ScreenCaptureType.窗口: {
                screenCaptureInfo.ThrowIfCantGetValidWindowHandle();
                break;
            }
        }

        var factory2 = ActivationFactory.Get(typeof(GraphicsCaptureItem).FullName);
        var interop = factory2.AsInterface<IGraphicsCaptureItemInterop>();
        IntPtr itemPointer = IntPtr.Zero;
        switch (screenCaptureInfo.ScreenCaptureType) {
            case ScreenCaptureType.屏幕: {
                itemPointer = interop.CreateForMonitor(screenCaptureInfo.HMonitor, GraphicsCaptureItemGuid);
                break;
            }
            case ScreenCaptureType.窗口: {
                itemPointer =
                    interop.CreateForWindow(screenCaptureInfo.WindowInfo!.Value.Hwnd, GraphicsCaptureItemGuid);
                break;
            }
        }

        var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        var dxgi = new DXGI(new DefaultNativeContext("dxgi"));
        ComPtr<IDXGIAdapter1> adapter1 = default;
        ComPtr<ID3D11DeviceContext> context = default;
        ComPtr<ID3D11DeviceContext> immediateContext = default;
        ComPtr<ID3D11Device> d3DDevice = default;
        ComPtr<ID3D11Resource> stagingResource = default;
        Direct3D11CaptureFrame direct3D11CaptureFrame = null;
        ComPtr<ID3D11Texture2D> stagingTexture = default;
        GraphicsCaptureSession session = null;
        Direct3D11CaptureFramePool framePool = null;
        using var d3D11 = new D3D11(new DefaultNativeContext("d3d11"));
        try {
            using var factory = dxgi.CreateDXGIFactory1<IDXGIFactory1>();
            var adapterForMonitor = GetAdapterForMonitor(factory, screenCaptureInfo.HMonitor);
            adapter1 = adapterForMonitor.Item1;

            var featureLevel = D3DFeatureLevel.Level110;
            D3DFeatureLevel[] featureLevels = [
                D3DFeatureLevel.Level110
            ];
            fixed (D3DFeatureLevel* pFeatureLevels = &featureLevels[0]) {
                if (d3D11.CreateDevice((IDXGIAdapter*)adapter1.Handle, D3DDriverType.Unknown, IntPtr.Zero,
                        (uint)CreateDeviceFlag.None, pFeatureLevels, (uint)featureLevels.Length, D3D11.SdkVersion,
                        d3DDevice.GetAddressOf(),
                        &featureLevel,  context.GetAddressOf()) != 0)
                    throw new Exception("Failed to create D3D11 device");
            }

            d3DDevice.GetImmediateContext(ref immediateContext);

            IDirect3DDevice CreateDirect3DDeviceFromSharpDxDevice(ID3D11Device* d3dDevice) {
                IDirect3DDevice device = null;

                // Acquire the DXGI interface for the Direct3D device.
                using var dxgiDevice = d3dDevice->QueryInterface<IDXGIDevice3>();
                // Wrap the native device using a WinRT interop object.
                var hr = CreateDirect3D11DeviceFromDXGIDevice((IntPtr)dxgiDevice.Handle, out var pUnknown);

                if (hr == 0) {
                    device = MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
                    Marshal.Release(pUnknown);
                }

                return device;
            }

            var direct3DDeviceFromSharpDxDevice = CreateDirect3DDeviceFromSharpDxDevice(d3DDevice);

            framePool = Direct3D11CaptureFramePool.Create(
                direct3DDeviceFromSharpDxDevice,
                adapterForMonitor.Item2.ColorSpace.ToString().EndsWith("2020")
                    ? DirectXPixelFormat.R16G16B16A16Float
                    : DirectXPixelFormat.R8G8B8A8UIntNormalized,
                2,
                item.Size);

            session = framePool.CreateCaptureSession(item);

            session.IsCursorCaptureEnabled = false;
            session.StartCapture();

            while ((direct3D11CaptureFrame = framePool.TryGetNextFrame()) == null) { }

            using var bitmap = CreateSharpDxTexture2D(direct3D11CaptureFrame.Surface);

            var mappedSubresource = new MappedSubresource();

            Texture2DDesc stagingTextureDesc = new() {
                CPUAccessFlags = (uint)CpuAccessFlag.Read,
                BindFlags = (uint)BindFlag.None,
                Format = adapterForMonitor.Item2.ColorSpace.ToString().EndsWith("2020")
                    ? Format.FormatR16G16B16A16Float
                    : Format.FormatR8G8B8A8Unorm,
                Width = (uint)(item.Size.Width),
                Height = (uint)item.Size.Height,
                MiscFlags = (uint)ResourceMiscFlag.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDesc = { Count = 1, Quality = 0 },
                Usage = Usage.Staging
            };

            if (d3DDevice.CreateTexture2D(&stagingTextureDesc, null, ref stagingTexture) != 0)
                throw new Exception("Failed to create staging texture");

            stagingTexture.QueryInterface<ID3D11Resource>(out stagingResource);
            immediateContext.CopyResource(stagingResource, bitmap);
            if (immediateContext.Map(stagingResource, 0, Map.Read, 0, &mappedSubresource) != 0)
                throw new Exception("Failed to map staging texture");

            //更新窗口的Size数据
            return new ScreenCaptureResult() {
                Info = screenCaptureInfo,
                Source = ConvertSubresourceToSdrMat(mappedSubresource, adapterForMonitor.Item2, ref screenCaptureInfo),
            };
        }
        finally {
            adapter1.Release();
            context.Release();
            immediateContext.Release();
            d3DDevice.Release();
            stagingResource.Release();
            if (direct3D11CaptureFrame != null) {
                direct3D11CaptureFrame.Dispose();
            }
            stagingTexture.Release();
            if (framePool != null) {
                framePool.Dispose();
            }
            if (session != null) {
                session.Dispose();
            }
        }
    }

    public static unsafe Mat? ConvertSubresourceToSdrMat(MappedSubresource mappedSubresource, OutputDesc1 outputDesc,
        ref ScreenCaptureInfo screenCaptureInfo) {
        if (screenCaptureInfo is not { RequestRect: not null, ScreenInfo: not null }) return null;
        int startX = Math.Clamp(screenCaptureInfo.RequestRect.Value.X, 0,
            screenCaptureInfo.ScreenInfo.Value.Width - 1);
        int startY = Math.Clamp(screenCaptureInfo.RequestRect.Value.Y, 0,
            screenCaptureInfo.ScreenInfo.Value.Height - 1);
        int endX = Math.Clamp(screenCaptureInfo.RequestRect.Value.X + screenCaptureInfo.RequestRect.Value.Width, 0,
            screenCaptureInfo.ScreenInfo.Value.Width);
        int endY = Math.Clamp(screenCaptureInfo.RequestRect.Value.Y + screenCaptureInfo.RequestRect.Value.Height, 0,
            screenCaptureInfo.ScreenInfo.Value.Height);
        if (!outputDesc.ColorSpace.ToString().EndsWith("2020")) {
            Mat mat = new Mat((int)(mappedSubresource.DepthPitch / mappedSubresource.RowPitch),
                (int)(mappedSubresource.RowPitch / 4), MatType.CV_8UC4);
            Buffer.MemoryCopy(mappedSubresource.PData, mat.DataPointer,
                mappedSubresource.DepthPitch,
                mappedSubresource.DepthPitch);
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGBA2BGRA);
            if (screenCaptureInfo.ScreenCaptureType != ScreenCaptureType.窗口) {
                var mat1 = mat[startY, endY, startX, endX];
                mat.Dispose();
                return mat1;
            }

            return mat;
        }
        else {
            var mat = new Mat((int)(mappedSubresource.DepthPitch / mappedSubresource.RowPitch),
                (int)(mappedSubresource.RowPitch / 8), MatType.MakeType(7, 4));
            Buffer.MemoryCopy(mappedSubresource.PData, mat.DataPointer,
                mappedSubresource.DepthPitch, mappedSubresource.DepthPitch);

            mat.ConvertTo(mat, MatType.CV_32FC4);
            //var vec4F = mat.Get<Vec4f>(2);
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGBA2RGB);
            var matrix = ColorSpaceCtr.CtrColorSpace([
                    outputDesc.RedPrimary[0],
                    outputDesc.RedPrimary[1],
                    outputDesc.GreenPrimary[0],
                    outputDesc.GreenPrimary[1],
                    outputDesc.BluePrimary[0],
                    outputDesc.BluePrimary[1],
                    outputDesc.WhitePoint[0],
                    outputDesc.WhitePoint[1]
                ],
                [
                    .640f, .330f, .300f, .600f, .150f, .060f, .3127f, .3290f
                ]
            );
            Cv2.Transform(mat, mat, Mat.FromArray(matrix));
            
            float scale = screenCaptureInfo.SdrWhiteLevelScale;
            if (scale < 0.1f) scale = 1.0f; // Safety check
            mat /= scale;
            
            Cv2.Pow(mat, 1.0 / 2.2, mat);
            mat *= 255.0;

            mat.ConvertTo(mat, MatType.CV_8UC4);
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGB2BGRA);
            if (screenCaptureInfo.ScreenCaptureType != ScreenCaptureType.窗口) {
                var mat1 = mat[startY, endY, startX, endX];
                mat.Dispose();
                return mat1;
            }

            return mat;
        }

    }
}