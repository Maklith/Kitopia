namespace Core.Services.Interfaces;

public interface IApplicationService
{
    public void Init();
    public void InitUrlProtocol();
    public bool ChangeAutoStart(bool autoStart);
    public Task ExitAsync(int exitCode = 0);
    public Task RestartAsync();
    public Task StopAsync();
    public Task<bool> CheckUpdate(bool toastIfNoUpdate);
}
