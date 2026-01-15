using Core.ViewModel;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.UI.SearchWindow.InputData;

public class KnowCommandIdentifier : IInputDataIdentifier
{
    private string[] knownCommand =
    [
        "cmd", "powershell", "wsl", "bash", "ping", "ipconfig", "nslookup", "tracert", "netstat", "arp", "route",
        "telnet", "ftp", "ssh", "scp", "sftp", "rsync", "nmap", "nc", "curl", "wget", "git", "svn", "hg", "docker",
        "docker-compose", "kubectl", "helm", "minikube"
    ];

    public IEnumerable<PluginCore.SearchWindow.InputData.InputData> IdentifyInputData(
        InputDataAnalyzeTimeFlags analyzeTimeFlags, string? value)
    {
        foreach (var se in knownCommand)
            if (!string.IsNullOrWhiteSpace(value) && value.StartsWith(se, StringComparison.OrdinalIgnoreCase))
                yield return new PluginCore.SearchWindow.InputData.InputData
                {
                    InputType = InputType.命令,
                    Data = value
                };
        yield break;
    }
}