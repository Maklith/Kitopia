using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Core.Services;

public static class LogManager
{
    public static readonly Logger Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.File($"{AppDomain.CurrentDomain.BaseDirectory}logs{Path.DirectorySeparatorChar}info.txt",
            rollingInterval: RollingInterval.Day, restrictedToMinimumLevel: LogEventLevel.Information,
            outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}][{SourceContext}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File($"{AppDomain.CurrentDomain.BaseDirectory}logs{Path.DirectorySeparatorChar}debug.txt",
            rollingInterval: RollingInterval.Day, restrictedToMinimumLevel: LogEventLevel.Debug,
            outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}][{SourceContext}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.Console(
            restrictedToMinimumLevel: LogEventLevel.Debug,
            outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}][{SourceContext}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();
}