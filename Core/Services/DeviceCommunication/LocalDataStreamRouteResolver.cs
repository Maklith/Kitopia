namespace Core.Services.DeviceCommunication;

internal static class LocalDataStreamRouteResolver
{
    private const string RouteFile = "file";
    private const string RouteMessage = "message";
    private const string RouteCommand = "command";

    private const string FileCommandBegin = "begin";
    private const string FileCommandEnd = "end";
    private const string FileCommandCancel = "cancel";
    private const string MessageCommandPublish = "publish";

    private const string LegacyStartFileCommand = "start_file";
    private const string LegacyFinishFileCommand = "finish_file";
    private const string LegacyCancelFileCommand = "cancel_file";

    public static Guid ResolveChannelId(Guid frameChannelId, string? channelIdText)
    {
        if (frameChannelId != Guid.Empty)
        {
            return frameChannelId;
        }

        return Guid.TryParse(channelIdText, out var channelId) ? channelId : Guid.Empty;
    }

    public static bool IsTerminalEnvelope(string route, string? command)
    {
        if (!string.Equals(route, RouteFile, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = NormalizeFileCommand(command);
        return normalized is FileCommandEnd or FileCommandCancel;
    }

    public static string ResolveRoute(string? route, string? command)
    {
        if (!string.IsNullOrWhiteSpace(route))
        {
            return route.Trim().ToLowerInvariant();
        }

        var normalized = command?.Trim().ToLowerInvariant();
        return normalized switch
        {
            FileCommandBegin or FileCommandEnd or FileCommandCancel or LegacyStartFileCommand or
                LegacyFinishFileCommand or LegacyCancelFileCommand => RouteFile,
            MessageCommandPublish or RouteMessage => RouteMessage,
            _ => RouteCommand
        };
    }

    public static string NormalizeFileCommand(string? command)
    {
        var normalized = command?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            LegacyStartFileCommand => FileCommandBegin,
            LegacyFinishFileCommand => FileCommandEnd,
            LegacyCancelFileCommand => FileCommandCancel,
            _ => normalized
        };
    }
}
