using PluginCore;

namespace Kitopia.Desktop.Features.Services.Interfaces;

public interface ISearchItemChooseService
{
    void Choose(Action<SearchViewItem> action);
}