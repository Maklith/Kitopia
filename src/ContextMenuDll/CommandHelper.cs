using System.Diagnostics;
using System.Text;

namespace ContextMenuDll;

public static class CommandHelper
{
    public static string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        
        // If absolute, return as is
        if (Path.IsPathRooted(path)) return path;
        
        return path;
    }

    public static void ExecuteCommand(ContextMenuItem item, List<string> paths, Action<string> logAction)
    {
        if (string.IsNullOrEmpty(item.Command)) return;
        
        try
        {
            if (paths.Count > 0)
            {
                // Simple replacement logic
                string args = item.Arguments ?? string.Empty;
                string command = ResolvePath(item.Command);

                // Case 1: Multi-file placeholder {all} or %*
                if (args.Contains("{all}") || args.Contains("%*"))
                {
                    var sb = new StringBuilder();
                    foreach (var p in paths) sb.Append($"\"{p}\" ");
                    string allPaths = sb.ToString().Trim();
                    
                    string finalArgs = args
                        .Replace("\"{all}\"", "{all}") // Remove existing quotes around placeholder
                        .Replace("\"%*\"", "%*")       // Remove existing quotes around placeholder
                        .Replace("{all}", allPaths)
                        .Replace("%*", allPaths);
                    
                    logAction($"Executing (Case 1): {command} Args: {finalArgs}");
                    
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = command,
                        Arguments = finalArgs,
                        UseShellExecute = true
                    });
                }
                // Case 2: Per-file placeholder {0} or %1
                else if (args.Contains("{0}") || args.Contains("%1"))
                {
                    foreach(var path in paths)
                    {
                        string quotedPath = $"\"{path}\"";
                        string fileArgs = args
                            .Replace("\"{0}\"", "{0}") // Remove existing quotes around placeholder
                            .Replace("\"%1\"", "%1")   // Remove existing quotes around placeholder
                            .Replace("{0}", quotedPath)
                            .Replace("%1", quotedPath);

                        logAction($"Executing (Case 2): {command} Args: {fileArgs}");
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = command,
                            Arguments = fileArgs,
                            UseShellExecute = true
                        });
                    }
                }
                    // Case 3: No placeholder - Append all paths (Run once)
                else
                {
                    var sb = new StringBuilder();
                    if (!string.IsNullOrEmpty(args)) sb.Append(args + " ");
                    foreach (var p in paths) sb.Append($"\"{p}\" ");
                     
                    var finalArgs = sb.ToString().Trim();
                    logAction($"Executing (Case 3): {command} Args: {finalArgs}");

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = command,
                        Arguments = finalArgs,
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                // No files selected (background click?), just run command
                string command = ResolvePath(item.Command);
                logAction($"Executing (No files): {command} Args: {item.Arguments}");
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = item.Arguments,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            logAction($"Error invoking command: {ex.Message}");
            Debug.WriteLine($"Error invoking command: {ex.Message}");
        }
    }
}
