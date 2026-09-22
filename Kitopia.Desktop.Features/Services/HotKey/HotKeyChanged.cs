namespace Kitopia.Desktop.Features.Services.HotKey;

public enum HotKeyChangeKind
{
    Added,
    Updated,
    Removed
}

public sealed record HotKeyChanged(string Uuid, HotKeyChangeKind Kind);
