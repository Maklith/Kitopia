namespace Kitopia.Desktop.Features.Services.Interfaces;

public interface IApplicationService
{
    public void Init();
    public void InitUrlProtocol();
    public Task<bool> ChangeAutoStartAsync(bool autoStart, bool preferEarlyStartup, bool allowElevation);
    public Task ExitAsync(int exitCode = 0);
    public Task RestartAsync();
    public Task StopAsync();
    public Task<bool> CheckUpdate(bool toastIfNoUpdate);
}
