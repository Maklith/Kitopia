using System.Runtime.InteropServices;

namespace KitopiaTest.Services;

internal static class FfmpegTestAssets
{
    public static string ToolDirectory { get; } = Path.Combine(
        FindRepositoryRoot(), "artifacts", "ffmpeg", RuntimeInformation.RuntimeIdentifier, "tools", "ffmpeg");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Kitopia.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Kitopia repository root.");
    }
}
