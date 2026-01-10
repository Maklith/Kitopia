using PluginCore;

namespace Core.Services.Interfaces;

public interface ISearchItemChooseService
{
    void Choose(Action<SearchViewItem> action);
}