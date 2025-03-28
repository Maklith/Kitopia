using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Core.SDKs.Services;

public class LogManager
{
    public static Logger Logger { get; set; }=new LoggerConfiguration()
        .WriteTo.File($"{AppDomain.CurrentDomain.BaseDirectory}logs{Path.DirectorySeparatorChar}info.txt", rollingInterval: RollingInterval.Day,restrictedToMinimumLevel: LogEventLevel.Information )
        .WriteTo.File($"{AppDomain.CurrentDomain.BaseDirectory}logs{Path.DirectorySeparatorChar}debug.txt", rollingInterval: RollingInterval.Day,restrictedToMinimumLevel: LogEventLevel.Debug )
        .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}][{SourceContext}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();
}