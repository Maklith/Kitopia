using System.IO.Compression;
using System.Text;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Kitopia.Feature.Localization;

namespace Kitopia.Desktop.Features.Search.Preview;

public static class FilePreviewLoader
{
    private const int TextLimit = 256 * 1024;
    private const int EntryLimit = 500;

    public static Task<FilePreviewContent> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        LoadAsync(path, null, extractArchiveFile: false, cancellationToken);

    public static Task<FilePreviewContent> LoadAsync(string path, string? archiveSubPath, CancellationToken cancellationToken = default) =>
        LoadAsync(path, archiveSubPath, extractArchiveFile: false, cancellationToken);

    public static Task<FilePreviewContent> LoadAsync(
        string path,
        string? archiveSubPath,
        bool extractArchiveFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                var entries = new List<FilePreviewEntry>();
                foreach (var subDirectory in directory.EnumerateDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entries.Count == EntryLimit) break;
                    entries.Add(new FilePreviewEntry(
                        subDirectory.Name,
                        Lang.Get("lang.kitopia.messages.folder"),
                        IsDirectory: true,
                        TargetPath: subDirectory.FullName));
                }
                foreach (var file in directory.EnumerateFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entries.Count == EntryLimit) break;
                    entries.Add(new FilePreviewEntry(
                        file.Name,
                        FormatSize(file.Length),
                        IsDirectory: false,
                        TargetPath: file.FullName));
                }
                return new FilePreviewContent(
                    directory.Name,
                    Lang.Format("lang.kitopia.messages.folder_modified_value", directory.LastWriteTime),
                    Entries: entries,
                    Notice: entries.Count == EntryLimit
                        ? Lang.Get("lang.kitopia.messages.showing_the_first_500_items")
                        : Lang.Format("lang.kitopia.messages.value_items", entries.Count),
                    CurrentDirectoryPath: directory.FullName);
            }

            var info = new FileInfo(path);
            var details = Lang.Format("lang.kitopia.messages.value_value_modified_value", info.Extension.TrimStart('.').ToUpperInvariant(), FormatSize(info.Length), info.LastWriteTime);
            var extension = info.Extension.ToLowerInvariant();
            if (IsArchiveExtension(extension))
            {
                return await LoadArchiveAsync(info, details, archiveSubPath, extractArchiveFile, cancellationToken);
            }

            return await LoadDiskFileAsync(info, details, extension, currentDirectoryPath: null, extractedPath: null, cancellationToken);
        }, cancellationToken);
    }

    public static bool IsArchiveExtension(string extension) =>
        extension is ".zip" or ".nupkg" or ".jar" or ".apk";

    private static bool IsImageExtension(string extension) =>
        extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff" or ".ico" or ".webp";

    private static bool IsTextExtension(string extension) =>
        extension is ".txt" or ".md" or ".markdown" or ".log" or ".json" or ".xml" or ".yaml" or ".yml"
            or ".csv" or ".tsv" or ".ini" or ".toml" or ".config" or ".cs" or ".csproj" or ".sln"
            or ".js" or ".jsx" or ".ts" or ".tsx" or ".css" or ".html" or ".htm" or ".py" or ".rs"
            or ".c" or ".cpp" or ".h" or ".java" or ".go" or ".sql" or ".sh" or ".ps1" or ".bat"
            or ".axaml" or ".xaml" or ".gitignore" or "";

    private static async Task<FilePreviewContent> LoadDiskFileAsync(
        FileInfo info,
        string details,
        string extension,
        string? currentDirectoryPath,
        string? extractedPath,
        CancellationToken cancellationToken)
    {
        if (IsImageExtension(extension))
        {
            await using var stream = File.OpenRead(info.FullName);
            return DecodeImagePreview(stream, info.Name, details, extension, info.FullName, currentDirectoryPath, extractedPath, cancellationToken);
        }

        if (IsTextExtension(extension))
        {
            await using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, true);
            var textPreview = await TryReadTextPreviewAsync(stream, info.Name, details, currentDirectoryPath, extractedPath, cancellationToken);
            if (textPreview is not null) return textPreview;
        }

        return new FilePreviewContent(info.Name, details, NativePath: info.FullName, CurrentDirectoryPath: currentDirectoryPath, ExtractedPath: extractedPath);
    }

    private static async Task<FilePreviewContent> LoadArchiveAsync(
        FileInfo archiveInfo,
        string archiveDetails,
        string? archiveSubPath,
        bool extractArchiveFile,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.Open(archiveInfo.FullName, ZipArchiveMode.Read, LegacyZipEntryEncoding.Instance);
        var normalizedSubPath = NormalizeArchivePath(archiveSubPath);
        var prefix = normalizedSubPath.Length == 0 ? "" : normalizedSubPath + "/";
        var directories = new Dictionary<string, FilePreviewEntry>(StringComparer.Ordinal);
        var files = new List<FilePreviewEntry>();
        ZipArchiveEntry? matchedFileEntry = null;
        var matchedDirectory = normalizedSubPath.Length == 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rawName = entry.FullName.Replace('\\', '/');
            if (normalizedSubPath.Length == 0
                && (rawName.StartsWith("../", StringComparison.Ordinal) || rawName.StartsWith("./", StringComparison.Ordinal)))
            {
                var isRelativeDir = rawName.EndsWith('/');
                if (isRelativeDir)
                {
                    directories.TryAdd(rawName, new FilePreviewEntry(rawName, Lang.Get("lang.kitopia.messages.folder"), true, archiveInfo.FullName, rawName));
                }
                else
                {
                    files.Add(new FilePreviewEntry(rawName, FormatSize(entry.Length), false, archiveInfo.FullName, rawName));
                }
                continue;
            }

            var fullName = rawName.TrimStart('/');
            if (fullName.Length == 0) continue;

            if (normalizedSubPath.Length > 0 && !rawName.EndsWith('/') && string.Equals(fullName, normalizedSubPath, StringComparison.Ordinal))
            {
                matchedFileEntry ??= entry;
            }

            if (prefix.Length > 0)
            {
                if (!fullName.StartsWith(prefix, StringComparison.Ordinal)) continue;
                matchedDirectory = true;
                if (fullName.Length == prefix.Length) continue;
            }

            var relative = fullName[prefix.Length..];
            var slashIndex = relative.IndexOf('/');
            if (slashIndex >= 0)
            {
                var folderName = relative[..slashIndex];
                if (folderName.Length > 0 && !directories.ContainsKey(folderName))
                {
                    directories[folderName] = new FilePreviewEntry(
                        folderName,
                        Lang.Get("lang.kitopia.messages.folder"),
                        IsDirectory: true,
                        TargetPath: archiveInfo.FullName,
                        ArchiveEntryPath: prefix + folderName);
                }
            }
            else
            {
                files.Add(new FilePreviewEntry(
                    relative,
                    FormatSize(entry.Length),
                    IsDirectory: false,
                    TargetPath: archiveInfo.FullName,
                    ArchiveEntryPath: prefix + relative));
            }
        }

        if (!matchedDirectory && matchedFileEntry is not null)
        {
            return await LoadArchiveFileEntryAsync(archiveInfo, matchedFileEntry, normalizedSubPath, extractArchiveFile, cancellationToken);
        }

        var totalCount = directories.Count + files.Count;
        var entries = new List<FilePreviewEntry>(Math.Min(totalCount, EntryLimit));
        foreach (var dir in directories.Values)
        {
            if (entries.Count == EntryLimit) break;
            entries.Add(dir);
        }
        foreach (var file in files)
        {
            if (entries.Count == EntryLimit) break;
            entries.Add(file);
        }

        var displayName = normalizedSubPath.Length == 0 ? archiveInfo.Name : Path.GetFileName(normalizedSubPath);
        var displayDetails = normalizedSubPath.Length == 0
            ? archiveDetails
            : $"{Lang.Get("lang.kitopia.messages.folder")} · {archiveInfo.Name}";
        var currentDirectoryPath = normalizedSubPath.Length == 0
            ? archiveInfo.Name
            : $"{archiveInfo.Name}/{normalizedSubPath}";
        var notice = totalCount > EntryLimit
            ? Lang.Format("lang.kitopia.messages.value_items_showing_the_first_500", totalCount)
            : Lang.Format("lang.kitopia.messages.value_items", entries.Count);

        return new FilePreviewContent(
            displayName,
            displayDetails,
            Entries: entries,
            Notice: notice,
            CurrentDirectoryPath: currentDirectoryPath);
    }

    private static async Task<FilePreviewContent> LoadArchiveFileEntryAsync(
        FileInfo archiveInfo,
        ZipArchiveEntry entry,
        string normalizedSubPath,
        bool extractArchiveFile,
        CancellationToken cancellationToken)
    {
        var entryName = Path.GetFileName(normalizedSubPath);
        var extension = Path.GetExtension(entryName).ToLowerInvariant();
        var details = Lang.Format(
            "lang.kitopia.messages.value_value_modified_value",
            extension.TrimStart('.').ToUpperInvariant(),
            FormatSize(entry.Length),
            entry.LastWriteTime.LocalDateTime);
        var location = $"{archiveInfo.Name}/{normalizedSubPath}";

        if (!extractArchiveFile)
        {
            return new FilePreviewContent(
                entryName,
                details,
                CurrentDirectoryPath: location,
                RequiresArchiveExtraction: true);
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "Kitopia", "ArchivePreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var safeFileName = string.IsNullOrWhiteSpace(entryName) ? "preview" : string.Join("_", entryName.Split(Path.GetInvalidFileNameChars()));
        var extractedPath = Path.Combine(tempDirectory, safeFileName);
        try
        {
            await using (var source = entry.Open())
            await using (var destination = new FileStream(extractedPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }
            if (entry.LastWriteTime.Year >= 1980)
            {
                File.SetLastWriteTime(extractedPath, entry.LastWriteTime.LocalDateTime);
            }
        }
        catch
        {
            try { Directory.Delete(tempDirectory, recursive: true); } catch { /* ignore cleanup failure */ }
            throw;
        }

        var extractedInfo = new FileInfo(extractedPath);
        if (IsArchiveExtension(extension))
        {
            var nestedArchive = await LoadArchiveAsync(extractedInfo, details, archiveSubPath: null, extractArchiveFile: false, cancellationToken);
            return nestedArchive with { CurrentDirectoryPath = location, ExtractedPath = extractedPath };
        }

        return await LoadDiskFileAsync(extractedInfo, details, extension, location, extractedPath, cancellationToken);
    }

    private static FilePreviewContent DecodeImagePreview(
        Stream stream,
        string name,
        string details,
        string extension,
        string nativePath,
        string? currentDirectoryPath,
        string? extractedPath,
        CancellationToken cancellationToken)
    {
        int width, height;
        using (var codec = SKCodec.Create(new SKManagedStream(stream, disposeManagedStream: false)))
        {
            if (codec is null) throw new InvalidDataException(Lang.Get("lang.kitopia.messages.the_image_is_damaged_or_its_encoding_is_unsupported"));
            if (extension == ".webp" && codec.FrameCount > 1)
                return new FilePreviewContent(name, details, NativePath: nativePath, CurrentDirectoryPath: currentDirectoryPath, ExtractedPath: extractedPath);
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
        return new FilePreviewContent(name, $"{width} × {height} · {details}", Image: image, CurrentDirectoryPath: currentDirectoryPath, ExtractedPath: extractedPath);
    }

    private static async Task<FilePreviewContent?> TryReadTextPreviewAsync(
        Stream stream,
        string name,
        string details,
        string? currentDirectoryPath,
        string? extractedPath,
        CancellationToken cancellationToken)
    {
        var encoding = DetectTextEncoding(stream);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var buffer = new char[TextLimit + 1];
        var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        if (buffer.AsSpan(0, count).Contains('\0')) return null;
        return new FilePreviewContent(
            name,
            details,
            Text: new string(buffer, 0, Math.Min(count, TextLimit)),
            Notice: count > TextLimit ? Lang.Get("lang.kitopia.messages.large_file_showing_the_first_256k_characters") : null,
            CurrentDirectoryPath: currentDirectoryPath,
            ExtractedPath: extractedPath);
    }

    private static Encoding DetectTextEncoding(Stream stream)
    {
        Span<byte> prefix = stackalloc byte[4];
        var read = stream.Read(prefix);
        stream.Position = 0;
        if (read >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
            return Encoding.UTF8;
        if (read >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE)
            return Encoding.Unicode;
        if (read >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        var sampleLength = (int)Math.Min(stream.Length, 16 * 1024);
        var sample = new byte[sampleLength];
        if (sampleLength > 0)
        {
            stream.ReadExactly(sample);
            stream.Position = 0;
        }

        if (IsValidUtf8(sample, allowIncompleteTrailingSequence: sampleLength < stream.Length, allowTwoByteSequences: true))
            return Encoding.UTF8;

        return LegacyZipEntryEncoding.Gb18030;
    }

    internal static string NormalizeArchivePath(string? archiveSubPath) =>
        string.IsNullOrWhiteSpace(archiveSubPath)
            ? ""
            : archiveSubPath.Replace('\\', '/').Trim('/');

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes, bool allowIncompleteTrailingSequence, bool allowTwoByteSequences)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            var first = bytes[index];
            if (first <= 0x7F) continue;

            var sequenceLength = first switch
            {
                >= 0xC2 and <= 0xDF when allowTwoByteSequences => 2,
                >= 0xE0 and <= 0xEF => 3,
                >= 0xF0 and <= 0xF4 => 4,
                _ => 0
            };
            if (sequenceLength == 0) return false;
            if (index + sequenceLength > bytes.Length) return allowIncompleteTrailingSequence;

            var second = bytes[index + 1];
            if (second is < 0x80 or > 0xBF
                || first == 0xE0 && second < 0xA0
                || first == 0xED && second >= 0xA0
                || first == 0xF0 && second < 0x90
                || first == 0xF4 && second > 0x8F)
            {
                return false;
            }

            for (var continuationIndex = 2; continuationIndex < sequenceLength; continuationIndex++)
            {
                if (bytes[index + continuationIndex] is < 0x80 or > 0xBF) return false;
            }

            index += sequenceLength - 1;
        }

        return true;
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024 * 1024 => $"{bytes / (1024d * 1024):0.##} MB",
        >= 1024 => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes} B"
    };

    private sealed class LegacyZipEntryEncoding : Encoding
    {
        public static readonly Encoding Gb18030;
        public static readonly LegacyZipEntryEncoding Instance = new();

        static LegacyZipEntryEncoding()
        {
            RegisterProvider(CodePagesEncodingProvider.Instance);
            Gb18030 = GetEncoding("GB18030");
        }

        public override string GetString(byte[] bytes, int index, int count)
        {
            var span = bytes.AsSpan(index, count);
            return IsValidUtf8(span, allowIncompleteTrailingSequence: false, allowTwoByteSequences: false)
                ? UTF8.GetString(span)
                : Gb18030.GetString(span);
        }

        public override int GetCharCount(byte[] bytes, int index, int count) =>
            GetString(bytes, index, count).Length;

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            var text = GetString(bytes, byteIndex, byteCount);
            text.CopyTo(0, chars, charIndex, text.Length);
            return text.Length;
        }

        public override int GetByteCount(char[] chars, int index, int count) =>
            UTF8.GetByteCount(chars, index, count);

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) =>
            UTF8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

        public override int GetMaxByteCount(int charCount) => UTF8.GetMaxByteCount(charCount);

        public override int GetMaxCharCount(int byteCount) =>
            Math.Max(UTF8.GetMaxCharCount(byteCount), Gb18030.GetMaxCharCount(byteCount));
    }
}
