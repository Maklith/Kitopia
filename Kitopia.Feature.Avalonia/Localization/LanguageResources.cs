using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Kitopia.Feature.Localization;

namespace Kitopia.Feature.Avalonia.Localization;

public sealed class LanguageResources : ResourceDictionary
{
    public LanguageResources()
    {
        var dispatcher = Dispatcher.UIThread;
        UpdateResources();
        // Weak subscription allows application resources from headless sessions to be collected.
        var reference = new WeakReference<LanguageResources>(this);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (!reference.TryGetTarget(out var resources))
            {
                Lang.Current.PropertyChanged -= handler;
                return;
            }
            if (args.PropertyName == nameof(Lang.Keys))
            {
                if (dispatcher.CheckAccess()) resources.UpdateResources();
                else dispatcher.Post(resources.UpdateResources);
            }
        };
        Lang.Current.PropertyChanged += handler;
    }

    private void UpdateResources()
    {
        // SetItems raises one resource notification after all translations have been updated.
        var keys = Lang.Current.Keys.ToHashSet(StringComparer.Ordinal);
        SetItems(keys.Select(key => new KeyValuePair<object, object?>(key, Lang.Get(key)))
            .Concat(Keys.OfType<string>().Where(key => !keys.Contains(key))
                .Select(key => new KeyValuePair<object, object?>(key, null))));
    }
}
