using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

        var destination = ArtifactsDirectory / "ffmpeg" / runtime / "tools" / "ffmpeg";
        var nativeFiles = new[] { "avutil-61.dll", "swresample-7.dll", "swscale-10.dll", "avcodec-63.dll",
            "avformat-63.dll", "avfilter-12.dll", "avdevice-63.dll", "ffmpeg.exe", "ffprobe.exe" };
        var bundleManifestPath = Path.Combine(destination, "bundle.json");
        var licensePath = Path.Combine(destination, "LICENSE.txt");
        var reuseBundle = false;
        if (File.Exists(bundleManifestPath) && File.Exists(licensePath) &&
            nativeFiles.All(name => File.Exists(Path.Combine(destination, name))))
        {
            try
            {
                using var bundledManifest = JsonDocument.Parse(File.ReadAllText(bundleManifestPath));
                reuseBundle = JsonElement.DeepEquals(root, bundledManifest.RootElement);
            }
            catch (JsonException)
            {
                Log.Warning("Ignoring invalid FFmpeg bundle manifest {Manifest}", bundleManifestPath);
            }
        }

        if (reuseBundle)
            Log.Information("Reusing existing FFmpeg bundle for {Runtime}", runtime);
        else
        {
            var archivePath = await ResolveFfmpegArchiveAsync(binary, runtime);
            using var archive = ZipFile.OpenRead(archivePath);
            var archiveRoot = binary.TryGetProperty("archiveRoot", out var rootProp) && !string.IsNullOrWhiteSpace(rootProp.GetString())
                ? rootProp.GetString()!
                : (archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("LICENSE.txt", StringComparison.OrdinalIgnoreCase))?.FullName[..^"LICENSE.txt".Length].TrimEnd('/')
                   ?? throw new InvalidDataException("Missing FFmpeg license"));
            var prefix = string.IsNullOrEmpty(archiveRoot) ? "" : archiveRoot + "/";
            var license = archive.GetEntry(prefix + "LICENSE.txt") ?? throw new InvalidDataException("Missing FFmpeg license");
            using (var reader = new StreamReader(license.Open()))
                if (!(await reader.ReadToEndAsync()).Contains("GNU LESSER GENERAL PUBLIC LICENSE", StringComparison.Ordinal))
                    throw new InvalidDataException("FFmpeg archive does not carry the LGPL license.");

            destination.DeleteDirectory();
            Directory.CreateDirectory(destination);
            foreach (var name in nativeFiles)
            {
                var entry = archive.GetEntry(prefix + "bin/" + name) ?? throw new InvalidDataException($"Missing native library {name}");
                entry.ExtractToFile(Path.Combine(destination, name));
            }
            license.ExtractToFile(licensePath);
        }

        foreach (var name in nativeFiles)
        {
            var path = Path.Combine(destination, name);
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var machine = runtime == "win-x64" ? Machine.Amd64 : Machine.Arm64;
            if (pe.PEHeaders.CoffHeader.Machine != machine)
                throw new InvalidDataException($"FFmpeg {name} has the wrong architecture for {runtime}.");
        }
        if (reuseBundle)
        {
            using var reader = File.OpenText(licensePath);
            if (!(await reader.ReadToEndAsync()).Contains("GNU LESSER GENERAL PUBLIC LICENSE", StringComparison.Ordinal))
                throw new InvalidDataException("The existing FFmpeg bundle does not carry the LGPL license.");
        }
        else
            File.Copy(manifestPath, bundleManifestPath);
        File.Copy(RootDirectory / "LICENSE", Path.Combine(destination, "GPL-3.0.txt"), overwrite: true);
        File.Copy(RootDirectory / "build" / "Ffmpeg" / "README.md", Path.Combine(destination, "SOURCE.md"), overwrite: true);
        Log.Information("Bundled LGPL FFmpeg tools and native libraries for {Runtime} in {Directory}", runtime, destination);
    }

    async Task<string> ResolveFfmpegArchiveAsync(JsonElement asset, string runtime)
    {
        var checksum = asset.TryGetProperty("sha256", out var checksumProp) ? checksumProp.GetString() : null;

        string localPath = null;
        if (asset.TryGetProperty("path", out var pathProp) && !string.IsNullOrWhiteSpace(pathProp.GetString()))
        {
            var configured = Path.IsPathRooted(pathProp.GetString())
                ? (AbsolutePath)pathProp.GetString()!
                : RootDirectory / pathProp.GetString();
            if (File.Exists(configured))
                localPath = configured;
        }

        if (localPath is null && asset.TryGetProperty("archiveRoot", out var rootProp) && !string.IsNullOrWhiteSpace(rootProp.GetString()))
        {
            var conventional = RootDirectory / "build" / "Ffmpeg" / (rootProp.GetString() + ".zip");
            if (File.Exists(conventional))
                localPath = conventional;
        }

        if (localPath is not null)
        {
            if (!string.IsNullOrWhiteSpace(checksum))
            {
                await using var stream = File.OpenRead(localPath);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                if (!actual.Equals(checksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"FFmpeg SHA256 verification failed for local archive {localPath}. Expected: {checksum}, Actual: {actual}");
            }
            Log.Information("Using local FFmpeg archive for {Runtime}: {Path}", runtime, localPath);
            return localPath;
        }

        if (asset.TryGetProperty("url", out var urlProp) && !string.IsNullOrWhiteSpace(urlProp.GetString()))
        {
            using var handler = new HttpClientHandler { UseProxy = !IsRelease };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            return await DownloadFfmpegArchiveAsync(client, asset);
        }

        throw new FileNotFoundException($"No local FFmpeg archive found and no URL configured for runtime {runtime}.");
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
