namespace Kitopia.Desktop.Abstractions.Shell;

public interface IDesktopShell
{
    void Open(string path, string? arguments = "", string? workingDirectory = "");

    void RunAsAdmin(string path, string arguments = "");

    void OpenFolderAndSelect(string path);
}
