using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using KitopiaEx.Ocr;
using OpenCvSharp;
using PluginCore;
using PluginCore.CustomScenario.Attribute;
using PluginCore.CustomScenario.Attribute.Scenario;
using PluginCore.ExMethod;
using PluginCore.Onnx;
using Point = Avalonia.Point;
using Rect = OpenCvSharp.Rect;
using TextDetector = KitopiaEx.Ocr.TextDetector;

namespace KitopiaEx.CustomScenarioMethods;
[ScenarioMethodCategory("文字识别")]
public class Ocr
{
    [OnnxModelInfo]
    public OnnxModelInfo OcrDetModel { get; } = new OnnxModelInfo()
    {
        ModelPath = "Ocr\\ocr_det.onnx",
        Name = "文字检测",
        Description = "文字检测模型",
        SignName = "paddleocrdet",
        
    };

    [OnnxModelInfo]
    public OnnxModelInfo OcrRecModel { get; } = new OnnxModelInfo()
    {
        ModelPath = "Ocr\\ocr_rec.onnx",
        Name = "文字识别",
        Description = "文字识别模型",
        SignName = "paddleocrrec",
    };
    [OnnxModelInfo]
    public OnnxModelInfo OcrRecServerModel { get; } = new OnnxModelInfo()
    {
        ModelPath = "Ocr\\ocr_rec_server.onnx",
        Name = "文字识别服务器版",
        Description = "参数量更大的文字识别模型",
        SignName = "paddleocrrecserver",
        CanDownload = true
    };

    public Ocr()
    {
        OcrRecServerModel.DownloadCommand=new AsyncRelayCommand<OnnxModelInfo>(DownloadCommand);
        OcrRecServerModel.CancelCommand=new AsyncRelayCommand<OnnxModelInfo>(CancelCommand);
    }

    private async Task CancelCommand(OnnxModelInfo? arg)
    {
       await arg._cancellationTokenSource.CancelAsync();
    }

    private async Task DownloadCommand(OnnxModelInfo? obj)
    {
        try
        {
            switch (obj.SignName)
            {
                case "paddleocrrecserver":
                {
                    obj.IsDownloading = true;
                    obj.IsIndeterminate = true;
                    if (obj._cancellationTokenSource is null || obj._cancellationTokenSource.IsCancellationRequested)
                    {
                        obj._cancellationTokenSource = new CancellationTokenSource();
                    }
                    using var httpClient = new HttpClient();
                    var response = await httpClient.GetAsync("https://hf-mirror.com/deepghs/paddleocr/resolve/main/rec/ch_PP-OCRv4_server_rec/model.onnx",
                        HttpCompletionOption.ResponseHeadersRead, obj._cancellationTokenSource.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        obj.IsIndeterminate = false;
                        var readAsStreamAsync = await response.Content.ReadAsStreamAsync(obj._cancellationTokenSource.Token);
                        var contentLength = response.Content.Headers.ContentLength;
                        await using (var fileStream = File.Create($"{obj.ModelPath}.tmp"))
                        {
                            int length = 1024*16;
                            byte[] buffer = new byte[length];
                            int bytesRead = 0;
                            int totalBytesRead = 0;
                            while ((bytesRead = await readAsStreamAsync.ReadAsync(buffer, 0, length, obj._cancellationTokenSource.Token)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead, obj._cancellationTokenSource.Token);
                                totalBytesRead += bytesRead;
                                if (contentLength != null)
                                    obj.Progress = (float)(totalBytesRead) * 100 / contentLength.Value;
                            }

                        
                        }

                        if (!obj._cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            File.Move($"{obj.ModelPath}.tmp", obj.ModelPath, true);
                            obj.IsDownloading = false;
                            obj.NotifyNeedDownload();
                        }
                        else
                        {
                            obj.IsDownloading = false;
                        }
                    
                    }
                    else
                    {
                        obj.IsDownloading = false;
                        obj.CanDownload = false;
                    }
                    break;
                }
            }
        }
        catch (Exception e)
        {
            obj.IsDownloading = false;
        }
    }

    [ScenarioMethod("文字提取", $"{nameof(dResult)}=截图数据","return=文字识别结果数据")]
    public IEnumerable<OcrResult> OcrImg(ScreenCaptureResult dResult, CancellationToken ct)
    {
        if (dResult.Source is null)
        {
            throw new Exception("无图像数据");
        }
        return OcrImgBase(dResult.Source,ct);
    }

    internal IEnumerable<OcrResult> OcrImgBase(Mat image, CancellationToken ct)
    {
        var ocrResults = new List<OcrResult>();
        var callingAssembly = Assembly.GetExecutingAssembly().Location;
        callingAssembly = callingAssembly.Remove(callingAssembly.LastIndexOf("\\", StringComparison.Ordinal));

        using TextDetector textDetector = new TextDetector();
        using TextRecognizer textRecognizer = new TextRecognizer($"{callingAssembly}\\Ocr\\rec_word_dict.txt");
       
        var detect = textDetector.Detect(image);
        using var textDetectorDstImg = textDetector.dstImg;
       
        foreach (var point2Fse in detect)
        {
            (Mat, Rect) textimg = textDetector.GetRotateCropImage(textDetectorDstImg, point2Fse);
            var predictText = textRecognizer.PredictText(textimg.Item1);
            textimg.Item1.Dispose();
            if (string.IsNullOrWhiteSpace(predictText))
            {
                continue;
            }

            Rect rect = textimg.Item2;

            ocrResults.Add(new OcrResult()
            {
                SPoint = new Point(rect.Left, rect.Top),
                EPoint = new Point(rect.Left + rect.Width, rect.Top + rect.Height),
                Text = predictText
            });
           
            //Console.WriteLine(predictText+" "+rect.Left + " " + rect.Top + " " + rect.Width + " " + rect.Height);
        }
        
        return ocrResults;
    }

    [ScenarioMethod("文字提取结果显示", $"{nameof(dResult)}=截图数据",$"{nameof(ocrResults)}=文字识别结果数据")]
    public void OcrResultShow(ScreenCaptureResult dResult,IEnumerable<OcrResult> ocrResults, CancellationToken ct)
    {
        if (dResult.Source != null) OcrResultShowBase(dResult.Source, ocrResults, ct);
    }

    internal void OcrResultShowBase(Mat img, IEnumerable<OcrResult> ocrResults, CancellationToken ct)
    {
        Dispatcher.UIThread.Invoke((() =>
        {
            var ocrResultShowWindow = new OcrResultShowWindow();
            ocrResultShowWindow.Image.Source = img.ToAWriteableBitmap();
            ocrResultShowWindow.ItemsControl.ItemsSource = ocrResults;
            ocrResultShowWindow.Show();
        }));
    }
    [ScenarioMethod("获取文字提取结果显示实例", $"return=文字提取结果显示实例")]
    public OcrResultShowWindow OcrResultShowIn(CancellationToken ct)
    {
        OcrResultShowWindow ocrResultShowWindow =null;
        ct.Register(() =>
        {
            Dispatcher.UIThread.InvokeAsync((() =>
            {
                ocrResultShowWindow.Close();
            }));
           
        });
        Dispatcher.UIThread.Invoke((() =>
        {
            ocrResultShowWindow = new OcrResultShowWindow();
            ocrResultShowWindow.Show();
            
        }));
        return ocrResultShowWindow; 
    }
    [ScenarioMethod("设置文字提取结果", $"{nameof(screenCapture)}=截图数据",$"{nameof(ocrResults)}=文字识别结果数据")]
    public void SetOcrResultShowWindowData(OcrResultShowWindow imagePin, ScreenCaptureResult screenCapture,IEnumerable<OcrResult> ocrResults, CancellationToken ct)
    {
        if (imagePin == null) return;
        Dispatcher.UIThread.Invoke((() =>
        {
            if (imagePin.Image.Source is null  )
            {
                imagePin.Image.Source =screenCapture.Source.ToAWriteableBitmap();
            }
            else if (imagePin.Image.Source is WriteableBitmap writeableBitmap)
            {
                if (writeableBitmap.Size.Width!= screenCapture.Source.Width ||
                    writeableBitmap.Size.Height!= screenCapture.Source.Height)
                {
                    imagePin.Image.Source =screenCapture.Source.ToAWriteableBitmap();
                }
                else
                {
                    if (!screenCapture.Source.IsContinuous())
                    {
                        screenCapture.Source= screenCapture.Source.Clone();
                    }
                    using (var l = writeableBitmap.Lock())
                    {
                        unsafe
                        {
                            var destinationSizeInBytes = screenCapture.Source.Width * 4 * screenCapture.Source.Height;

                            Buffer.MemoryCopy(screenCapture.Source.DataPointer, (void*)l.Address,
                                destinationSizeInBytes, destinationSizeInBytes);


                        }
                    }
                    imagePin.Image.InvalidateVisual();
                }
                
            }
            imagePin.ItemsControl.ItemsSource = ocrResults;
            imagePin.UpdateImageScale();

        }));
    }
}