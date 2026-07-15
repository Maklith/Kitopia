using System;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

partial class Build
{
    Target RestoreAndroid => _ => _
        .Executes(() => DotNetRestore(c => c.SetProjectFile(AndroidProjectFile)));

    Target PackAndroidX64 => _ => _
        .DependsOn(CreateRelease, RestoreAndroid)
        .Executes(() => PublishAndroid("android-x64"));

    Target PackAndroidArm64 => _ => _
        .DependsOn(CreateRelease, RestoreAndroid)
        .Executes(() => PublishAndroid("android-arm64"));

    Target PackAndroid => _ => _
        .DependsOn(PackAndroidX64, PackAndroidArm64);

    void PublishAndroid(string runtime)
    {
        var output = ArtifactsDirectory / "android" / runtime;
        output.DeleteDirectory();
        var androidSdkDirectory = ResolveAndroidSdkDirectory();

        DotNetPublish(c =>
        {
            c = c
                .SetProject(AndroidProjectFile)
                .SetOutput(output)
                .SetRuntime(runtime)
                .SetFramework("net10.0-android36.0")
                .SetConfiguration(ReleaseConfiguration)
                .SetProperty("PublishAot", true);

            return androidSdkDirectory is null
                ? c
                : c.SetProperty("AndroidSdkDirectory", androidSdkDirectory);
        });

        RemoveSymbolsAndDocs(output);
        var apk = Directory.EnumerateFiles(output, "*.apk", SearchOption.AllDirectories)
            .OrderByDescending(path => path.EndsWith("-Signed.apk", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (apk is null)
            throw new InvalidOperationException($"Android publish did not produce an APK for {runtime}: {output}");

        var artifact = RootDirectory / $"Kitopia.Mobile_{runtime}.apk";
        artifact.DeleteFile();
        File.Copy(apk, artifact);
        Log.Information("Created Android artifact {Artifact}", artifact);
        UploadReleaseAsset(artifact);
    }

    static string ResolveAndroidSdkDirectory()
    {
        foreach (var variableName in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var configuredPath = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrWhiteSpace(configuredPath) && Directory.Exists(configuredPath))
                return configuredPath;
        }

        if (!OperatingSystem.IsWindows())
            return null;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userSdk = Path.Combine(localAppData, "Android", "Sdk");
        return Directory.Exists(Path.Combine(userSdk, "ndk")) ? userSdk : null;
    }
}
