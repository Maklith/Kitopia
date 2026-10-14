using System;
using System.Globalization;
using System.IO;
using System.Linq;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Converter.OnnxModelManagerPage;

internal static class OnnxModelSize
{
    private const long Kibibyte = 1024;
    private const long Mebibyte = Kibibyte * 1024;
    private const long Gibibyte = Mebibyte * 1024;

    public static bool TryGetTotalBytes(OnnxModelInfo model, out long totalBytes)
    {
        var files = new[] { model.ModelPath }
            .Concat(model.RequiredFiles)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => new FileInfo(path))
            .GroupBy(static file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(static files => files.First())
            .ToArray();

        if (files.Length == 0 || files.Any(static file => !file.Exists))
        {
            totalBytes = 0;
            return false;
        }

        totalBytes = files.Sum(static file => file.Length);
        return true;
    }

    public static string Format(long bytes, CultureInfo culture)
    {
        if (bytes >= Gibibyte)
            return $"{(bytes / (double)Gibibyte).ToString("0.##", culture)} GiB";
        if (bytes >= Mebibyte)
            return $"{(bytes / (double)Mebibyte).ToString("0.##", culture)} MiB";
        if (bytes >= Kibibyte)
            return $"{(bytes / (double)Kibibyte).ToString("0.##", culture)} KiB";

        return $"{bytes} B";
    }
}
