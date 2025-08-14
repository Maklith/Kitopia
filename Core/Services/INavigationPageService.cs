namespace Core.SDKs.Services;

public interface INavigationPageService
{
    bool Navigate(Type pageType);
    bool Navigate(string pageIdOrTargetTag);
}