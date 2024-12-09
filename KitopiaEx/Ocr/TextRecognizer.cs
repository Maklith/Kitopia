using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System.Text;

namespace PaddleOCRTestOnnx
{
    internal class TextRecognizer
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

        public TextRecognizer(string modelpath)
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

            using (StreamReader sr = new StreamReader("rec_word_dict.txt"))
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
            Normalize(dstimg);

            int[] input_shape_ = new int[] { 1, 3, inpHeight, inpWidth };

            var input_tensor_ = new DenseTensor<float>(input_image_.ToArray(), input_shape_);

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

        private Mat Preprocess(Mat srcimg)
        {
            Mat dstimg = new Mat();
            int h = srcimg.Rows;
            int w = srcimg.Cols;
            float ratio = w / (float)h;
            int resized_w = (int)Math.Ceiling((float)inpHeight * ratio);
            if (Math.Ceiling(inpHeight * ratio) > inpWidth)
            {
                resized_w = inpWidth;
            }

            Cv2.Resize(srcimg, dstimg, new OpenCvSharp.Size(resized_w, inpHeight), interpolation: InterpolationFlags.Linear);
            return dstimg;
        }

        private void Normalize(Mat img)
        {
            int row = img.Rows;
            int col = img.Cols;

            this.input_image_.Clear();

            for (int c = 0; c < 3; c++)
            {
                for (int i = 0; i < row; i++)
                {
                    for (int j = 0; j < col; j++)
                    {
                        float pix = img.Get<Vec3b>(i, j)[c];
                        this.input_image_.Add((pix / 255.0f -0.5f) / 0.5f);
                    }
                    for (int j = col; j < inpWidth; j++)
                    {
                        this.input_image_.Add(0.0f);
                    }
                }
            }
        }

    }
}
