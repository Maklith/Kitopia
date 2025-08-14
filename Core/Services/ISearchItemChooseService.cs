using PluginCore;

namespace Core.Services;

public interface ISearchItemChooseService
{
    void Choose(Action<SearchViewItem> action);
}