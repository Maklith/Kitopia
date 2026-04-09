using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using OpenCvSharp;
using PluginCore;

namespace Core.Services.DeviceCommunication.FileTransfer;

public sealed class ClipboardAssetExtractor : IClipboardAssetExtractor
{
    private const string ClipboardImageTempFolderName = "KitopiaClipboardTransfers";
    private readonly IClipboardService _clipboardService;

    public ClipboardAssetExtractor(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public async Task<IReadOnlyList<string>> TryGetClipboardFilePathsAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard is null)
            {
                return (IReadOnlyList<string>)Array.Empty<string>();
            }

            try
            {
                var clipboardFiles = await clipboard.TryGetFilesAsync();
                var clipboardFileList = clipboardFiles?.ToList() ?? [];
                if (clipboardFileList.Count > 0)
                {
                    var localPaths = clipboardFileList
                        .Select(item => item.Path)
                        .Where(path => path is { IsAbsoluteUri: true, IsFile: true })
                        .Select(path => path!.LocalPath)
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Where(File.Exists)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (localPaths.Count > 0)
                    {
                        return (IReadOnlyList<string>)localPaths;
                    }
                }
            }
            catch
            {
            }

            try
            {
                var text = await clipboard.TryGetTextAsync();
                return ParseFilePathsFromClipboardText(text);
            }
            catch
            {
                return (IReadOnlyList<string>)Array.Empty<string>();
            }
        });
    }

    public string? TryExtractClipboardImageToTempFilePath()
    {
        if (!_clipboardService.HasImage())
        {
            return null;
        }

        try
        {
            using var image = _clipboardService.GetImage();
            if (image is null || image.Width <= 0 || image.Height <= 0)
            {
                return null;
            }

            var tempFolder = Path.Combine(Path.GetTempPath(), ClipboardImageTempFolderName);
            Directory.CreateDirectory(tempFolder);
            var filePath = Path.Combine(tempFolder, $"clipboard-image-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");
            return Cv2.ImWrite(filePath, image) ? filePath : null;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ParseFilePathsFromClipboardText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var rawLine in lines)
        {
            var candidatePath = rawLine.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                continue;
            }

            if (File.Exists(candidatePath))
            {
                result.Add(candidatePath);
            }
        }

        return result.Count == 0 ? Array.Empty<string>() : result.ToList();
    }
}
