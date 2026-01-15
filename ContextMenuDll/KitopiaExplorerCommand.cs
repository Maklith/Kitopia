using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Vanara.PInvoke;

namespace ContextMenuDll;

[ComVisible(true)]
[Guid("60DA6757-67FE-B7CE-8195-3EFD30746B23")]
public class KitopiaExplorerCommand : Shell32.IExplorerCommand
{
    private const string ConfigFileName = "KitopiaContextMenu.json";
    private ContextMenuConfig? _config;
    private readonly string _dllDirectory;

    private void Log(string message)
    {
        Debug.WriteLine(message);
        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KitopiaContextMenu.log");
            System.IO.File.AppendAllText(logPath, $"{DateTime.Now}: {message}{Environment.NewLine}");
        }
        catch { }
    }

    public KitopiaExplorerCommand()
    {
        Log("Constructor called");
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        _dllDirectory = System.IO.Path.GetDirectoryName(assemblyLocation) ?? string.Empty;
        Log($"DLL Directory: {_dllDirectory}");
        LoadConfig();
    }

    private void LoadConfig()
    {
        try
        {
            // Look in 'configs' folder relative to DLL
            var configPath = System.IO.Path.Combine(_dllDirectory, "configs", ConfigFileName);
            Log($"Attempting to load config from: {configPath}");
            
            // Fallback: look in parent 'configs' (e.g. if DLL is in bin/Debug/netX.X)
            if (!System.IO.File.Exists(configPath))
            {
                Log("Config not found at primary path. Trying parents...");
                var parent = System.IO.Directory.GetParent(_dllDirectory)?.FullName;
                if (parent != null)
                {
                     var parentConfig = System.IO.Path.Combine(parent, "configs", ConfigFileName);
                     if (System.IO.File.Exists(parentConfig)) configPath = parentConfig;
                     else
                     {
                         // Try up to 3 levels up for development environments
                         for (int i = 0; i < 3; i++)
                         {
                             parent = System.IO.Directory.GetParent(parent!)?.FullName;
                             if (parent == null) break;
                             parentConfig = System.IO.Path.Combine(parent, "configs", ConfigFileName);
                             if (System.IO.File.Exists(parentConfig))
                             {
                                 configPath = parentConfig;
                                 break;
                             }
                         }
                     }
                }
            }

            if (System.IO.File.Exists(configPath))
            {
                Log($"Found config at: {configPath}");
                var json = System.IO.File.ReadAllText(configPath);
                _config = JsonSerializer.Deserialize<ContextMenuConfig>(json);
                Log($"Config loaded. Items count: {_config?.Items?.Count ?? 0}");
            }
            else
            {
                Log("Config file NOT found!");
            }
        }
        catch (Exception ex)
        {
            // Fail silently or log to debug
            Log($"Failed to load KitopiaContextMenu config: {ex}");
            Debug.WriteLine("Failed to load KitopiaContextMenu config.");
        }
    }

    public HRESULT GetTitle(Shell32.IShellItemArray? psiItemArray, out string? ppszName)
    {
        Log("GetTitle called");
        ppszName = "Kitopia"; // Default root title
        return HRESULT.S_OK;
    }

    public HRESULT GetIcon(Shell32.IShellItemArray? psiItemArray, out string? ppszIcon)
    {
        // Try to find icon in assets or use dll itself
        ppszIcon = System.IO.Path.Combine(_dllDirectory, "Assets", "icon.ico");
        if (!System.IO.File.Exists(ppszIcon))
        {
            // Fallback
             ppszIcon = System.Reflection.Assembly.GetExecutingAssembly().Location;
        }
        return HRESULT.S_OK;
    }

    public HRESULT GetToolTip(Shell32.IShellItemArray? psiItemArray, out string? ppszInfotip)
    {
        ppszInfotip = "Kitopia Context Menu";
        return HRESULT.S_OK;
    }

    public HRESULT GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = Guid.Parse("6B6E3182-5813-40D9-9238-1D7A76288863");
        return HRESULT.S_OK;
    }

    public HRESULT GetState(Shell32.IShellItemArray? psiItemArray, bool fOkToBeSlow, out Shell32.EXPCMDSTATE pCmdState)
    {
        var state = _config != null && _config.Items.Count > 0 
            ? Shell32.EXPCMDSTATE.ECS_ENABLED 
            : Shell32.EXPCMDSTATE.ECS_HIDDEN;
        
        Log($"GetState called. Result: {state}");
        pCmdState = state;
        return HRESULT.S_OK;
    }

    public HRESULT Invoke(Shell32.IShellItemArray? psiItemArray, System.Runtime.InteropServices.ComTypes.IBindCtx? pbc)
    {
        return HRESULT.S_OK;
    }

    public HRESULT GetFlags(out Shell32.EXPCMDFLAGS pFlags)
    {
        pFlags = Shell32.EXPCMDFLAGS.ECF_HASSUBCOMMANDS;
        return HRESULT.S_OK;
    }

    public HRESULT EnumSubCommands(out Shell32.IEnumExplorerCommand? ppEnum)
    {
        if (_config != null && _config.Items.Count > 0)
        {
            ppEnum = new ExplorerCommandEnumerator(_config.Items);
        }
        else
        {
            ppEnum = null;
        }
        return HRESULT.S_OK;
    }

    public HRESULT SetSite(object? pUnkSite)
    {
        return default;
    }

    public HRESULT GetSite(in Guid riid, out object? ppvSite)
    {
        ppvSite = null;
        return default;
    }
}

public class ExplorerCommandEnumerator : Shell32.IEnumExplorerCommand
{
    private readonly List<ContextMenuItem> _items;
    private int _current = 0;

    public ExplorerCommandEnumerator(List<ContextMenuItem> items)
    {
        _items = items;
    }

    public HRESULT Next(uint celt, Shell32.IExplorerCommand[]? pElements, out uint pceltFetched)
    {
        pceltFetched = 0;
        if (celt == 0 || pElements == null) return HRESULT.E_INVALIDARG;

        if (_current < _items.Count)
        {
            pElements[0] = new SubExplorerCommand(_items[_current]);
            _current++;
            pceltFetched = 1;
            return HRESULT.S_OK;
        }

        return HRESULT.S_FALSE;
    }

    public HRESULT Skip(uint celt)
    {
        _current += (int)celt;
        if (_current > _items.Count) _current = _items.Count;
        return HRESULT.S_OK;
    }

    public void Reset()
    {
        _current = 0;
       
    }

    public Shell32.IEnumExplorerCommand Clone()
    {
        return  new ExplorerCommandEnumerator(_items);;
    }
    
}

public class SubExplorerCommand : Shell32.IExplorerCommand
{
    private readonly ContextMenuItem _item;

    public SubExplorerCommand(ContextMenuItem item)
    {
        _item = item;
    }

    public HRESULT GetTitle(Shell32.IShellItemArray? psiItemArray, out string? ppszName)
    {
        ppszName = _item.Title;
        return HRESULT.S_OK;
    }

    public HRESULT GetIcon(Shell32.IShellItemArray? psiItemArray, out string? ppszIcon)
    {
        ppszIcon = _item.Icon;
        return HRESULT.S_OK;
    }

    public HRESULT GetToolTip(Shell32.IShellItemArray? psiItemArray, out string? ppszInfotip)
    {
        ppszInfotip = _item.Title;
        return HRESULT.S_OK;
    }

    public HRESULT GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = Guid.NewGuid(); // Should probably be stable, but generated for now
        return HRESULT.S_OK;
    }

    public HRESULT GetState(Shell32.IShellItemArray? psiItemArray, bool fOkToBeSlow, out Shell32.EXPCMDSTATE pCmdState)
    {
        pCmdState = Shell32.EXPCMDSTATE.ECS_ENABLED;
        return HRESULT.S_OK;
    }

    public HRESULT Invoke(Shell32.IShellItemArray? psiItemArray, System.Runtime.InteropServices.ComTypes.IBindCtx? pbc)
    {
        if (string.IsNullOrEmpty(_item.Command)) return HRESULT.S_OK;

        var paths = new List<string>();
        if (psiItemArray != null)
        {
             uint count=psiItemArray.GetCount();
             for (uint i = 0; i < count; i++)
             {
                 try
                 {
                     var shellItem=psiItemArray.GetItemAt(i);
                     if (shellItem != null)
                     {
                         // SIGDN_FILESYSPATH = 0x80058000
                         var path=shellItem.GetDisplayName(Shell32.SIGDN.SIGDN_FILESYSPATH );
                         if (!string.IsNullOrEmpty(path))
                         {
                             paths.Add(path);
                         }
                     }
                 }
                 catch { /* Ignore items we can't get path for */ }
             }
        }

        try
        {
            if (paths.Count > 0)
            {
                // Simple replacement logic
                string args = _item.Arguments;
                
                // If args is empty, just pass the file path
                if (string.IsNullOrEmpty(args))
                {
                    foreach(var path in paths)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = _item.Command,
                            Arguments = $"\"{path}\"", // Quote paths
                            UseShellExecute = true
                        });
                    }
                }
                else
                {
                     // If %1 or {0} is present, run per file
                     if (args.Contains("{0}") || args.Contains("%1"))
                     {
                         foreach(var path in paths)
                         {
                             string fileArgs = args.Replace("{0}", $"\"{path}\"").Replace("%1", $"\"{path}\"");
                             Process.Start(new ProcessStartInfo
                             {
                                 FileName = _item.Command,
                                 Arguments = fileArgs,
                                 UseShellExecute = true
                             });
                         }
                     }
                     else
                     {
                         // Run once
                         Process.Start(new ProcessStartInfo
                         {
                             FileName = _item.Command,
                             Arguments = args,
                             UseShellExecute = true
                         });
                     }
                }
            }
            else
            {
                // No files selected (background click?), just run command
                Process.Start(new ProcessStartInfo
                {
                    FileName = _item.Command,
                    Arguments = _item.Arguments,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error invoking command: {ex.Message}");
        }

        return HRESULT.S_OK;
    }

    public HRESULT GetFlags(out Shell32.EXPCMDFLAGS pFlags)
    {
        pFlags = Shell32.EXPCMDFLAGS.ECF_DEFAULT;
        if (_item.SubItems != null && _item.SubItems.Count > 0)
        {
             pFlags |= Shell32.EXPCMDFLAGS.ECF_HASSUBCOMMANDS;
        }
        return HRESULT.S_OK;
    }

    public HRESULT EnumSubCommands(out Shell32.IEnumExplorerCommand? ppEnum)
    {
        if (_item.SubItems != null && _item.SubItems.Count > 0)
        {
            ppEnum = new ExplorerCommandEnumerator(_item.SubItems);
        }
        else
        {
            ppEnum = null;
        }
        return HRESULT.S_OK;
    }
}
