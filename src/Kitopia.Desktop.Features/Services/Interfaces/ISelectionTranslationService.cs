using System.Threading.Tasks;
using PluginCore;

namespace Kitopia.Desktop.Features.Services.Interfaces;

public interface ISelectionTranslationService
{
    void Start();
    void Refresh();
    Task TriggerManualAsync();
    Task TriggerAutomaticAsync(HotKeyModel hotKeyModel);
    void Stop();
}
