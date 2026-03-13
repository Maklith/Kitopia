namespace Core.Services.Interfaces;

public interface IApplicationService
{
    public void Init();
    public void InitUrlProtocol();
    public bool ChangeAutoStart(bool autoStart);
    public void Exit(int exitCode = 0);
    public void Restart();
    public void Stop();
    public Task CheckUpdate(bool toastIfNoUpdate);
}
