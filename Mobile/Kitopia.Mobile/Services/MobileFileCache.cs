namespace Kitopia.Mobile.Services;

public static class MobileFileCache
{
    private static readonly HashSet<char> InvalidFileNameCharacters =
    [
        .. Path.GetInvalidFileNameChars(),
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    ];

    public static string CreatePath(string cacheRoot, string? originalFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);

        var directory = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, SanitizeFileName(originalFileName));
    }

    public static string SanitizeFileName(string? fileName)
    {
        var name = fileName?.Trim() ?? string.Empty;
        var separatorIndex = name.LastIndexOfAny(['/', '\\']);
        if (separatorIndex >= 0)
        {
            name = name[(separatorIndex + 1)..];
        }

        var sanitized = new string(name
            .Select(character => InvalidFileNameCharacters.Contains(character) || char.IsControl(character)
                ? '_'
                : character)
            .ToArray())
            .Trim()
            .TrimEnd('.');

        return string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized;
    }
}
