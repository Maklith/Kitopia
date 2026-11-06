using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Kitopia.Desktop.Platform.Windows;

public partial class ApplicationService
{
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly SemaphoreSlim _startupRegistrationGate = new(1, 1);
    private bool _startupSettingsRequested;

    public async Task<bool> ChangeAutoStartAsync(bool autoStart, bool preferEarlyStartup, bool allowElevation)
    {
        await _startupRegistrationGate.WaitAsync().ConfigureAwait(false);
        string? originalCommand = null;
        string? originalTask = null;
        string? taskName = null;
        var commandChanged = false;
        var taskChanged = false;
        try
        {
            // An explicit Settings request takes precedence over a queued, stale startup reconciliation.
            if (allowElevation) _startupSettingsRequested = true;
            else if (_startupSettingsRequested) return true;
            var userId = GetStartupUserId();
            taskName = "Kitopia.Startup." + userId;
            originalCommand = ReadStartupCommand();
            originalTask = await ReadStartupTaskAsync(taskName).ConfigureAwait(false);
            if (autoStart && preferEarlyStartup)
            {
                var executable = ResolveExecutablePath();
                if (!File.Exists(executable)) return false;
                var desiredTask = CreateStartupTaskXml(userId, executable);
                if (originalTask is null || !StartupTaskMatches(originalTask, desiredTask))
                {
                    if (!await WriteStartupTaskAsync(taskName, desiredTask, allowElevation).ConfigureAwait(false)) return false;
                    taskChanged = true;
                }
                if (originalCommand is not null)
                {
                    commandChanged = true;
                    WriteStartupCommand(null);
                }
            }
            else
            {
                var executable = ResolveExecutablePath();
                if (autoStart && !File.Exists(executable)) return false;
                var desiredCommand = autoStart ? $"\"{executable}\"" : null;
                if (originalCommand != desiredCommand)
                {
                    commandChanged = true;
                    WriteStartupCommand(desiredCommand);
                }
                if (originalTask is not null)
                {
                    if (!await WriteStartupTaskAsync(taskName, null, allowElevation).ConfigureAwait(false))
                        throw new InvalidOperationException("Could not remove the previous startup task.");
                    taskChanged = true;
                }
            }
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to update startup registration");
            try
            {
                if (commandChanged) WriteStartupCommand(originalCommand);
            }
            catch (Exception rollbackException)
            {
                Logger.Error(rollbackException, "Could not restore the previous startup Run entry");
            }
            try
            {
                if (taskChanged && taskName is not null &&
                    !await WriteStartupTaskAsync(taskName, originalTask, allowElevation).ConfigureAwait(false))
                    Logger.Error("Could not restore startup task {TaskName}", taskName);
            }
            catch (Exception rollbackException)
            {
                Logger.Error(rollbackException, "Could not restore the previous startup task");
            }
            return false;
        }
        finally
        {
            _startupRegistrationGate.Release();
        }
    }

    protected virtual string GetStartupUserId()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw new InvalidOperationException("The current user has no SID.");
    }

    protected virtual string? ReadStartupCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath);
        return key?.GetValue("Kitopia") as string;
    }

    protected virtual void WriteStartupCommand(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath);
        if (command is null) key.DeleteValue("Kitopia", throwOnMissingValue: false);
        else key.SetValue("Kitopia", command);
    }

    protected virtual Task<string?> ReadStartupTaskAsync(string taskName) => Task.Run(() =>
    {
        // Missing tasks are normal; GetTask would throw before the debugger can reach a catch filter.
        dynamic? scheduler = null;
        dynamic? folder = null;
        dynamic? tasks = null;
        try
        {
            scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!);
            scheduler!.Connect();
            folder = scheduler.GetFolder(@"\");
            tasks = folder.GetTasks(1); // TASK_ENUM_HIDDEN includes registrations hidden in Task Scheduler.
            var count = (int)tasks.Count;
            for (var index = 1; index <= count; index++)
            {
                dynamic task = tasks[index];
                try
                {
                    if (string.Equals((string)task.Name, taskName, StringComparison.OrdinalIgnoreCase))
                        return (string?)task.Xml;
                }
                finally
                {
                    Marshal.FinalReleaseComObject(task);
                }
            }
            return null;
        }
        finally
        {
            if (tasks is not null) Marshal.FinalReleaseComObject(tasks);
            if (folder is not null) Marshal.FinalReleaseComObject(folder);
            if (scheduler is not null) Marshal.FinalReleaseComObject(scheduler);
        }
    });

    protected virtual async Task<bool> WriteStartupTaskAsync(string taskName, string? xml, bool allowElevation)
    {
        string? temporaryFile = null;
        try
        {
            if (xml is not null)
            {
                temporaryFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(temporaryFile, xml, Encoding.Unicode).ConfigureAwait(false);
            }
            async Task<int> RunAsync(bool elevated)
            {
                var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
                {
                    UseShellExecute = elevated,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                if (elevated) startInfo.Verb = "runas";
                startInfo.ArgumentList.Add(xml is null ? "/Delete" : "/Create");
                startInfo.ArgumentList.Add("/TN");
                startInfo.ArgumentList.Add(taskName);
                if (temporaryFile is not null)
                {
                    startInfo.ArgumentList.Add("/XML");
                    startInfo.ArgumentList.Add(temporaryFile);
                }
                startInfo.ArgumentList.Add("/F");
                using var process = Process.Start(startInfo);
                if (process is null) return -1;
                await process.WaitForExitAsync().ConfigureAwait(false);
                return process.ExitCode;
            }
            var exitCode = await RunAsync(false).ConfigureAwait(false);
            // schtasks can report access denied as the generic exit code 1; do not parse localized output.
            if (allowElevation && exitCode != 0)
                exitCode = await RunAsync(true).ConfigureAwait(false);
            return exitCode == 0;
        }
        catch (Win32Exception exception)
        {
            Logger.Warning(exception, "Startup task registration failed or elevation was cancelled");
            return false;
        }
        finally
        {
            if (temporaryFile is not null)
            {
                try { File.Delete(temporaryFile); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Logger.Warning(exception, "Could not delete startup task XML {Path}", temporaryFile);
                }
            }
        }
    }

    internal static string CreateStartupTaskXml(string userId, string executable)
    {
        var ns = TaskNamespace;
        return new XDocument(new XDeclaration("1.0", "UTF-16", null),
            new XElement(ns + "Task", new XAttribute("version", "1.2"),
                new XElement(ns + "Triggers", new XElement(ns + "LogonTrigger",
                    new XElement(ns + "Enabled", true), new XElement(ns + "UserId", userId))),
                new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "User"),
                    new XElement(ns + "UserId", userId), new XElement(ns + "LogonType", "InteractiveToken"),
                    new XElement(ns + "RunLevel", "LeastPrivilege"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", false),
                    new XElement(ns + "StopIfGoingOnBatteries", false),
                    new XElement(ns + "StartWhenAvailable", true),
                    new XElement(ns + "Enabled", true),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Priority", 6)),
                new XElement(ns + "Actions", new XAttribute("Context", "User"),
                    new XElement(ns + "Exec", new XElement(ns + "Command", executable),
                        new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(executable)))))).ToString();
    }

    internal static bool StartupTaskMatches(string actualXml, string desiredXml)
    {
        var actual = XDocument.Parse(actualXml).Root!;
        var desired = XDocument.Parse(desiredXml).Root!;
        foreach (var leaf in desired.Descendants().Where(element => !element.HasElements))
        {
            var path = leaf.AncestorsAndSelf().Reverse().Skip(1).Select(element => element.Name);
            var matching = path.Aggregate((XElement?)actual, (element, name) => element?.Element(name));
            if (matching?.Value != leaf.Value) return false;
        }
        // A task with a delay or extra trigger/action is not our immediate single-launch registration.
        return actual.Descendants(TaskNamespace + "Delay").All(delay => delay.Value == "PT0S") &&
               actual.Element(TaskNamespace + "Triggers")?.Elements().Count() == 1 &&
               actual.Element(TaskNamespace + "Actions")?.Elements().Count() == 1 &&
               !actual.Descendants(TaskNamespace + "Arguments").Any(argument => argument.Value.Length > 0);
    }
}
