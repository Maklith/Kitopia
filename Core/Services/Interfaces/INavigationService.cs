namespace Core.Services.Interfaces;

public interface INavigationService
{
    string? CurrentPageRoute { get; }
    bool CanGoBack { get; }
    event Action<string>? PageNavigated;

    NavigationResult Navigate(string route, IReadOnlyDictionary<string, object?>? parameters = null);
    NavigationResult Open(string route, IReadOnlyDictionary<string, object?>? parameters = null);
    NavigationResult GoBack();
}

public sealed record NavigationResult(bool Success, string? ErrorCode = null, string? Message = null)
{
    public static NavigationResult Ok() => new(true);

    public static NavigationResult Fail(string errorCode, string? message = null)
    {
        return new NavigationResult(false, errorCode, message);
    }
}
