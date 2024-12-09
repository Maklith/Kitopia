using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using PaddleOCRTestOnnx;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Attribute.Scenario;
using SharpHook;
using SharpHook.Native;
using Point = Avalonia.Point;
using TextDetector = PaddleOCRTestOnnx.TextDetector;

namespace KitopiaEx.Ocr;
[ScenarioMethodCategory("文字识别")]
public class Ocr
{
    [ScenarioMethod("文字提取", "key=按键")]
    public IEnumerable<OcrResult> OcrImg(ScreenCaptureResult dResult, CancellationToken ct)
    {
        if (dResult.Bytes is null)
        {
            throw new Exception("无图像数据");
        }

        var ocrResults = new List<OcrResult>();
        TextDetector _textDetector=new TextDetector("ocr_det.onnx");
        TextRecognizer _textRecognizer= new TextRecognizer("ocr_rec.onnx");
        Mat img = Mat.FromImageData(dResult.Bytes, ImreadModes.Color);
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

        return ocrResults;
    }
}