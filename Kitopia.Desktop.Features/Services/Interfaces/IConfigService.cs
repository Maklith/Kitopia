using System.Text.Json;
using Kitopia.Desktop.Features.Services.Config;
using PluginCore;
using PluginCore.Config;

namespace Kitopia.Desktop.Features.Services.Interfaces;

public interface IConfigService
{
    Version Version => throw new NotSupportedException();
    string ApiUrl => throw new NotSupportedException();
    Dictionary<string, ConfigBase> Configs => throw new NotSupportedException();
    KitopiaConfig Config => throw new NotSupportedException();
    JsonSerializerOptions DefaultOptions => throw new NotSupportedException();

    void Init() => throw new NotSupportedException();
    void RemoveConfig(string key) => throw new NotSupportedException();
    void RequsetUpdateHotKey(HotKeyModel hotKeyModel) => throw new NotSupportedException();
    void Save() => throw new NotSupportedException();
    void Save(string key) => throw new NotSupportedException();
}
