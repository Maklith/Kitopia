using System.Diagnostics;
using Kitopia.Desktop.Abstractions.Shell;

namespace Kitopia.Desktop.Platform.Linux;

public sealed class LinuxDesktopShell : IDesktopShell
{
    private static readonly string[] PrivilegeElevationCandidates =
    [
        "/usr/bin/pkexec",
        "/bin/pkexec",
        "/usr/bin/sudo",
        "/bin/sudo"
    ];

    private static readonly string[] DbusSendCandidates =
    [
        "/usr/bin/dbus-send",
        "/bin/dbus-send"
    ];

    private static readonly string[] XdgOpenCandidates =
    [
        "/usr/bin/xdg-open",
        "/bin/xdg-open"
    ];

    public void Open(string path, string? arguments = "", string? workingDirectory = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = Path.GetFullPath(workingDirectory);
        }

        Start(startInfo, $"open '{path}'");
    }

    public void RunAsAdmin(string path, string arguments = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var launcher = PrivilegeElevationCandidates.FirstOrDefault(File.Exists);
        if (launcher is null)
        {
            throw new InvalidOperationException(
                "Cannot elevate the process because neither pkexec nor sudo is installed.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = launcher,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(path);
        foreach (var argument in ParseArguments(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        Start(startInfo, $"run '{path}' with elevated privileges");
    }

    public void OpenFolderAndSelect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var dbusSend = DbusSendCandidates.FirstOrDefault(File.Exists);
        if (dbusSend is not null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dbusSend,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--session");
            startInfo.ArgumentList.Add("--dest=org.freedesktop.FileManager1");
            startInfo.ArgumentList.Add("--type=method_call");
            startInfo.ArgumentList.Add("/org/freedesktop/FileManager1");
            startInfo.ArgumentList.Add("org.freedesktop.FileManager1.ShowItems");
            startInfo.ArgumentList.Add($"array:string:{new Uri(fullPath).AbsoluteUri}");
            startInfo.ArgumentList.Add("string:");

            try
            {
                Start(startInfo, $"select '{fullPath}' in the file manager");
                return;
            }
            catch (InvalidOperationException)
            {
                // Fall back to opening the containing directory for desktops without FileManager1.
            }
        }

        var directory = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The path does not have a containing directory.", nameof(path));
        }

        var xdgOpen = XdgOpenCandidates.FirstOrDefault(File.Exists);
        if (xdgOpen is null)
        {
            Open(directory);
            return;
        }

        var fallback = new ProcessStartInfo
        {
            FileName = xdgOpen,
            UseShellExecute = false
        };
        fallback.ArgumentList.Add(directory);
        Start(fallback, $"open directory '{directory}'");
    }

    private static void Start(ProcessStartInfo startInfo, string operation)
    {
        try
        {
            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException($"Failed to {operation}: no process was created.");
            }
        }
        catch (Exception exception) when (exception is not ArgumentException)
        {
            throw new InvalidOperationException($"Failed to {operation}.", exception);
        }
    }

    private static IEnumerable<string> ParseArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            yield break;
        }

        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var quote = '\0';
        for (var index = 0; index < arguments.Length; index++)
        {
            var character = arguments[index];
            if ((character == '\'' || character == '"') && (!inQuotes || character == quote))
            {
                if (inQuotes)
                {
                    inQuotes = false;
                    quote = '\0';
                }
                else
                {
                    inQuotes = true;
                    quote = character;
                }

                continue;
            }

            if (character == '\\' && index + 1 < arguments.Length &&
                (arguments[index + 1] == '\\' || arguments[index + 1] == '\'' || arguments[index + 1] == '"'))
            {
                current.Append(arguments[++index]);
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (inQuotes)
        {
            throw new ArgumentException("Arguments contain an unmatched quote.", nameof(arguments));
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
