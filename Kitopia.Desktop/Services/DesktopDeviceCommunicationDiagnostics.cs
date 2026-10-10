using System;
using Kitopia.Feature.DeviceCommunication.Diagnostics;
using Serilog;
using Serilog.Core;

namespace Kitopia.Desktop.Services;

public sealed class DesktopDeviceCommunicationDiagnostics(ILogger logger) : IDeviceCommunicationDiagnostics
{
    public void Debug(string category, string message) =>
        logger.ForContext(Constants.SourceContextPropertyName, category).Debug("{Message:l}", message);

    public void Info(string category, string message) =>
        logger.ForContext(Constants.SourceContextPropertyName, category).Information("{Message:l}", message);

    public void Warning(string category, string message) =>
        logger.ForContext(Constants.SourceContextPropertyName, category).Warning("{Message:l}", message);

    public void Error(string category, string message, Exception? exception = null) =>
        logger.ForContext(Constants.SourceContextPropertyName, category).Error(exception, "{Message:l}", message);
}
