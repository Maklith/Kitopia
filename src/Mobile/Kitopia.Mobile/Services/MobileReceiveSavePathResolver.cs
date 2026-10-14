namespace Kitopia.Mobile.Services;

public static class MobileReceiveSavePathResolver
{
    public static string ResolveIncomingPath(string rootDirectory, string suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var incomingDirectory = Path.Combine(rootDirectory, "Incoming");
        Directory.CreateDirectory(incomingDirectory);

        var fileName = SanitizeFileName(suggestedFileName);
        return Path.Combine(incomingDirectory, fileName);
    }

    private static string SanitizeFileName(string? suggestedFileName)
    {
        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "received-file"
            : Path.GetFileName(suggestedFileName);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "received-file";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName;
    }
}
