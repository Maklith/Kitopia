using Core.SDKs.HotKey;
using PluginCore;

namespace Core.SDKs.Services;

public interface IHotKetImpl
{
    public void Init();
    public void StartHook();
    public bool Add(HotKeyModel hotKeyModel, Action<HotKeyModel> rallBack,bool initHotKey=true);
    public bool Del(HotKeyModel hotKeyModel);
    public bool Del(string uuid);
    public bool DeleteCompletely(string uuid);
    public bool RequestUserModify(string uuid);
    public bool Modify(HotKeyModel hotKeyModel);
    public HotKeyModel? GetByUuid(string uuid);

    public bool IsActive(string uuid);
    public IEnumerable<HotKeyModel> GetAllRegistered();

    public IEnumerable<HotKeyModel> AllRegistered => GetAllRegistered();
}