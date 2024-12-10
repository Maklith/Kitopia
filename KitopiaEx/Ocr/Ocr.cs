using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using OpenCvSharp;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Attribute.Scenario;
using SharpHook;
using SharpHook.Native;
using Point = Avalonia.Point;
using TextDetector = KitopiaEx.Ocr.TextDetector;

namespace KitopiaEx.Ocr;
[ScenarioMethodCategory("文字识别")]
public class Ocr
{
    [ScenarioMethod("文字提取", $"{nameof(dResult)}=截图数据","return=文字识别结果数据")]
    public IEnumerable<OcrResult> OcrImg(ScreenCaptureResult dResult, CancellationToken ct)
    {
        if (dResult.Bytes is null)
        {
            throw new Exception("无图像数据");
        }

        var ocrResults = new List<OcrResult>();
        var callingAssembly = Assembly.GetExecutingAssembly().Location;
        callingAssembly = callingAssembly.Remove(callingAssembly.LastIndexOf("\\"));
        
        TextDetector _textDetector=new TextDetector($"{callingAssembly}\\Ocr\\ocr_det.onnx");
        TextRecognizer _textRecognizer= new TextRecognizer($"{callingAssembly}\\Ocr\\ocr_rec.onnx",$"{callingAssembly}\\Ocr\\rec_word_dict.txt");
        Mat img = Mat.FromPixelData(dResult.Info.Height,dResult.Info.Width,MatType.CV_8UC4,dResult.Bytes);
        
        var detect = _textDetector.Detect(img);
        
        foreach (var point2Fse in detect)
        {
            Mat textimg = _textDetector.GetRotateCropImage(img, point2Fse);
            var predictText = _textRecognizer.PredictText(textimg);
            ocrResults.Add(new OcrResult()
            {
                SPoint = new Point(point2Fse[0].X,point2Fse[0].Y),
                EPoint = new Point(point2Fse[1].X,point2Fse[1].Y),
                Text = predictText
            });
            //Console.WriteLine(predictText);
        }

        img.Dispose();
        _textDetector.Dispose();
        _textRecognizer.Dispose();
        return ocrResults;
    }

    [ScenarioMethod("文字提取结果显示", $"{nameof(dResult)}=截图数据",$"{nameof(ocrResults)}=文字识别结果数据")]
    public void OcrResultShow(ScreenCaptureResult dResult,IEnumerable<OcrResult> ocrResults, CancellationToken ct)
    {
        Dispatcher.UIThread.Invoke((() =>
        {
            var ocrResultShowWindow = new OcrResultShowWindow();
            var writeableBitmap = new WriteableBitmap(
                new PixelSize(dResult.Info.Width, dResult.Info.Height),
                new Vector(96, 96), PixelFormat.Rgba8888);
            using (var l = writeableBitmap.Lock())
            {
                for (var r = 0; r < dResult.Info.Height; r++)
                    Marshal.Copy(dResult.Bytes, r * dResult.Info.Width * 4,
                        new IntPtr(l.Address.ToInt64() + r * l.RowBytes),
                        dResult.Info.Width * 4);
            }

            ocrResultShowWindow.Image.Source = writeableBitmap;
            ocrResultShowWindow.ItemsControl.ItemsSource = ocrResults;
            ocrResultShowWindow.Show();
        }));
       
    }
}