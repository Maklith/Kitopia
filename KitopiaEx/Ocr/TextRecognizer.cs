using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenCvSharp;
using PluginCore;
using PluginCore.Onnx;

namespace KitopiaEx.Ocr 
{
    internal class TextRecognizer : IDisposable
    {
        private IInferenceSession _session;
        private List<string> alphabet;
       
        public TextRecognizer(string recWorldDictPath)
        {
            if (Config.INSTANCE.UseServerOcrRecModel)
            {
                this._session = Kitopia.InferenceSessionManager.GetSession("paddleocrrecserver");
            }else
            {
                this._session = Kitopia.InferenceSessionManager.GetSession("paddleocrrec");
            }

            using (StreamReader sr = new StreamReader(recWorldDictPath))
            {
                string line;
                alphabet = new List<string>();
                while ((line = sr.ReadLine()) != null)
                {
                    alphabet.Add(line);
                }
            }
            alphabet.Add(" ");
        }

        public string PredictText(Mat cv_image)
        {
            using var dstimg = Preprocess(cv_image);
            Cv2.Normalize(dstimg, dstimg, 0, 1, NormTypes.MinMax, MatType.CV_32F);

            var elementCount = 3 * dstimg.Width * dstimg.Height;
            var tensor = ArrayPool<float>.Shared.Rent(elementCount);
            Memory<float> output;
            try
            {
                var input = tensor.AsMemory(0, elementCount);
                OnnxInputDataTool.InputTensor(dstimg, input);
                List<(string, Memory<int>, Memory<float>)> inputs2 = new List<(string, Memory<int>, Memory<float>)>()
                {
                    (_session.InputNames.First(), new[] { 1, 3,dstimg.Rows, dstimg.Width }, input)
                };
                output = _session.Infer(inputs2);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(tensor);
            }

            var outputs0 = output.Span;
            
            int dimension = _session.OutputShape[0][2];  //输出维度
            int characters = outputs0.Length / dimension;
            List<float> confidences = new List<float>(characters);
            List<int>  labels = new List<int>(characters);
            for (int c=0;c<characters;c++)
            {
                int one_label_idx = 0;
                float max_data = -10000;
                for (int d = 0; d < dimension; d++)
                {
                    float data_ = outputs0[c * dimension + d];
                    if (data_ > max_data)
                    {
                        max_data = data_;
                        one_label_idx = d;
                    }
                }
                labels.Add(one_label_idx);
                confidences.Add(max_data);
            }
            

            List<int> no_repeat_blank_label = new List<int>();
            for (int elementIndex = 0; elementIndex < characters; ++elementIndex)
            {
                if (labels[elementIndex] != 0 && !(elementIndex > 0 && labels[elementIndex - 1] == labels[elementIndex])&&
                    confidences[elementIndex] >= 0.4)
                {
                    no_repeat_blank_label.Add(labels[elementIndex] - 1);
                }
            }
            int len_s = no_repeat_blank_label.Count;
            StringBuilder plate_text = new StringBuilder();
            for (int i = 0; i < len_s; i++)
            {
                plate_text.Append(alphabet[no_repeat_blank_label[i]]);
            }

            //Console.WriteLine($"{plate_text} {string.Join(", ", confidences)}");
            labels.Clear();
            confidences.Clear();
            return plate_text.ToString();
        }

        private Mat Preprocess(Mat srcImg)
        {
           
            int w = srcImg.Cols;
            int h = srcImg.Rows;
            int tarW = (int)(w / 32) * 32;
            
            if (tarW < w)
            {
                tarW += 32; // Adjust to the next multiple of 32
            }
            int tarH = (int)(h / 48) * 48;
            
            if (tarH < h)
            {
                tarH += 48; // Adjust to the next multiple of 32
            }
            // Convert to grayscale
           // var grayImg = new Mat();
           // Cv2.CvtColor(srcImg, grayImg, ColorConversionCodes.RGBA2GRAY);

            // Create a new image with white padding
// Force height to 48 without scaling the width
          

            // Create a new image with white padding
            var paddedImg = new Mat(tarH, tarW, MatType.CV_8UC3, new Scalar(255, 255, 255));
            Cv2.CopyMakeBorder(srcImg, paddedImg, 0, tarH-srcImg.Rows, 0, tarW - srcImg.Cols, BorderTypes.Isolated, new Scalar(255, 255, 255));
            Cv2.Resize(paddedImg,paddedImg,new Size(tarW*(48.0/tarH),48));
           // Cv2.Threshold(paddedImg,paddedImg,127,255,ThresholdTypes.Binary);
            return paddedImg;    
        }
        

        public void Dispose()
        {
            
            _session.Dispose();
        }
    }
}
