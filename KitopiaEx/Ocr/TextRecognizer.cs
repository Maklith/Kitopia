using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace KitopiaEx.Ocr 
{
    internal class TextRecognizer : IDisposable
    {
        private InferenceSession _session;
        private List<string> input_names;
        private List<string> output_names;
        private List<int[]> output_node_dims;
        private List<string> alphabet;
        private int inpHeight = 48;
        private int inpWidth = 320;
        private List<float> input_image_;
        private List<int> preb_label;

        public TextRecognizer(string modelpath,string recWorldDictPath)
        {
            var sessionOptions = new SessionOptions();
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC;

            _session = new InferenceSession(modelpath, sessionOptions);

            input_names = new List<string>();
            output_names = new List<string>();

            this.input_image_ = new List<float>();

            output_node_dims = new List<int[]>();
           

            foreach (var name in this._session.InputMetadata.Keys)
            {
                this.input_names.Add(name);
            }

            foreach (var name in this._session.OutputMetadata.Keys)
            {
                this.output_names.Add(name);
            }

            foreach (var value in this._session.OutputMetadata.Values)
            {
                this.output_node_dims.Add(value.Dimensions);
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
            Mat dstimg = Preprocess(cv_image);
            var normalize = Normalize(dstimg);

            int[] input_shape_ = new int[] { 1, 3, dstimg.Rows, dstimg.Width };

            var input_tensor_ = new DenseTensor<float>(normalize, input_shape_);

            var ort_inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor<float>(input_names[0], input_tensor_)
            };

            var ort_outputs = _session.Run(ort_inputs);

            float[] outputs0 = ort_outputs[0].AsTensor<float>().ToArray<float>();

            int dimension = this.output_node_dims[0][2];  //输出维度
            int characters = outputs0.Length / dimension;

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
            }
            

            List<int> no_repeat_blank_label = new List<int>();
            for (int elementIndex = 0; elementIndex < characters; ++elementIndex)
            {
                if (labels[elementIndex] != 0 && !(elementIndex > 0 && labels[elementIndex - 1] == labels[elementIndex]))
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

            return plate_text.ToString();
        }

        private Mat Preprocess(Mat srcImg)
        {
           
            int w = srcImg.Cols;
            
            int tarW = (int)(w / 32) * 32;
            if (tarW < w)
            {
                tarW += 32; // Adjust to the next multiple of 32
            }

            // Convert to grayscale
           // var grayImg = new Mat();
           // Cv2.CvtColor(srcImg, grayImg, ColorConversionCodes.RGBA2GRAY);

            // Create a new image with white padding
// Force height to 48 without scaling the width
            int tarH = 48;

            // Create a new image with white padding
            var paddedImg = new Mat(tarH, w, MatType.CV_8UC3, new Scalar(255, 255, 255));

            // Resize the original image to the target height while keeping the width unchanged
            var resizedImg = new Mat();
            Cv2.Resize(srcImg, resizedImg, new OpenCvSharp.Size(w, tarH));
            // Copy the original image to the center of the new image
            Cv2.CopyMakeBorder(resizedImg, paddedImg, 0, 0, 0, tarW - srcImg.Cols, BorderTypes.Isolated, new Scalar(255, 255, 255));

            return paddedImg;
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
                    inputImage[i*col+j+2]=(pix[0] /  255.0f -0.5f) / 0.5f;
                    inputImage[i*col+j+1]=(pix[1] / 255.0f -0.5f) / 0.5f;
                    inputImage[i*col+j]=(pix[2] /  255.0f -0.5f) / 0.5f;
                        
                }
            }
            return inputImage;
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }
}
