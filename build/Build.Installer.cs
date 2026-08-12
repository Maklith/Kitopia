using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.PowerShell;
using Serilog;

partial class Build
{
    Target PackInstallerX64 => _ => _
        .DependsOn(CreateRelease, PackWindowsX64)
        .OnlyWhenDynamic(() => Release is not null)
        .Executes(() => BuildInstaller("win-x64", "x86_64-pc-windows-msvc"));

    Target PackInstallerX86 => _ => _
        .DependsOn(CreateRelease, PackWindowsX86)
        .OnlyWhenDynamic(() => Release is not null)
        .Executes(() => BuildInstaller("win-x86", "i686-pc-windows-msvc"));

    Target PackInstallerArm64 => _ => _
        .DependsOn(CreateRelease, PackWindowsArm64)
        .OnlyWhenDynamic(() => Release is not null)
        .Executes(() => BuildInstaller("win-arm64", "aarch64-pc-windows-msvc"));

    Target PackInstaller => _ => _
        .DependsOn(PackInstallerX64, PackInstallerX86, PackInstallerArm64)
        .OnlyWhenDynamic(() => Release is not null);

    // Compatibility alias for the previous x64-only installer target.
    Target BuildNativeInstaller => _ => _
        .DependsOn(PackInstallerX64)
        .OnlyWhenDynamic(() => Release is not null);

    void BuildInstaller(string runtime, string rustTarget)
    {
        PrepareInstallerAssets(runtime);
        PowerShellTasks.PowerShell(
            $"scripts/build_release.ps1 -Target {rustTarget}",
            RootDirectory / "ModernInstallerR");

        var installer = RootDirectory / "ModernInstallerR" / "dist" / rustTarget / "ModernInstaller.exe";
        var artifact = RootDirectory /
                       $"Kitopia{AvaloniaProject.GetProperty("Version")}_{runtime}_Installer.exe";
        artifact.DeleteFile();
        File.Copy(installer, artifact);
        Log.Information("Created {Runtime} installer {Installer}", runtime, artifact);
        UploadReleaseAsset(artifact);

        if (runtime == "win-x64")
        {
            // Existing x64 clients only know the legacy asset name.
            var legacyArtifact = RootDirectory /
                                 $"Kitopia{AvaloniaProject.GetProperty("Version")}_Installer.exe";
            legacyArtifact.DeleteFile();
            File.Copy(installer, legacyArtifact);
            Log.Information("Created legacy update installer {Installer}", legacyArtifact);
            UploadReleaseAsset(legacyArtifact);
        }
    }

    void PrepareInstallerAssets(string runtime)
    {
        var installerAssets = RootDirectory / "ModernInstallerR" / "installer_assets";
        installerAssets.CreateDirectory();
        foreach (var file in (RootDirectory / "build" / "InstallerAssets").GetFiles())
            File.Copy(file, installerAssets / file.Name, true);

        WriteInstallerInfo(installerAssets / "info.json", runtime);

        var staging = ArtifactsDirectory / "installer-input" / runtime;
        staging.DeleteDirectory();
        CopyDirectory(ArtifactsDirectory / "windows" / runtime, staging);

        var pluginsArchive = installerAssets / "plugins.zip";
        pluginsArchive.DeleteFile();
        (staging / "plugins").ZipTo(pluginsArchive,
            compressionLevel: CompressionLevel.SmallestSize, fileMode: FileMode.Create);
        (staging / "plugins").DeleteDirectory();

        var bgeModelArchive = installerAssets / "BGE_Model.zip";
        bgeModelArchive.DeleteFile();
        (staging / "BGE_Model").ZipTo(bgeModelArchive,
            compressionLevel: CompressionLevel.SmallestSize, fileMode: FileMode.Create);
        (staging / "BGE_Model").DeleteDirectory();

        var appArchive = installerAssets / "App.zip";
        appArchive.DeleteFile();
        staging.ZipTo(appArchive, compressionLevel: CompressionLevel.SmallestSize, fileMode: FileMode.Create);
    }

    void WriteInstallerInfo(AbsolutePath infoPath, string runtime)
    {
        var runtimeArchitecture = runtime switch
        {
            "win-x64" => (Is64: true, Name: "x64", FileName: "windowsdesktop-runtime-win-x64.exe"),
            "win-x86" => (Is64: false, Name: "x86", FileName: "windowsdesktop-runtime-win-x86.exe"),
            "win-arm64" => (Is64: true, Name: "ARM64", FileName: "windowsdesktop-runtime-win-arm64.exe"),
            _ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "Unsupported installer runtime")
        };

        var info = JsonNode.Parse(File.ReadAllText(infoPath))?.AsObject()
                   ?? throw new InvalidDataException($"Invalid installer info JSON: {infoPath}");
        info["DisplayVersion"] = AvaloniaProject.GetProperty("Version");
        info["Is64"] = runtimeArchitecture.Is64;

        var dependency = info["InstallDependencies"]?.AsArray()[0]?.AsObject()
                         ?? throw new InvalidDataException("Installer info has no install dependency entry.");
        dependency["Name"] = $".NET 10 Windows Desktop Runtime ({runtimeArchitecture.Name})";
        dependency["Url"] = $"https://aka.ms/dotnet/10.0/{runtimeArchitecture.FileName}";
        dependency["FileName"] = runtimeArchitecture.FileName;

        File.WriteAllText(infoPath, info.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    static void CopyDirectory(AbsolutePath source, AbsolutePath destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
    }
}
