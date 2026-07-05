using System.Text.Json;
using Core.Services.Interfaces;

namespace Kitopia.Mobile.Services;

public sealed class MobileConfigService : IConfigService
{
    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kitopia", "mobile_custom_names.json");

    public string? GetDeviceCustomName(string deviceId)
    {
        var names = LoadAll();
        return names.TryGetValue(deviceId, out var name) ? name : null;
    }

    public void SetDeviceCustomName(string deviceId, string name)
    {
        var names = LoadAll();
        names[deviceId] = name;
        SaveAll(names);
    }

    public void RemoveDeviceCustomName(string deviceId)
    {
        var names = LoadAll();
        names.Remove(deviceId);
        SaveAll(names);
    }

    private static Dictionary<string, string> LoadAll()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath))
                       ?? new Dictionary<string, string>();
            }
        }
        catch { }
        return new Dictionary<string, string>();
    }

    private static void SaveAll(Dictionary<string, string> names)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(names));
    }
}
