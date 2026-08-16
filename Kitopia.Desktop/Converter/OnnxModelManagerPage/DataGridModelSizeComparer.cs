using System.Collections;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Converter.OnnxModelManagerPage;

public class DataGridModelSizeComparer : IComparer
{
    public static readonly DataGridModelSizeComparer Default = new();

    public int Compare(object? x, object? y)
    {
        if (x is OnnxModelInfoWrapper onnxModelInfoWrapper && y is OnnxModelInfoWrapper onnxModelInfoWrapper2)
        {
            var hasSize = OnnxModelSize.TryGetTotalBytes(onnxModelInfoWrapper.Model, out var size);
            var hasSize2 = OnnxModelSize.TryGetTotalBytes(onnxModelInfoWrapper2.Model, out var size2);
            if (!hasSize && !hasSize2) return 0;
            if (!hasSize2) return 1;
            if (!hasSize) return -1;
            return size.CompareTo(size2);
        }

        return 0;
    }
}
