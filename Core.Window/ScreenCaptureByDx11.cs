using System.Collections;
using System.ComponentModel;
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
using IScreenCapture = PluginCore.IScreenCapture;

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

    public class DisposableTool(Action busySetter) : IDisposable
    {
        public void Dispose() => busySetter.Invoke();
    }
    [PInvokeData("wingdi.h", MSDNShortId = "2DACA175-19BC-4192-A2FF-CB8AC7220B98")]
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
    {
        public Gdi32.DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        /// <summary>A POINTL structure that specifies the size of the VidPn source surface that is being displayed on the monitor.</summary>
        public POINT PathSourceSize;
        /// <summary>
        /// A RECTL structure that defines where the desktop image will be positioned within path source. Region must be completely inside
        /// the bounds of the path source size.
        /// </summary>
        public RECT DesktopImageRegion;
        /// <summary>
        /// A RECTL structure that defines which part of the desktop image for this clone group will be displayed on this path. This
        /// currently must be set to the desktop size.
        /// </summary>
        public RECT DesktopImageClip;
    }
    private IEnumerable<(uint, uint)?> GetWhiteSDRLevel()
    {
        var err = User32.GetDisplayConfigBufferSizes(User32.QDC.QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount);
        if (err != 0)
        {
            yield return (0, 0);
            yield break;
        }
            

        var paths = new Gdi32.DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new Gdi32.DISPLAYCONFIG_MODE_INFO[modeCount];
        err = User32.QueryDisplayConfig(User32.QDC.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (err != 0)
        {
            yield return (0, 0);
            yield break;
        }

        for (uint index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            StructPointer<DISPLAYCONFIG_DESKTOP_IMAGE_INFO > targetName2 = new();
            var colorInfo2 = new DISPLAYCONFIG_DESKTOP_IMAGE_INFO ();
            
            colorInfo2.header.type =
                Gdi32.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
            colorInfo2.header.size = (uint)Marshal.SizeOf<Gdi32.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>();
            colorInfo2.header.adapterId = path.targetInfo.adapterId;
            colorInfo2.header.id = path.targetInfo.id;
            err = User32.DisplayConfigGetDeviceInfo(targetName2.DestructiveAssign(colorInfo2));
            if (err != 0)
            {
                yield return (0, 0);
                continue;
            }
            StructPointer<Gdi32.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO> targetName = new();
            var colorInfo = new Gdi32.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
            colorInfo.header.type =
                Gdi32.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
            colorInfo.header.size = (uint)Marshal.SizeOf<Gdi32.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>();
            colorInfo.header.adapterId = path.targetInfo.adapterId;
            colorInfo.header.id = path.targetInfo.id;
            err = User32.DisplayConfigGetDeviceInfo(targetName.DestructiveAssign(colorInfo));
            if (err != 0)
            {
                yield return (0, 0);
                continue;
            }

            if (!targetName.Value.Value.value.HasFlag(Gdi32.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_VALUE
                    .advancedColorEnabled))
            {
                yield return (0, 0);
                continue;
            }

            StructPointer<DISPLAYCONFIG_SDR_WHITE_LEVEL> targetName1 = new();
            var colorInfo1 = new DISPLAYCONFIG_SDR_WHITE_LEVEL();
            colorInfo1.header.type = Gdi32.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL;
            colorInfo1.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SDR_WHITE_LEVEL>();
            colorInfo1.header.adapterId = path.targetInfo.adapterId;
            colorInfo1.header.id = path.targetInfo.id;
            err = User32.DisplayConfigGetDeviceInfo(targetName1.DestructiveAssign(colorInfo1));
            if (err != 0)
            {
                yield return (0, 0);
                continue;
            }

            yield return (index, targetName1.Value.Value.SDRWhiteLevel);
        }
    }
    [PInvokeData("wingdi.h")]
    public struct DISPLAYCONFIG_SDR_WHITE_LEVEL : Gdi32.IDisplayConfig
    {
        /// <summary>Undocumented.</summary>
        public Gdi32.DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        /// <summary>
        /// SDRWhiteLevel represents a multiplier for standard SDR white peak value i.e. 80 nits represented as fixed point. To get value in
        /// nits use the following conversion SDRWhiteLevel in nits = (SDRWhiteLevel / 1000 ) * 80
        /// </summary>
        public uint SDRWhiteLevel;
    }
    public Stack<ScreenCaptureResult> CaptureAllScreen()
    {
        var whiteSdrLevel = GetWhiteSDRLevel();
        
        var screenCaptureResults = new Stack<ScreenCaptureResult>();
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

                    uint i = 0;
                    IDXGIOutput* output = null;
                    while (adapter1->EnumOutputs(i, ref output) == 0)
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
                            uint whiteSDRLevel = 0;
                            var firstOrDefault = whiteSdrLevel.FirstOrDefault(e=>e?.Item1==i-1);
                            if (firstOrDefault!= null)
                            {
                                whiteSDRLevel = firstOrDefault.Value.Item2;
                            }
                            Format[] dFormats =
                            [
                                Format.FormatR16G16B16A16Float
                            ];
                            if (whiteSDRLevel==0)
                            {
                                dFormats =
                                [
                                    Format.FormatR8G8B8A8Unorm
                                ];
                            }
                            fixed (Format* pFeatureLevels = &dFormats[0])
                            {
                                
                                if (output5.DuplicateOutput1 ((IUnknown*)device,0,(uint)dFormats.Length,pFeatureLevels, ref outputDuplication) != 0)
                                {
                                    throw new Exception("Failed to get output duplication");
                                }
                            }
                            

                            OutduplFrameInfo outduplFrameInfo = new OutduplFrameInfo();
                            
                            Thread.Sleep(20);
                            OutduplDesc desc2 = new OutduplDesc();
                            outputDuplication->GetDesc(ref desc2);
                            if (outputDuplication->AcquireNextFrame(3000, &outduplFrameInfo, &desktopResource) != 0 ||
                                outduplFrameInfo.LastPresentTime == 0)
                            {
                                throw new Exception("Failed to acquire next frame");
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

                            if (device->CreateTexture2D(&stagingTextureDesc, null, ref stagingTexture) != 0)
                            {
                                throw new Exception("Failed to create staging texture");
                            }

                            stagingTexture->QueryInterface<ID3D11Resource>(out stagingResource);
                            immediateContext->CopyResource(stagingResource, desktopTexture);
                            
                            MappedSubresource mappedSubresource = new MappedSubresource();
                            
                            if (immediateContext->Map(stagingResource, 0, Map.Read, 0, &mappedSubresource) != 0)
                            {
                                throw new Exception("Failed to map staging texture");
                            }
                            var span = new ReadOnlySpan<UInt64>(mappedSubresource.PData,
                                (int)mappedSubresource.DepthPitch/8);
                            immediateContext->Unmap(stagingResource, 0);
                            outputDuplication->ReleaseFrame();
                            
                            
                            //R10G10B10A2
                            //Rgba8888
                            Span<byte> re = new byte[span.Length*4];
                            int index = 0;
                            float[,] matrix = {
                                { 1.660491f, -0.587641f, -0.072850f },
                                { -0.124550f, 1.132900f, -0.008349f },
                                { -0.018151f, -0.100579f, 1.118730f }, 
                            };

                            float Linear(float value)
                            {
                                return value <= 0.081f ? value / 4.5f : (float)Math.Pow((value + 0.0993f) / 1.0993f, 2.222f);
                            }


                            foreach (var value in span)
                            {
                                if (whiteSDRLevel==0)
                                {
                                    re[index*4] = (byte)((value) & 0xFF); 
                                    re[index * 4 + 1] = (byte)((value >> 8) & 0xFF);
                                    re[index * 4 + 2] = (byte)((value >> 16) & 0xFF);
                                    re[index*4+3] = (byte)((value  >> 24) & 0xFF);
                                }
                                else
                                {
                                   
                                    int r = (int)((value >> 0) & 0xFFFF); // 获取最低的16位
                                    int g = (int)((value >> 16) & 0xFFFF); // 获取次低的16位
                                    int b = (int)((value >> 32) & 0xFFFF); // 获取次高的16位
                                    int a = (int)((value >> 48) & 0xFFFF); // 获取最高的16位
                                    float linearR = Linear(r /65534f);
                                    float linearG = Linear(g / 65534f);
                                    float linearB = Linear(b / 65534f);
                                    float bt2020R = matrix[0, 0] * linearR + matrix[0, 1] * linearG + matrix[0, 2] * linearB;
                                    float bt2020G = matrix[1, 0] * linearR + matrix[1, 1] * linearG + matrix[1, 2] * linearB;
                                    float bt2020B = matrix[2, 0] * linearR + matrix[2, 1] * linearG + matrix[2, 2] * linearB;
                                    bt2020R =(float) Math.Clamp(bt2020R*255*whiteSDRLevel/outputDesc.MaxLuminance, 0, 255);
                                    bt2020G =(float) Math.Clamp(bt2020G*255*whiteSDRLevel/outputDesc.MaxLuminance, 0, 255);
                                    bt2020B =(float) Math.Clamp(bt2020B*255*whiteSDRLevel/outputDesc.MaxLuminance, 0, 255);
                                    var d = outputDesc.MaxLuminance;
                                    re[index*4]= (byte)Math.Round(bt2020R );
                                    re[index*4+1]= (byte)Math.Round(bt2020G);
                                    re[index*4+2] = (byte)Math.Round(bt2020B);
                                    
                                    re[index*4+3] = (byte)(a*whiteSDRLevel/outputDesc.MaxLuminance);
                                }
                                
                                index++;
                            } 
                            var source = re.ToArray();
                            var writeableBitmap = new WriteableBitmap(
                                new PixelSize(desc.DesktopCoordinates.Size.X, desc.DesktopCoordinates.Size.Y),
                                new Vector(96, 96), PixelFormat.Rgba8888);
                            using (var l = writeableBitmap.Lock())
                            {
                                for (var r = 0; r < desc.DesktopCoordinates.Size.Y; r++)
                                {
                                    
                                    Marshal.Copy(source, r * desc.DesktopCoordinates.Size.X * 4,
                                        new IntPtr(l.Address.ToInt64() + r * l.RowBytes),
                                        desc.DesktopCoordinates.Size.X * 4);
                                }
                            }
                            
                        
                            var process = GaussianBlur1.GaussianBlur(source, desc.DesktopCoordinates.Size.X,
                                desc.DesktopCoordinates.Size.Y, 4);
                            var writeableBitmap2 = new WriteableBitmap(
                                new PixelSize(desc.DesktopCoordinates.Size.X, desc.DesktopCoordinates.Size.Y),
                                new Vector(96, 96), PixelFormat.Rgba8888 );
                            using (var l = writeableBitmap2.Lock())
                            {
                                for (var r = 0; r < desc.DesktopCoordinates.Size.Y; r++)
                                {
                                    Marshal.Copy(process, r * desc.DesktopCoordinates.Size.X * 4,
                                        new IntPtr(l.Address.ToInt64() + r * l.RowBytes),
                                        desc.DesktopCoordinates.Size.X * 4);
                                }
                            }
                        
                            screenCaptureResults.Push(new ScreenCaptureResult()
                            {
                                Source = writeableBitmap,
                                Mosaic = writeableBitmap2,
                                Info = new ScreenCaptureInfo()
                                {
                                    Height = desc.DesktopCoordinates.Size.Y,
                                    Width = desc.DesktopCoordinates.Size.X,
                                    X= desc.DesktopCoordinates.Min.X,
                                    Y = desc.DesktopCoordinates.Min.Y
                                }
                            });
                           // array = null;
                            process = null;

                           
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
       
       


        return screenCaptureResults;
    }
    
   
    private SizeInt32 _lastSize;
    private GraphicsCaptureItem _item;
    private Direct3D11CaptureFramePool _framePool;
    private GraphicsCaptureSession _session;

    public (Bitmap?, Bitmap?)? CaptureScreen(ScreenCaptureInfo screenCaptureInfo, bool withMosaic = false)
    {
        return (null, null);
    }

    public ScreenCaptureInfo GetScreenCaptureInfoByUserManual()
    {
        return ServiceManager.Services.GetService<IScreenCaptureWindow>()!.GetScreenCaptureInfo()
            .Result;
    }
}