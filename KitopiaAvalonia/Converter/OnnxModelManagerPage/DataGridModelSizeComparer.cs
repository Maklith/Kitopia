using System.Collections;
using System.IO;
using Core.SDKs.Services.Plugin;
using PluginCore.Onnx;

namespace KitopiaAvalonia.Converter.OnnxModelManagerPage;

public class DataGridModelSizeComparer : IComparer
{
    public static readonly DataGridModelSizeComparer Default = new DataGridModelSizeComparer();

    public int Compare(object? x, object? y)
    {
        if (x is OnnxModelInfoWrapper onnxModelInfoWrapper &&y is OnnxModelInfoWrapper onnxModelInfoWrapper2)
        {
            var fileInfo = new FileInfo(onnxModelInfoWrapper.Model.ModelPath);
            var fileInfo2 = new FileInfo(onnxModelInfoWrapper2.Model.ModelPath);
            if (!fileInfo2.Exists&&!fileInfo.Exists)
            {
                return 0;
            }
            if (!fileInfo2.Exists)
            {
                return 1;
            }
            if (!fileInfo.Exists)
            {
                return -1;
            }
            if (fileInfo.Length> fileInfo2.Length)
            {
                return 1;
            }

            if (fileInfo.Length < fileInfo2.Length)
            {
                return -1;
            }
        }

        return 0;
    }
}