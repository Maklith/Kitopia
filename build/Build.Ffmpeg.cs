using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Fallout.Common;
using Fallout.Common.IO;
using Serilog;

partial class Build
{
    [Parameter("FFmpeg bundle runtime; omit to prepare all supported Windows runtimes")]
    readonly string FfmpegRuntime;

    Target BundleFfmpeg => _ => _
        .Executes(async () =>
        {
            if (!string.IsNullOrWhiteSpace(FfmpegRuntime))
                await BundleFfmpegAsync(FfmpegRuntime);
            else
            {
                await BundleFfmpegAsync("win-x64");
                await BundleFfmpegAsync("win-arm64");
            }
        });

    async Task BundleFfmpegAsync(string runtime)
    {
        var manifestPath = RootDirectory / "build" / "Ffmpeg" / "manifest.json";
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        if (root.GetProperty("license").GetString() != "LGPL-3.0-or-later")
            throw new InvalidDataException("The FFmpeg bundle must use LGPL.");
        if (!root.GetProperty("runtimes").TryGetProperty(runtime, out var binary))
            throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "No LGPL FFmpeg bundle is configured");

        using var handler = new HttpClientHandler { UseProxy = !IsRelease };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        var archivePath = await DownloadFfmpegArchiveAsync(client, binary);
        using var archive = ZipFile.OpenRead(archivePath);
        var prefix = binary.GetProperty("archiveRoot").GetString() + "/";
        var license = archive.GetEntry(prefix + "LICENSE.txt") ?? throw new InvalidDataException("Missing FFmpeg license");
        using (var reader = new StreamReader(license.Open()))
            if (!(await reader.ReadToEndAsync()).Contains("GNU LESSER GENERAL PUBLIC LICENSE", StringComparison.Ordinal))
                throw new InvalidDataException("Downloaded FFmpeg does not carry the LGPL license.");

        var destination = ArtifactsDirectory / "ffmpeg" / runtime / "tools" / "ffmpeg";
        destination.DeleteDirectory();
        Directory.CreateDirectory(destination);
        foreach (var name in new[] { "avutil-61.dll", "swresample-7.dll", "swscale-10.dll", "avcodec-63.dll",
                     "avformat-63.dll", "avfilter-12.dll", "avdevice-63.dll" })
        {
            var entry = archive.GetEntry(prefix + "bin/" + name) ?? throw new InvalidDataException($"Missing native library {name}");
            var path = Path.Combine(destination, name);
            entry.ExtractToFile(path);
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var machine = runtime == "win-x64" ? Machine.Amd64 : Machine.Arm64;
            if (pe.PEHeaders.CoffHeader.Machine != machine)
                throw new InvalidDataException($"FFmpeg {name} has the wrong architecture for {runtime}.");
        }
        license.ExtractToFile(Path.Combine(destination, "LICENSE.txt"));
        File.Copy(RootDirectory / "LICENSE", Path.Combine(destination, "GPL-3.0.txt"));
        File.Copy(manifestPath, Path.Combine(destination, "bundle.json"));
        File.Copy(RootDirectory / "build" / "Ffmpeg" / "README.md", Path.Combine(destination, "SOURCE.md"));
        Log.Information("Bundled LGPL FFmpeg native libraries for {Runtime} in {Directory}", runtime, destination);
    }

    async Task<string> DownloadFfmpegArchiveAsync(HttpClient client, JsonElement asset)
    {
        var url = asset.GetProperty("url").GetString()!;
        var checksum = asset.GetProperty("sha256").GetString()!;
        var cache = RootDirectory / ".fallout" / "temp" / "ffmpeg";
        Directory.CreateDirectory(cache);
        var path = Path.Combine(cache, checksum + ".zip");
        if (File.Exists(path))
        {
            await using var existing = File.OpenRead(path);
            if (Convert.ToHexString(await SHA256.HashDataAsync(existing)).Equals(checksum, StringComparison.OrdinalIgnoreCase))
                return path;
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Log.Information("Downloading pinned FFmpeg asset {Url}", url);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var output = File.Create(temporary)) await response.Content.CopyToAsync(output);
            await using (var downloaded = File.OpenRead(temporary))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(downloaded)).Equals(checksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"FFmpeg SHA256 verification failed: {url}");
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally { File.Delete(temporary); }
    }
}
