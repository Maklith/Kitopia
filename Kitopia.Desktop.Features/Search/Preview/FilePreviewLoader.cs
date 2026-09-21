using System.IO.Compression;
using System.Text;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Kitopia.Desktop.Features.Search.Preview;

public static class FilePreviewLoader
{
    private const int TextLimit = 256 * 1024;
    private const int EntryLimit = 500;

    public static Task<FilePreviewContent> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                var entries = new List<FilePreviewEntry>();
                foreach (var item in directory.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entries.Count == EntryLimit) break;
                    entries.Add(new FilePreviewEntry(item.Name,
                        item is FileInfo file ? FormatSize(file.Length) : "文件夹", item is DirectoryInfo));
                }
                return new FilePreviewContent(directory.Name, $"文件夹 · 修改于 {directory.LastWriteTime:g}",
                    Entries: entries, Notice: entries.Count == EntryLimit ? "显示前 500 项" : $"{entries.Count} 个项目");
            }

            var info = new FileInfo(path);
            var details = $"{info.Extension.TrimStart('.').ToUpperInvariant()} · {FormatSize(info.Length)} · 修改于 {info.LastWriteTime:g}";
            var extension = info.Extension.ToLowerInvariant();
            if (extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff" or ".ico")
            {
                await using var stream = File.OpenRead(path);
                int width, height;
                using (var codec = SKCodec.Create(new SKManagedStream(stream, disposeManagedStream: false)))
                {
                    if (codec is null) throw new InvalidDataException("图片已损坏或编码不受支持。");
                    width = codec.Info.Width;
                    height = codec.Info.Height;
                }
                stream.Position = 0;
                var image = width >= height
                    ? Bitmap.DecodeToWidth(stream, Math.Min(width, 2048))
                    : Bitmap.DecodeToHeight(stream, Math.Min(height, 2048));
                if (cancellationToken.IsCancellationRequested)
                {
                    image.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return new FilePreviewContent(info.Name, $"{width} × {height} · {details}", Image: image);
            }

            if (extension is ".zip" or ".nupkg" or ".jar" or ".apk")
            {
                using var archive = ZipFile.OpenRead(path);
                var entries = new List<FilePreviewEntry>(Math.Min(archive.Entries.Count, EntryLimit));
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entries.Count == EntryLimit) break;
                    var isDirectory = entry.FullName.EndsWith('/');
                    entries.Add(new FilePreviewEntry(entry.FullName,
                        isDirectory ? "文件夹" : FormatSize(entry.Length), isDirectory));
                }
                return new FilePreviewContent(info.Name, details, Entries: entries,
                    Notice: archive.Entries.Count > EntryLimit ? $"共 {archive.Entries.Count:N0} 项，显示前 500 项" : $"{entries.Count} 个项目");
            }

            if (extension is ".txt" or ".md" or ".markdown" or ".log" or ".json" or ".xml" or ".yaml" or ".yml"
                or ".csv" or ".tsv" or ".ini" or ".toml" or ".config" or ".cs" or ".csproj" or ".sln"
                or ".js" or ".jsx" or ".ts" or ".tsx" or ".css" or ".html" or ".htm" or ".py" or ".rs"
                or ".c" or ".cpp" or ".h" or ".java" or ".go" or ".sql" or ".sh" or ".ps1" or ".bat"
                or ".axaml" or ".xaml" or ".gitignore" or "")
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, true);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var buffer = new char[TextLimit + 1];
                var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
                if (!buffer.AsSpan(0, count).Contains('\0'))
                    return new FilePreviewContent(info.Name, details, Text: new string(buffer, 0, Math.Min(count, TextLimit)),
                        Notice: count > TextLimit ? "文件较大，仅显示前 256K 个字符" : null);
            }

            return new FilePreviewContent(info.Name, details, NativePath: path);
        }, cancellationToken);
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024 * 1024 => $"{bytes / (1024d * 1024):0.##} MB",
        >= 1024 => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes} B"
    };
}
