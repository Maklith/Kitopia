namespace Core.Services.Interfaces;

public interface IFileLocksmithWindow
{
    void Show(List<LockingProcessInfo> processes);
}
