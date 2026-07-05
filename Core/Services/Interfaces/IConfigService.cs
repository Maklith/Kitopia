namespace Core.Services.Interfaces;

public interface IConfigService
{
    string? GetDeviceCustomName(string deviceId);
    void SetDeviceCustomName(string deviceId, string name);
    void RemoveDeviceCustomName(string deviceId);
}
