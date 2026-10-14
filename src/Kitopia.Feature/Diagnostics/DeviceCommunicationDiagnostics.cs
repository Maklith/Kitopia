namespace Kitopia.Feature.DeviceCommunication.Diagnostics;

public static class DeviceCommunicationDiagnostics
{
    private static IDeviceCommunicationDiagnostics _current = NoopDeviceCommunicationDiagnostics.Instance;

    public static IDeviceCommunicationDiagnostics Current
    {
        get => _current;
        set => _current = value ?? NoopDeviceCommunicationDiagnostics.Instance;
    }

    public static void Debug(string category, string message)
    {
        Current.Debug(category, message);
    }

    public static void Info(string category, string message)
    {
        Current.Info(category, message);
    }

    public static void Warning(string category, string message)
    {
        Current.Warning(category, message);
    }

    public static void Error(string category, string message, Exception? exception = null)
    {
        Current.Error(category, message, exception);
    }

    private sealed class NoopDeviceCommunicationDiagnostics : IDeviceCommunicationDiagnostics
    {
        public static NoopDeviceCommunicationDiagnostics Instance { get; } = new();

        public void Debug(string category, string message)
        {
            _ = category;
            _ = message;
        }

        public void Info(string category, string message)
        {
            _ = category;
            _ = message;
        }

        public void Warning(string category, string message)
        {
            _ = category;
            _ = message;
        }

        public void Error(string category, string message, Exception? exception = null)
        {
            _ = category;
            _ = message;
            _ = exception;
        }
    }
}
