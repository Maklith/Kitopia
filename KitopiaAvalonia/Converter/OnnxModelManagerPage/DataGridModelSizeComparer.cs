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
            var path = PluginManager.GetPluginByPlgStr(onnxModelInfoWrapper.PluginStr).Path;
            var fileInfo = new FileInfo($"{path}{onnxModelInfoWrapper.Model.ModelPath}");
            var path2 = PluginManager.GetPluginByPlgStr(onnxModelInfoWrapper2.PluginStr).Path;
            var fileInfo2 = new FileInfo($"{path2}{onnxModelInfoWrapper2.Model.ModelPath}");
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