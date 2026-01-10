namespace Core.SDKs.Services;

public interface IShellUtils
{
    void Open(string path, string arguments = "", string workingDirectory = "");
    void RunAsAdmin(string path, string arguments = "");
    void OpenFolderAndSelect(string path);
}
