namespace Core.Services;

public interface IApplicationService
{
    public void Init();
    public void InitUrlProtocol();
    public bool ChangeAutoStart(bool autoStart);
    public void Restart();
    public void Stop();
}