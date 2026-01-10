namespace Core.Services.Interfaces;

public interface INavigationPageService
{
    bool Navigate(Type pageType);
    bool Navigate(string pageIdOrTargetTag);
}