using Avalonia.Controls;

namespace Kitopia.Desktop.Features.CustomScenario.Services;

public interface IScenarioUploadService
{
    Task ShowAsync(CustomScenario scenario, Window? owner);
}
