using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;
using PluginCore;
using PluginCore.Onnx;
using Rect = OpenCvSharp.Rect;

namespace KitopiaEx.Ocr
{
    internal class TextDetector : IDisposable
    {
        private float unclipRatio;
        private int maxCandidates;
        private IInferenceSession _session;
        private int shortSize = 736;
        private float shortSideThresh = 3.0f;
        public Mat dstImg;
        public TextDetector()
        {
            this.unclipRatio = 1.6f;
            this.maxCandidates = 1000;
            this._session = Kitopia.InferenceSessionManager.GetSession("paddleocrdet");
        }

       
        public List<Point2f[]> Detect(Mat srcImg)
        {
            unsafe
            {
                int h = srcImg.Rows;
                int w = srcImg.Cols;
                //0. 图像预处理 尺寸调整  归一化
                dstImg = this.Preprocess(srcImg);
                var clone = dstImg.Clone();
                Cv2.Normalize(clone, clone, 0, 1, NormTypes.MinMax, MatType.CV_32F);
                clone.Add(new Scalar(-0.485, -0.456, -0.406));
                clone.Mul(new Scalar(1 / 0.229, 1 / 0.224, 1 / 0.225));
                List<(string, Memory<int>, Memory<float>)> inputs2 = new List<(string, Memory<int>, Memory<float>)>()
                {
                    (_session.InputNames.First(), new[] { 1, 3, dstImg.Rows, dstImg.Cols }, OnnxInputDataTool.InputTensor(clone, 1 * 3 * dstImg.Rows * dstImg.Cols))
                };
                //2. 推理
                var outputs = this._session.Infer(inputs2);

                //3. 输出值解码
                ReadOnlySpan<float> span = outputs.Span;
                fixed (float* ptr = &span.GetPinnableReference())
                {
                    Mat binary = Mat.FromPixelData(dstImg.Rows, dstImg.Cols, MatType.CV_32FC1,(IntPtr)ptr);
                    binary.ConvertTo(binary, MatType.CV_8UC1, 255.0);
                    binary.Threshold(0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);  
                    OpenCvSharp.Point[][] contours;
                    Cv2.FindContours(binary, out contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxTC89L1);
            
                    var results = new List<Point2f[]>();

                    foreach (var contour in contours)
                    {
                        var box = Cv2.MinAreaRect(contour);
                        float shortSide = Math.Min(box.Size.Width, box.Size.Height);
                        if (shortSide < this.shortSideThresh)
                            continue;
                        bool swapSize = box.Size.Width < box.Size.Height || Math.Abs(box.Angle) >= 60.0f;
                        if (swapSize)
                        {
                            (box.Size.Width, box.Size.Height) = (box.Size.Height, box.Size.Width);

                            if (box.Angle < 0)
                            {
                                box.Angle += 90;
                            }
                            else if (box.Angle > 0)
                            {
                                box.Angle -= 90;
                            }
                        }
                        var oUnclip = this.Unclip(box.Points());

                        box = Cv2.MinAreaRect(oUnclip);
                        shortSide = Math.Min(box.Size.Width, box.Size.Height);
                        if (shortSide < this.shortSideThresh+2.0f)
                            continue;
                        results.Add(box.Points());
                    }
            
                    binary.Dispose();
                    return results;
                }
                
            
                
            }
        }
        
        //
        private Mat Preprocess(Mat srcMat)
        {

            int h = srcMat.Rows;
            int w = srcMat.Cols;
            int tarH = h;
            int tarW = w;

            // 计算目标高度和宽度，确保是32的倍数
            tarH = h / 32 * 32;
            if (tarH < h)
            {
                tarH += 32; // 如果刚好是32的倍数，则需要再加32
            }
            tarW = w / 32 * 32;
            if (tarW < w)
            {
                tarW += 32; // 如果刚好是32的倍数，则需要再加32
            }

            var dstImg = new Mat();
            Cv2.CvtColor(srcMat, dstImg, ColorConversionCodes.BGRA2BGR);

            // 创建一个新的图像，用于填充白色背景
            var resizedImgWithPadding = new Mat(tarH, tarW, MatType.CV_8UC3);

            // 计算原图在新图像中的位置

            // 将原图复制到新图像的中心位置
            Cv2.CopyMakeBorder(dstImg, resizedImgWithPadding, 0, tarH - dstImg.Rows, 0, tarW  - dstImg.Cols, BorderTypes.Isolated, new Scalar(255, 255, 255));
            dstImg.Dispose();
            return resizedImgWithPadding;
            // 
            
           
        }
        
        private Point2f[] Unclip(Point2f[] inPoly)
        {
            var outPoly = new Point2f[4];
            float area = (float)Cv2.ContourArea(inPoly);    //轮廓面积
            float length = (float)Cv2.ArcLength(inPoly, true); //轮廓周长
            float distance = area * this.unclipRatio / length;

            int numPoints = inPoly.Length;
            var newLines = new List<List<Point2f>>();

            for (int i = 0; i < numPoints; i++)
            {
                var newLine = new List<Point2f>();
                Point2f pt1 = inPoly[i];
                Point2f pt2 = inPoly[(i - 1 + numPoints) % numPoints];
                Point2f vec = pt1 - pt2;
                float unclipDis = (float)(distance / Math.Sqrt(vec.X * vec.X + vec.Y * vec.Y));
                
                Point2f rotateVec = new Point2f(vec.Y * unclipDis, -vec.X * unclipDis);
                newLine.Add(new Point2f(pt1.X + rotateVec.X, pt1.Y + rotateVec.Y));
                newLine.Add(new Point2f(pt2.X + rotateVec.X, pt2.Y + rotateVec.Y));
                newLines.Add(newLine);
            }

            int numLines = newLines.Count;

            for (int i = 0; i < numLines; i++)
            {
                Point2f a = newLines[i][0];
                Point2f b = newLines[i][1];
                Point2f c = newLines[(i + 1) % numLines][0];
                Point2f d = newLines[(i + 1) % numLines][1];
                Point2f pt;
                Point2f v1 = b - a;
                Point2f v2 = d - c;
                float cosAngle = (float)((v1.X * v2.X + v1.Y * v2.Y) / (Math.Sqrt(v1.X * v1.X + v1.Y * v1.Y) * Math.Sqrt(v2.X * v2.X + v2.Y * v2.Y)));

                if (Math.Abs(cosAngle) > 0.7)
                {
                    pt.X = (b.X + c.X) * 0.5f;
                    pt.Y = (b.Y + c.Y) * 0.5f;
                }
                else
                {
                    float denom = a.X * (d.Y - c.Y) + b.X * (c.Y - d.Y) +
                                  d.X * (b.Y - a.Y) + c.X * (a.Y - b.Y);
                    float num = a.X * (d.Y - c.Y) + c.X * (a.Y - d.Y) + d.X * (c.Y - a.Y);
                    float s = num / denom;

                    pt.X = a.X + s * (b.X - a.X);
                    pt.Y = a.Y + s * (b.Y - a.Y);
                }

                outPoly[i] = pt;
            }
                
            return outPoly;
        }
        
        public (Mat,Rect) GetRotateCropImage(Mat frame, Point2f[] vertices)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i].X = Math.Clamp(vertices[i].X, 0, frame.Cols - 1);
                vertices[i].Y = Math.Clamp(vertices[i].Y, 0, frame.Rows - 1);
            }
            Rect rect = Cv2.BoundingRect(vertices);
            Mat cropImg = new Mat(frame, rect);
            return (cropImg, rect);
        }

        public void Dispose()
        {
            
            _session.Dispose();
            
        }
    }

}