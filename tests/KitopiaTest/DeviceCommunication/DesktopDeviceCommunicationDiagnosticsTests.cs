using Kitopia.Desktop.Services;
using Kitopia.Feature.DeviceCommunication.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
[DoNotParallelize]
public sealed class DesktopDeviceCommunicationDiagnosticsTests
{
    [TestMethod]
    public void Diagnostics_DesktopLogger_PreservesLevelsSourceMessagesAndExceptions()
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        var previousDiagnostics = DeviceCommunicationDiagnostics.Current;
        try
        {
            DeviceCommunicationDiagnostics.Current = new DesktopDeviceCommunicationDiagnostics(logger);
            const string message = "Received discovery payload: {peer}";
            var exception = new InvalidOperationException("Discovery socket failed.");

            DeviceCommunicationDiagnostics.Debug("DiscoveryService", message);
            DeviceCommunicationDiagnostics.Info("DiscoveryService", message);
            DeviceCommunicationDiagnostics.Warning("DiscoveryService", message);
            DeviceCommunicationDiagnostics.Error("DiscoveryService", message, exception);
            DeviceCommunicationDiagnostics.Error("TcpLocalDataListener", message);

            CollectionAssert.AreEqual(new[]
            {
                LogEventLevel.Debug, LogEventLevel.Information, LogEventLevel.Warning, LogEventLevel.Error, LogEventLevel.Error
            }, sink.Events.Select(entry => entry.Level).ToArray());
            for (var index = 0; index < sink.Events.Count; index++)
            {
                var entry = sink.Events[index];
                Assert.AreEqual(message, entry.RenderMessage());
                Assert.AreEqual(index == 4 ? "TcpLocalDataListener" : "DiscoveryService",
                    ((ScalarValue)entry.Properties[Constants.SourceContextPropertyName]).Value);
                Assert.AreSame(index == 3 ? exception : null, entry.Exception);
            }
        }
        finally
        {
            DeviceCommunicationDiagnostics.Current = previousDiagnostics;
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
