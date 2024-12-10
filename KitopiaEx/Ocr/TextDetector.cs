using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace KitopiaEx.Ocr
{
    internal class TextDetector : IDisposable
    {
        private float unclipRatio;
        private int maxCandidates;
        private string modelPath;
        private SessionOptions sessionOptions;
        private InferenceSession _session;
        private List<string> inputNames;
        
        private int shortSize = 736;
        private float shortSideThresh = 3.0f;

        public TextDetector(string modelpath, SessionOptions opts = null)
        {
            this.unclipRatio = 1.6f;
            this.maxCandidates = 1000;

            this.modelPath = modelpath;
            this.sessionOptions = new SessionOptions();
            this.sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC;

            this._session = new InferenceSession(this.modelPath, this.sessionOptions);
            this.inputNames = new List<string>();
         
            

            //智能
            foreach (var name in this._session.InputMetadata.Keys)
            {
                this.inputNames.Add(name);
            }

          
        }


        public List<Point2f[]> Detect(Mat srcImg)
        {
            
            int h = srcImg.Rows;
            int w = srcImg.Cols;

            //0. 图像预处理 尺寸调整  归一化
            Mat dstImg = this.Preprocess(srcImg);
            var normalize = this.Normalize(dstImg);
           
            //1. 构建输入张量
            int[] inputShape = { 1, 3, dstImg.Rows, dstImg.Cols };
            var inputTensor = new DenseTensor<float>(normalize, inputShape);
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(this.inputNames[0], inputTensor)
            };

            //2. 推理
            var outputs = this._session.Run(inputs);

            //3. 输出值解码
            var floatArray = outputs[0].AsTensor<float>();

            Mat binary = new Mat(dstImg.Rows, dstImg.Cols, MatType.CV_8UC1);
            
            for (int y = 0; y < dstImg.Rows; y++)
            {
                for (int x = 0; x < dstImg.Cols; x++)
                {
                    binary.Set<byte>(y, x, (byte)(floatArray.GetValue(y * dstImg.Cols + x) > 0 ? 255:0));
                }
            }
            float scaleHeight = (float)(h) / (float)(binary.Size(0));
            float scaleWidth = (float)(w) / (float)(binary.Size(1));
            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(binary, out contours, out hierarchy, RetrievalModes.List, ContourApproximationModes.ApproxTC89L1);
            
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
            
            foreach (var t in results)
            {
                for (var i1 = 0; i1 < t.Length; i1++)
                {
                    t[i1].Y =Math.Clamp(t[i1].Y * scaleHeight,0 ,srcImg.Rows - 1) ;
                    t[i1].X = Math.Clamp(t[i1].X * scaleWidth, 0, srcImg.Cols - 1);
                }
            }
            return results;
        }
        
        //
        private Mat Preprocess(Mat srcMat)
        {

            int h = srcMat.Rows;
            int w = srcMat.Cols;
            float scaleH = 1;
            float scaleW = 1;

            if (h < w)
            {
                scaleH = (float)shortSize / h;
                float tarW = w * scaleH;
                tarW = tarW - (int)tarW % 32;
                tarW = Math.Max(32, tarW);
                scaleW = tarW / w;
            }
            else
            {
                scaleW = (float)this.shortSize / w;
                float tarH = h * scaleW;
                tarH = tarH - (int)tarH % 32;
                tarH = Math.Max(32, tarH);
                scaleH = tarH / h;
            }

            var dstImg = new Mat();
            Cv2.CvtColor(srcMat, dstImg, ColorConversionCodes.RGBA2GRAY);
            Cv2.Resize(dstImg, dstImg, new OpenCvSharp.Size((int)(scaleW * dstImg.Cols), (int)(scaleH * dstImg.Rows)), interpolation: InterpolationFlags.Linear);
            return dstImg;
        }

        private float[]  Normalize(Mat img)
        {
            int row = img.Rows;
            int col = img.Cols;
            float[] inputImage = new float[row*col*3];
            for (int i = 0; i < row; i++)
            {
                for (int j = 0; j < col; j++)
                {
                    Vec3b pix = img.Get<Vec3b>(i, j);
                    //由于在上一步中未进行 BGR2RGB ,此处进行
                    inputImage[i*col+j+2]=(pix[2] / 255.0f - 0.485f) / 0.229f;
                    inputImage[i*col+j+1]=(pix[1] / 255.0f -  0.456f) / 0.224f;
                    inputImage[i*col+j]=(pix[0] / 255.0f -0.406f ) / 0.225f;
                        
                }
            }
            return inputImage;
        }
        

        //计算轮廓分值 20240416未完全理解
        // TODO 注意返回值的归一化
        private float ContourScore(Mat binary, OpenCvSharp.Point[] contour)
        {
            //1. 获取轮廓点的外接矩形
            Rect rect = Cv2.BoundingRect(contour);
            int xmin = Math.Max(rect.X, 0);
            int xmax = Math.Min(rect.X + rect.Width, binary.Cols - 1);
            int ymin = Math.Max(rect.Y, 0);
            int ymax = Math.Min(rect.Y + rect.Height, binary.Rows - 1);

            //2. 填充外接矩形内，由轮廓点围成的多边形
            Mat binROI = new Mat(binary, new Rect(xmin, ymin, xmax - xmin + 1, ymax - ymin + 1));
            Mat mask = Mat.Zeros(new OpenCvSharp.Size(xmax - xmin + 1, ymax - ymin + 1), MatType.CV_8U);
            var roiContour = contour.Select(p => new OpenCvSharp.Point(p.X - xmin, p.Y - ymin)).ToList();
            Cv2.FillPoly(mask, new List<List<OpenCvSharp.Point>> { roiContour },(Scalar)1); // 1
            
            //3. 计算填充多边形区域的均值 

            Scalar mean = Cv2.Mean(binROI, mask);

            return (float)mean.Val0/255.0f;

        }

        // 未理解该函数的意义 20240416
        // 或许可参考 DBNet后处理unclip()函数转C++  https://www.jianshu.com/p/0227c40b0736  
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
                    float denom = a.X * (float)(d.Y - c.Y) + b.X * (float)(c.Y - d.Y) +
                                  d.X * (float)(b.Y - a.Y) + c.X * (float)(a.Y - b.Y);
                    float num = a.X * (float)(d.Y - c.Y) + c.X * (float)(a.Y - d.Y) + d.X * (float)(c.Y - a.Y);
                    float s = num / denom;

                    pt.X = a.X + s * (b.X - a.X);
                    pt.Y = a.Y + s * (b.Y - a.Y);
                }

                outPoly[i] = pt;
            }
                
            return outPoly;
        }

        //基于vertices围成的外接矩形，似乎没有作用
        public Mat GetRotateCropImage(Mat frame, Point2f[] vertices)
        {
            Rect rect = Cv2.BoundingRect(vertices);
            Mat cropImg = new Mat(frame, rect);
            OpenCvSharp.Size outputSize = new OpenCvSharp.Size(rect.Width, rect.Height);

            List<Point2f> targetVertices = new List<Point2f>
            {
                new Point2f(0, outputSize.Height),
                new Point2f(0, 0),
                new Point2f(outputSize.Width, 0), 
                new Point2f(outputSize.Width, outputSize.Height)
            };

            for (int i = 0; i < 4; i++)
            {
                vertices[i].X -= rect.X;
                vertices[i].Y -= rect.Y;
            }

            Mat rotationMatrix = Cv2.GetPerspectiveTransform(vertices.ToArray(), targetVertices.ToArray());
            Mat result = new Mat();
            Cv2.WarpPerspective(cropImg, result, rotationMatrix, outputSize, borderMode: BorderTypes.Replicate);

            return result;
        }

        public void Dispose()
        {
            sessionOptions.Dispose();
            _session.Dispose();
        }
    }

}