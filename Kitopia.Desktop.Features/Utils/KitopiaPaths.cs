namespace Kitopia.Desktop.Features.Utils;

public static class KitopiaPaths
{
    private const string AppName = "Kitopia";

    public static string AppRoot { get; } = BuildAppRoot();
    internal static Exception? ConfigMigrationError { get; private set; }

    public static string ConfigsDirectory => EnsureDirectory("configs");
    public static string PluginsDirectory => EnsureDirectory("plugins");
    public static string LogsDirectory => EnsureDirectory("logs");
    public static string CustomScenariosDirectory => EnsureDirectory("customScenarios");
    public static string TempDirectory => EnsureDirectory("temp");
    public static string ReceivedFilesDirectory => EnsureDirectory("receivedFiles");

    public static string PortFilePath => Path.Combine(AppRoot, ".port");

    public static string GetConfigFilePath(string key) => Path.Combine(ConfigsDirectory, $"{key}.json");

    public static string GetCustomScenarioFilePath(string uuid) =>
        Path.Combine(CustomScenariosDirectory, $"{uuid}.json");

    public static string GetCustomScenarioIconPath(string uuid) =>
        Path.Combine(CustomScenariosDirectory, $"{uuid}.png");

    public static string GetPluginDirectory(string pluginSign) => Path.Combine(PluginsDirectory, pluginSign);

    public static string GetPluginAvatarPath(string pluginSign) =>
        Path.Combine(GetPluginDirectory(pluginSign), "avatar.png");

    public static string UserAvatarPath => Path.Combine(AppRoot, "user_avatar.png");

    public static string GetTempFilePath(string fileName) => Path.Combine(TempDirectory, fileName);

    private static string BuildAppRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppDomain.CurrentDomain.BaseDirectory;
        }

        var root = Path.Combine(localAppData, AppName);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string EnsureDirectory(string folderName)
    {
        var directory = Path.Combine(AppRoot, folderName);
        Directory.CreateDirectory(directory);
        TryMigrateLegacyDirectory(folderName, directory);
        return directory;
    }

    private static void TryMigrateLegacyDirectory(string folderName, string targetDirectory)
    {
        try
        {
            var legacyDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderName);
            MigrateLegacyDirectory(legacyDirectory, targetDirectory);
            if (folderName == "configs") ConfigMigrationError = null;
        }
        catch (Exception exception)
        {
            if (folderName == "configs") ConfigMigrationError = exception;
        }
    }

    internal static void MigrateLegacyDirectory(string legacyDirectory, string targetDirectory)
    {
        if (!Directory.Exists(legacyDirectory)
            || string.Equals(Path.GetFullPath(legacyDirectory), Path.GetFullPath(targetDirectory),
                StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var sourceFile in Directory.EnumerateFiles(legacyDirectory, "*", SearchOption.AllDirectories))
        {
            var targetFile = Path.Combine(targetDirectory, Path.GetRelativePath(legacyDirectory, sourceFile));
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            if (!File.Exists(targetFile))
                File.Copy(sourceFile, targetFile);
        }
    }
}
