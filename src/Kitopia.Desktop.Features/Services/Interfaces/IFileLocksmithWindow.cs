using Kitopia.Desktop.Abstractions.FileSystem;

namespace Kitopia.Desktop.Features.Services.Interfaces;

public interface IFileLocksmithWindow
{
    void Show(List<FileLockInfo> processes);

    void ShowForScope(string? rootDir = null, IReadOnlyCollection<string>? targetPaths = null);
}
