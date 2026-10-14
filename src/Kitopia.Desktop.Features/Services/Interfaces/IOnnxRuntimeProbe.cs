namespace Kitopia.Desktop.Features.Services.Interfaces;

public interface IOnnxRuntimeProbe
{
    Task<bool?> CheckAsync(string device, CancellationToken cancellationToken = default, bool forceRefresh = false);
}
