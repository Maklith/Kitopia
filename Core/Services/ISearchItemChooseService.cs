using PluginCore;

namespace Core.SDKs.Services;

public interface ISearchItemChooseService
{
    void Choose(Action<SearchViewItem> action);
}