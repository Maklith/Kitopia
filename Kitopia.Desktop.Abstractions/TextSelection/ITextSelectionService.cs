using System.Threading;
using System.Threading.Tasks;

namespace Kitopia.Desktop.Abstractions.TextSelection;

public interface ITextSelectionService
{
    Task<TextSelectionSnapshot?> TryGetSelectionAsync(
        bool allowClipboardFallback,
        CancellationToken cancellationToken = default);
}
