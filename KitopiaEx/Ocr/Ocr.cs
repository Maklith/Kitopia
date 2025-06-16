using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Core.SDKs;
using OpenCvSharp;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Attribute.Scenario;
using PluginCore.ExMethod;
using PluginCore.Onnx;
using SharpHook;
using SharpHook.Native;
using Point = Avalonia.Point;
using Rect = OpenCvSharp.Rect;
using TextDetector = KitopiaEx.Ocr.TextDetector;

namespace KitopiaEx.Ocr;
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

        var ocrResults = new List<OcrResult>();
        var callingAssembly = Assembly.GetExecutingAssembly().Location;
        callingAssembly = callingAssembly.Remove(callingAssembly.LastIndexOf("\\"));

        using TextDetector _textDetector = new TextDetector();
        using TextRecognizer _textRecognizer = new TextRecognizer($"{callingAssembly}\\Ocr\\rec_word_dict.txt");
       
        var detect = _textDetector.Detect(dResult.Source);
        using var textDetectorDstImg = _textDetector.dstImg;
       
        foreach (var point2Fse in detect)
        {
            (Mat, Rect) textimg = _textDetector.GetRotateCropImage(textDetectorDstImg, point2Fse);
            var predictText = _textRecognizer.PredictText(textimg.Item1);
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
       
        Dispatcher.UIThread.Invoke((() =>
        {
            var ocrResultShowWindow = new OcrResultShowWindow();
            ocrResultShowWindow.Image.Source = dResult.Source.ToAWriteableBitmap();
            ocrResultShowWindow.ItemsControl.ItemsSource = ocrResults;
            ocrResultShowWindow.Show();
        }));
       
    }

  
}