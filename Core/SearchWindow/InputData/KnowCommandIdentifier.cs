using Core.ViewModel;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.SearchWindow.InputData;

public class KnowCommandIdentifier : IInputDataIdentifier
{
    private string[] knownCommand =
    [
        "cmd", "powershell", "wsl", "bash", "ping", "ipconfig", "nslookup", "tracert", "netstat", "arp", "route",
        "telnet", "ftp", "ssh", "scp", "sftp", "rsync", "nmap", "nc", "curl", "wget", "git", "svn", "hg", "docker",
        "docker-compose", "kubectl", "helm", "minikube"
    ];
    public IEnumerable<ViewModel.InputData> IdentifyInputData(IInputDataAnalyzeTimeFlags analyzeTimeFlags,string? value)
    {
        foreach (var se in knownCommand)
            if ( !string.IsNullOrWhiteSpace(value) && value.StartsWith(se,StringComparison.OrdinalIgnoreCase))
            {
                yield return new ViewModel.InputData()
                {
                    InputType = InputType.命令,
                    Data = value
                };
            }
        yield break;
    }
}