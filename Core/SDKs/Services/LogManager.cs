using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.AsyncFile;

namespace Core.SDKs.Services;

public class LogManager
{
    public static Logger Logger { get; set; }=new LoggerConfiguration()
        .WriteTo.AsyncFile($"{AppDomain.CurrentDomain.BaseDirectory}logs{Path.DirectorySeparatorChar}info.txt", rollingPolicyOptions:new RollingPolicyOptions()
        {
            RollOnStartup = true,
        } ,outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}][{SourceContext}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}][{SourceContext}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();
}