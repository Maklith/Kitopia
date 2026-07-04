using Android.Util;
using Kitopia.DeviceCommunication.Diagnostics;

namespace Kitopia.Mobile;

public sealed class AndroidDeviceCommunicationDiagnostics : IDeviceCommunicationDiagnostics
{
    private const string Tag = "KitopiaDiscovery";

    public void Debug(string category, string message)
    {
        Log.Debug(Tag, FormatMessage(category, message));
    }

    public void Info(string category, string message)
    {
        Log.Info(Tag, FormatMessage(category, message));
    }

    public void Warning(string category, string message)
    {
        Log.Warn(Tag, FormatMessage(category, message));
    }

    public void Error(string category, string message, Exception? exception = null)
    {
        if (exception is null)
        {
            Log.Error(Tag, FormatMessage(category, message));
            return;
        }

        Log.Error(Tag, $"{FormatMessage(category, message)} Exception={exception}");
    }

    private static string FormatMessage(string category, string message)
    {
        return $"[{category}] {message}";
    }
}
