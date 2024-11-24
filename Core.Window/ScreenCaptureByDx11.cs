using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.Display;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Core.SDKs.Services;
using Core.SDKs.Tools.ImageTools;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Pbm;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Qoi;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Vanara.Extensions.Reflection;
using Vanara.PInvoke;


namespace Core.Window;

public class ScreenCaptureByDx11 : IScreenCapture
{
    private static readonly Lazy<Configuration> Lazy = new(CreateDefaultInstance);
    public static Configuration Configuration => Lazy.Value;
    private static readonly ILog log = LogManager.GetLogger(nameof(ScreenCaptureByDx11));

    private static Configuration CreateDefaultInstance()
    {
        return new Configuration(
            new PngConfigurationModule(),
            new JpegConfigurationModule(),
            new GifConfigurationModule(),
            new BmpConfigurationModule(),
            new PbmConfigurationModule(),
            new TgaConfigurationModule(),
            new TiffConfigurationModule(),
            new WebpConfigurationModule(),
            new QoiConfigurationModule())
        {
            PreferContiguousImageBuffers = true
        };
    }

    private void Dx11Helper(Action<Dx11Ptrs> action)
    {
        unsafe
        {
            DXGI dxgi = new DXGI(new DefaultNativeContext("dxgi"));
            ComPtr<IDXGIFactory1> factory = null;
            IDXGIAdapter1* adapter1 = null;
            ID3D11Device* device = null;
            ID3D11DeviceContext* context = null;
            ID3D11DeviceContext* immediateContext = null;
            D3D11 d3D11 = new D3D11(new DefaultNativeContext("d3d11"));
            try
            {
                if (dxgi.CreateDXGIFactory1(out factory) != 0)
                {
                    throw new Exception("Failed to create DXGI factory");
                }

                if (factory.EnumAdapters1(0, ref adapter1) != 0)
                {
                    throw new Exception("Failed to create DXGI adapter");
                }

                D3DFeatureLevel featureLevel = D3DFeatureLevel.Level111;
                D3DFeatureLevel[] featureLevels =
                [
                    D3DFeatureLevel.Level111
                ];

                fixed (D3DFeatureLevel* pFeatureLevels = &featureLevels[0])
                {
                    if (d3D11.CreateDevice((IDXGIAdapter*)adapter1, D3DDriverType.Unknown, IntPtr.Zero,
                            (uint)CreateDeviceFlag.None, pFeatureLevels, (uint)featureLevels.Length, D3D11.SdkVersion,
                            ref device,
                            &featureLevel, ref context) != 0)
                    {
                        throw new Exception("Failed to create D3D11 device");
                    }
                }

                device->GetImmediateContext(ref immediateContext);

                   
                action.Invoke(new Dx11Ptrs()
                {
                    _adapter1 = adapter1,
                    device = device,
                    context = context,
                    immediateContext = immediateContext,
                });
            }
            finally
            {
                dxgi.Dispose();
                d3D11.Dispose();
                factory.Dispose();
                adapter1->Release();
                device->Release();
                context->Release();
                immediateContext->Release();
                dxgi = null;
                d3D11 = null;
                factory = null;
                adapter1 = null;
                device = null;
                context = null;
                immediateContext = null;
            }
        }
    }
    public Stack<ScreenCaptureResult> CaptureAllScreen()
    {
        var screenCaptureResults = new Stack<ScreenCaptureResult>();
        
       
       


        return screenCaptureResults;
    }

    private static unsafe byte[] GetBytesSpan(MappedSubresource mappedSubresource, OutputDesc1 outputDesc)
    {
        byte[] re = new byte[(int)mappedSubresource.DepthPitch*4];

        if (!outputDesc.ColorSpace.ToString().EndsWith("2020"))
        {
            var span = new ReadOnlySpan<UInt32>(mappedSubresource.PData,
                (int)mappedSubresource.DepthPitch/4);
            
            var index = 0;
            foreach (var value in span)
            {
                re[index*4] = (byte)((value) & 0xFF); 
                re[index * 4 + 1] = (byte)((value >> 8) & 0xFF);
                re[index * 4 + 2] = (byte)((value >> 16) & 0xFF);
                re[index*4+3] = (byte)((value  >> 24) & 0xFF);
                index++;
            }
        }
        else
        {
            float[,] matrix = ColorSpaceCtr.CtrColorSpace([
                    outputDesc.RedPrimary[0],
                    outputDesc.RedPrimary[1],
                    outputDesc.GreenPrimary[0],
                    outputDesc.GreenPrimary[1],
                    outputDesc.BluePrimary[0],
                    outputDesc.BluePrimary[1],
                    outputDesc.WhitePoint[0],
                    outputDesc.WhitePoint[1]
                ],
                [.640f, .330f, .300f, .600f, .150f, .060f,outputDesc.WhitePoint[0],
                    outputDesc.WhitePoint[1]]
            );
            var span = new ReadOnlySpan<Half>(mappedSubresource.PData,
                (int)mappedSubresource.DepthPitch/2);
           
            float LogNormalize(float value, float maxHDR, float k=1)
            {
                if (value < 0) value = 0;
                return (float)(Math.Log(1 + k * value) / Math.Log(1 + k * maxHDR));
            }
            var maxHdr = 4.75f;
            for (var index = 0; index < span.Length/4-1; )
            {
                                    
                float r =LogNormalize((float)span[index*4],maxHdr); // 获取最低的16位
                float g =LogNormalize((float)span[index*4+1],maxHdr);
                float b =LogNormalize((float)span[index*4+2],maxHdr);
                float a =LogNormalize((float)span[index*4+3],maxHdr);
                float bt2020R = matrix[0, 0] * r + matrix[0, 1] * g +
                                matrix[0, 2] * b;
                float bt2020G = matrix[1, 0] * r + matrix[1, 1] * g +
                                matrix[1, 2] * b;
                float bt2020B = matrix[2, 0] * r + matrix[2, 1] * g +
                                matrix[2, 2] * b;
                bt2020R =Math.Clamp(bt2020R*255, 0, 255);
                bt2020G =Math.Clamp(bt2020G*255, 0, 255);
                bt2020B =Math.Clamp(bt2020B*255, 0, 255);
                re[index * 4] = (byte)(bt2020R );
                re[index * 4 + 1] = (byte)(bt2020G);
                re[index * 4 + 2] = (byte)(bt2020B);
                re[index * 4 + 3] =255;
                index++;
            }
        }

        return re;
    }

    public List<ScreenCaptureInfo> GetAllScreenInfo()
    {
        var screenCaptureInfos = new List<ScreenCaptureInfo>();
        unsafe
        {
            DXGI dxgi = new DXGI(new DefaultNativeContext("dxgi"));
            ComPtr<IDXGIFactory1> factory = null;
            IDXGIAdapter1* adapter1 = null;
            ID3D11Device* device = null;
            ID3D11DeviceContext* context = null;
            ID3D11DeviceContext* immediateContext = null;
            D3D11 d3D11 = new D3D11(new DefaultNativeContext("d3d11"));
            try
            {
                if (dxgi.CreateDXGIFactory1(out factory) != 0)
                {
                    throw new Exception("Failed to create DXGI factory");
                }

                if (factory.EnumAdapters1(0, ref adapter1) != 0)
                {
                    throw new Exception("Failed to create DXGI adapter");
                }

                D3DFeatureLevel featureLevel = D3DFeatureLevel.Level111;
                D3DFeatureLevel[] featureLevels =
                [
                    D3DFeatureLevel.Level111
                ];

                fixed (D3DFeatureLevel* pFeatureLevels = &featureLevels[0])
                {
                    if (d3D11.CreateDevice((IDXGIAdapter*)adapter1, D3DDriverType.Unknown, IntPtr.Zero,
                            (uint)CreateDeviceFlag.None, pFeatureLevels, (uint)featureLevels.Length, D3D11.SdkVersion,
                            ref device,
                            &featureLevel, ref context) != 0)
                    {
                        throw new Exception("Failed to create D3D11 device");
                    }
                }

                device->GetImmediateContext(ref immediateContext);

                uint i = 0;
                IDXGIOutput* output = null;
                while (adapter1->EnumOutputs(i, ref output) == 0)
                {
                    i++;
                    try
                    {
                        OutputDesc desc = new OutputDesc(null);
                        if (output->GetDesc(ref desc) != 0)
                        {
                            throw new Exception("Failed to get output description");
                        }
                        screenCaptureInfos.Add(new ScreenCaptureInfo()
                        {
                            Index = i,
                            Height = desc.DesktopCoordinates.Size.Y,
                            Width = desc.DesktopCoordinates.Size.X,
                            X = desc.DesktopCoordinates.Min.X,
                            Y = desc.DesktopCoordinates.Min.Y
                        });

                    }
                    catch (Exception e)
                    {
                        log.Error("错误", e);
                    }
                    finally
                    {
                        output->Release();
                        output = null;
                            
                    }
                }

                return screenCaptureInfos;
            }
            finally
            {
                dxgi.Dispose();
                d3D11.Dispose();
                factory.Dispose();
                adapter1->Release();
                device->Release();
                context->Release();
                immediateContext->Release();
                dxgi = null;
                d3D11 = null;
                factory = null;
                adapter1 = null;
                device = null;
                context = null;
                immediateContext = null;
            }
        }
    }

    public ScreenCaptureInfo GetScreenCaptureInfoByIndex(int index)
    {
        return default;
    }

    

    private unsafe struct Dx11Ptrs
    {
        public IDXGIAdapter1* _adapter1;
        public ID3D11Device* device ;
        public ID3D11DeviceContext* context ;
        public ID3D11DeviceContext* immediateContext ;
        
    }
    public unsafe Stack<ScreenCaptureResult> CaptureAllScreenBytes()
    {
        var screenCaptureResults = new Stack<ScreenCaptureResult>();
        Dx11Helper(intPtr =>
        {
            uint i = 0;
            IDXGIOutput* output = null;
            while (intPtr._adapter1->EnumOutputs(i, ref output) == 0)
            {
                i++;
                IDXGIOutputDuplication* outputDuplication = null;
                IDXGIResource* desktopResource = null;
                ID3D11Texture2D* stagingTexture = null;
                ComPtr<IDXGIOutput5> output5 = null;
                ComPtr<IDXGIOutput6> output6 = null;
                ComPtr<ID3D11Resource> desktopTexture = null;
                ComPtr<ID3D11Resource> stagingResource = null;
                try
                {
                    OutputDesc desc = new OutputDesc(null);
                    if (output->GetDesc(ref desc) != 0)
                    {
                        throw new Exception("Failed to get output description");
                    }
                    if (output->QueryInterface<IDXGIOutput5>(out output5) != 0)
                    {
                        throw new Exception("Failed to get IDXGIOutput5");
                    }
                    if (output->QueryInterface<IDXGIOutput6>(out output6) != 0)
                    {
                        throw new Exception("Failed to get IDXGIOutput6");
                    }

                    OutputDesc1 outputDesc=new OutputDesc1() ;
                    if (output6.GetDesc1(ref outputDesc)!=0)
                    {
                        throw new Exception("Failed to get Desc1");
                    }
                    //uint whiteSDRLevel = 0;
                    //var firstOrDefault = whiteSdrLevel.FirstOrDefault(e=>e?.Item1==i-1);
                            
                    Format[] dFormats =
                    [
                        Format.FormatR16G16B16A16Float
                    ];
                    if (!outputDesc.ColorSpace.ToString().EndsWith("2020"))
                    {
                        dFormats =
                        [
                            Format.FormatR8G8B8A8Unorm
                        ];
                    }
                    fixed (Format* pFeatureLevels = &dFormats[0])
                    {
                                
                        if (output5.DuplicateOutput1 ((IUnknown*)intPtr.device,0,(uint)dFormats.Length,pFeatureLevels, ref outputDuplication) != 0)
                        {
                            throw new Exception("Failed to get output duplication");
                        }
                    }
                            

                    OutduplFrameInfo outduplFrameInfo = new OutduplFrameInfo();
                            
                            
                    OutduplDesc desc2 = new OutduplDesc();
                    outputDuplication->GetDesc(ref desc2);
                            
                    while (true)
                    {
                        Thread.Sleep(50);
                        if (outputDuplication->AcquireNextFrame(3000, &outduplFrameInfo, &desktopResource) != 0 ||
                            outduplFrameInfo.LastPresentTime== 0)
                        {
                            break;
                        }
                    }
                    if (desktopResource->QueryInterface<ID3D11Resource>(out desktopTexture) != 0)
                    {
                        throw new Exception("Failed to get desktop texture");
                    }
                            
                    Texture2DDesc stagingTextureDesc = new()
                    {
                        CPUAccessFlags = (uint)CpuAccessFlag.Read,
                        BindFlags = (uint)(BindFlag.None),
                        Format = dFormats[0],
                        Width = (uint)desc.DesktopCoordinates.Size.X,
                        Height = (uint)desc.DesktopCoordinates.Size.Y,
                        MiscFlags = (uint)ResourceMiscFlag.None,
                        MipLevels = 1,
                        ArraySize = 1,
                        SampleDesc = { Count = 1, Quality = 0 },
                        Usage = Usage.Staging
                    };

                    if (intPtr.device->CreateTexture2D(&stagingTextureDesc, null, ref stagingTexture) != 0)
                    {
                        throw new Exception("Failed to create staging texture");
                    }

                    stagingTexture->QueryInterface<ID3D11Resource>(out stagingResource);
                    intPtr.immediateContext->CopyResource(stagingResource, desktopTexture);
                            
                    MappedSubresource mappedSubresource = new MappedSubresource();
                            
                    if (intPtr.immediateContext->Map(stagingResource, 0, Map.Read, 0, &mappedSubresource) != 0)
                    {
                        throw new Exception("Failed to map staging texture");
                    }
                            
                    var re = GetBytesSpan(mappedSubresource, outputDesc);
                    intPtr.immediateContext->Unmap(stagingResource, 0);
                    outputDuplication->ReleaseFrame();
                    
                    screenCaptureResults.Push(new ScreenCaptureResult()
                    {
                        Bytes = re,
                        Info = new ScreenCaptureInfo()
                        {
                            Height = desc.DesktopCoordinates.Size.Y,
                            Width = desc.DesktopCoordinates.Size.X,
                            X= desc.DesktopCoordinates.Min.X,
                            Y = desc.DesktopCoordinates.Min.Y
                        }
                    });
                }
                catch (Exception e)
                {
                    log.Error("错误", e);
                }
                finally
                {
                    output->Release();
                    outputDuplication->Release();
                    desktopResource->Release();
                    stagingTexture->Release();
                    output5.Release();
                    output6.Dispose();
                    desktopTexture.Release();
                    stagingResource.Release();
                    output = null;
                    outputDuplication = null;
                    desktopResource = null;
                    stagingTexture = null;
                    output5 = null;
                    desktopTexture = null;
                    stagingResource = null;
                            
                }
            }
        });
        return screenCaptureResults;
    }
    
    public Stack<ScreenCaptureResult> CaptureAllScreenBitmap()
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

    public ScreenCaptureResult CaptureScreenBitmap(ScreenCaptureInfo screenCaptureInfo)
    {
        return default;
    }

    public ScreenCaptureResult CaptureScreenBytes(ScreenCaptureInfo screenCaptureInfo)
    {
        return default;
    }
}