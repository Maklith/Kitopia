using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using ContextMenuDll.Interop;
using System.Runtime.InteropServices.Marshalling;

namespace ContextMenuDll;

#if !DEBUG
[GeneratedComClass]
#endif
[ComVisible(true)]
[Guid("60DA6757-67FE-B7CE-8195-3EFD30746B23")]
public partial class KitopiaExplorerCommand : IExplorerCommand, IShellExtInit, IContextMenu
{
    private const string AppName = "Kitopia";
    private const string ConfigFileName = "KitopiaContextMenu.json";
    private ContextMenuConfig? _config;
    private readonly string _dllDirectory;

    public KitopiaExplorerCommand()
    {
        Log("Constructor called");
        _dllDirectory = GetModulePath();
        Log($"DLL Directory: {_dllDirectory}");
        LoadConfig();
    }

    private static unsafe string GetModulePath()
    {
#if DEBUG
        // In Debug (Managed), GetModuleHandleEx might fail.
        // AppContext.BaseDirectory can be unreliable in some comhost scenarios.
        // Use Assembly.Location.
        var location = typeof(KitopiaExplorerCommand).Assembly.Location;
        if (!string.IsNullOrEmpty(location))
        {
            return System.IO.Path.GetDirectoryName(location) ?? string.Empty;
        }
        return AppContext.BaseDirectory;
#else
        try 
        {
            // Use GetModuleHandleEx with a pointer to this method to get the handle of the current DLL
            // Cast to void* then IntPtr
            if (GetModuleHandleEx(6, (IntPtr)(void*)(delegate* unmanaged<void>)&DummyMethod, out IntPtr hModule))
            {
                StringBuilder sb = new StringBuilder(1024);
                if (GetModuleFileName(hModule, sb, (uint)sb.Capacity) > 0)
                {
                    return System.IO.Path.GetDirectoryName(sb.ToString()) ?? string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get module path: {ex}");
        }
        
        // Fallback (might point to explorer.exe)
        return AppContext.BaseDirectory;
#endif
    }

    // Dummy method for address resolution
    [UnmanagedCallersOnly]
    private static void DummyMethod() { }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetModuleHandleEx(uint dwFlags, IntPtr lpModuleName, out IntPtr phModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, uint nSize);

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

    private void LoadConfig()
    {
        try
        {
            // 1. Try to load Settings from Package LocalState
            ContextMenuSettings? settings = null;
            
            try 
            {
                // Verify we are in a package context
                var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                var settingsPath = System.IO.Path.Combine(localFolder, "ContextMenuSettings.json");
                if (System.IO.File.Exists(settingsPath))
                {
                    var settingsJson = System.IO.File.ReadAllText(settingsPath);
                    settings = JsonSerializer.Deserialize(settingsJson, ContextMenuJsonContext.Default.ContextMenuSettings);
                    Log($"Loaded settings from {settingsPath}");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to access Package LocalState (not packaged?): {ex.Message}");
            }

            // 2. Config path is fixed in AppData
            var configPath = GetAppDataConfigPath();
            Log($"Attempting to load config from AppData: {configPath}");

            // 3. Load Items
            if (System.IO.File.Exists(configPath))
            {
                Log($"Found config at: {configPath}");
                var json = System.IO.File.ReadAllText(configPath);
                _config = JsonSerializer.Deserialize(json, ContextMenuJsonContext.Default.ContextMenuConfig);
                Log($"Config loaded. Items count: {_config?.Items?.Count ?? 0}");
            }
            else
            {
                Log("Config file NOT found!");
                _config = new ContextMenuConfig();
            }
            
            // 4. Apply Visibility Filter
            if (settings != null && _config != null && _config.Items.Count > 0)
            {
                var filteredItems = new List<ContextMenuItem>();
                foreach(var item in _config.Items)
                {
                    // If key exists and is false, skip. Default true.
                    if (settings.Visibility.TryGetValue(item.Title, out bool isVisible))
                    {
                        if (!isVisible) continue;
                    }
                    filteredItems.Add(item);
                }
                _config.Items = filteredItems;
                Log($"Filtered items count: {_config.Items.Count}");
            }
            
            // 5. Default Fallback if no items
            if (_config == null || _config.Items.Count == 0)
            {
                Log("No items found. Adding default configuration item.");
                _config ??= new ContextMenuConfig();
                
                // Try to find the manager app relative to this DLL (assuming inside MSIX)
                // Layout: PackageRoot/ContextMenuDll/ContextMenuDll.dll
                //         PackageRoot/ContextMenu.Avalonia/ContextMenu.Avalonia.exe
                var managerPath = System.IO.Path.Combine(_dllDirectory, "..", "ContextMenu.Avalonia", "ContextMenu.Avalonia.exe");
                managerPath = System.IO.Path.GetFullPath(managerPath);
                
                if (!System.IO.File.Exists(managerPath))
                {
                    // Fallback check for Kitopia.StoreCompanion
                    var altPath = System.IO.Path.Combine(_dllDirectory, "..", "Kitopia.StoreCompanion", "Kitopia.StoreCompanion.exe");
                    if (System.IO.File.Exists(altPath)) managerPath = System.IO.Path.GetFullPath(altPath);
                }

                _config.Items.Add(new ContextMenuItem 
                {
                    Title = "通过Kitopia伴侣程序配置",
                    Command = managerPath,
                    Icon = managerPath // Use app icon
                });
            }
        }
        catch (Exception ex)
        {
            // Fail silently or log to debug
            Log($"Failed to load KitopiaContextMenu config: {ex}");
            Debug.WriteLine("Failed to load KitopiaContextMenu config.");
        }
    }

    private static string GetAppDataConfigPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        var root = System.IO.Path.Combine(localAppData, AppName);
        System.IO.Directory.CreateDirectory(root);
        var configDirectory = System.IO.Path.Combine(root, "configs");
        System.IO.Directory.CreateDirectory(configDirectory);
        return System.IO.Path.Combine(configDirectory, ConfigFileName);
    }

    // Implement IExplorerCommand (not Shell32.IExplorerCommand)
    public void GetTitle(IShellItemArray? psiItemArray, out string ppszName)
    {
        LogStatic("GetTitle called");
        ppszName = "Kitopia"; // Default root title
    }

    public void GetIcon(IShellItemArray? psiItemArray, out string ppszIcon)
    {
        LogStatic("GetIcon called");
        // Try to find icon in assets or use dll itself
        ppszIcon = System.IO.Path.Combine(_dllDirectory, "Assets", "icon.ico");
        if (!System.IO.File.Exists(ppszIcon))
        {
            // Fallback
            ppszIcon = System.IO.Path.Combine(_dllDirectory, "ContextMenuDll.dll");
        }
    }

    public void GetToolTip(IShellItemArray? psiItemArray, out string ppszInfotip)
    {
        LogStatic("GetToolTip called");
        ppszInfotip = "Kitopia Context Menu";
    }

    public void GetCanonicalName(out Guid pguidCommandName)
    {
        LogStatic("GetCanonicalName called");
        pguidCommandName = Guid.Parse("6B6E3182-5813-40D9-9238-1D7A76288863");
    }

    public void GetState(IShellItemArray? psiItemArray, bool fOkToBeSlow, out uint pCmdState)
    {
        LogStatic("GetState called");
        var state = _config != null && _config.Items.Count > 0 
            ? EXPCMDSTATE.ECS_ENABLED 
            : EXPCMDSTATE.ECS_HIDDEN;
        
        pCmdState = (uint)state;
    }

    public void Invoke(IShellItemArray? psiItemArray, object? pbc)
    {
        LogStatic("Invoke called");
        // No action for root
    }

    public void GetFlags(out uint pFlags)
    {
        LogStatic("GetFlags called");
        pFlags = (uint)EXPCMDFLAGS.ECF_HASSUBCOMMANDS;
    }

    public void EnumSubCommands(out IEnumExplorerCommand? ppEnum)
    {
        LogStatic("EnumSubCommands called");
        if (_config != null && _config.Items.Count > 0)
        {
            ppEnum = new ExplorerCommandEnumerator(_config.Items);
        }
        else
        {
            ppEnum = null;
        }
    }

#if !DEBUG
    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    public static unsafe int DllGetClassObject(Guid* rclsid, Guid* riid, IntPtr* ppv)
    {
        Guid clsid = *rclsid;
        Guid iid = *riid;
        
        LogStatic($"DllGetClassObject called. CLSID: {clsid}, IID: {iid}");

        // 60DA6757-67FE-B7CE-8195-3EFD30746B23
        if (clsid == Guid.Parse("60DA6757-67FE-B7CE-8195-3EFD30746B23"))
        {
            try 
            {
                var factory = new ClassFactory();
                IntPtr pFactory = (IntPtr)ComInterfaceMarshaller<IClassFactory>.ConvertToUnmanaged(factory);
                
                int hr = Marshal.QueryInterface(pFactory, ref iid, out IntPtr pObj);
                if (hr == 0) // S_OK
                {
                    *ppv = pObj;
                    Marshal.Release(pFactory); // Release our ref, pObj has its own ref from QI
                    LogStatic("DllGetClassObject success");
                    return 0;
                }
                
                LogStatic($"DllGetClassObject QueryInterface failed: {hr}");
                Marshal.Release(pFactory);
                return -2147467262; // E_NOINTERFACE
            }
            catch (Exception ex)
            {
                LogStatic($"DllGetClassObject Exception: {ex}");
                return -2147467259; // E_FAIL
            }
        }
        
        LogStatic("DllGetClassObject Class Not Available");
        return -2147221231; // CLASS_E_CLASSNOTAVAILABLE
    }

    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    public static int DllCanUnloadNow()
    {
        return 0; // S_OK
    }
#endif

    private static void LogStatic(string message)
    {
        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KitopiaContextMenu_Entry.log");
            System.IO.File.AppendAllText(logPath, $"{DateTime.Now}: {message}{Environment.NewLine}");
        }
        catch { }
    }
}

#if !DEBUG
[GeneratedComClass]
public partial class ClassFactory : IClassFactory
{
    public unsafe void CreateInstance(object? pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        LogStatic($"CreateInstance called. IID: {riid}");
        ppvObject = IntPtr.Zero;

        if (pUnkOuter != null)
        {
            LogStatic("CreateInstance aggregation not supported");
            // CLASS_E_NOAGGREGATION = 0x80040110
            throw new COMException("Aggregation not supported", unchecked((int)0x80040110));
        }

        try
        {
            var obj = new KitopiaExplorerCommand();
            
            // Get the IExplorerCommand interface pointer for the object
            IntPtr pExplorerCommand = (IntPtr)ComInterfaceMarshaller<IExplorerCommand>.ConvertToUnmanaged(obj);
            
            try 
            {
                // Query for the requested interface (riid)
                int hr = Marshal.QueryInterface(pExplorerCommand, ref riid, out ppvObject);
                
                if (hr != 0)
                {
                    LogStatic($"CreateInstance QueryInterface failed: {hr}");
                    Marshal.ThrowExceptionForHR(hr);
                }
                LogStatic("CreateInstance success");
            }
            finally
            {
                Marshal.Release(pExplorerCommand);
            }
        }
        catch (Exception ex)
        {
            LogStatic($"CreateInstance Exception: {ex}");
            throw;
        }
    }

    public void LockServer(bool fLock)
    {
        LogStatic($"LockServer: {fLock}");
    }

    private static void LogStatic(string message)
    {
        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KitopiaContextMenu_Entry.log");
            System.IO.File.AppendAllText(logPath, $"{DateTime.Now}: {message}{Environment.NewLine}");
        }
        catch { }
    }
}
#endif


#if !DEBUG
[GeneratedComClass]
#endif
[ComVisible(true)]
[Guid("1F2E3D4C-5B6A-7890-E1F2-A3B4C5D6E7F8")] // Random GUID
public partial class ExplorerCommandEnumerator : IEnumExplorerCommand
{
    private readonly List<ContextMenuItem> _items;
    private int _current = 0;

    public ExplorerCommandEnumerator(List<ContextMenuItem> items)
    {
        _items = items;
    }

    public unsafe int Next(uint celt, IntPtr pElements, out uint pceltFetched)
    {
        pceltFetched = 0;
        if (celt == 0) return 0; // S_OK
        if (pElements == IntPtr.Zero) return -2147024809; // E_INVALIDARG

        if (_current < _items.Count)
        {
            // Marshal array manually
            IntPtr* ptr = (IntPtr*)pElements;
            
            // Create object
            var subCmd = new SubExplorerCommand(_items[_current]);
            
            // Convert to unmanaged interface pointer using ComInterfaceMarshaller
            // This is the AOT-safe way to get the COM pointer for a [GeneratedComClass] object
            // ConvertToUnmanaged returns void*, so we cast to IntPtr
#if DEBUG
            IntPtr pInterface = Marshal.GetComInterfaceForObject(subCmd, typeof(IExplorerCommand));
#else
            IntPtr pInterface = (IntPtr)ComInterfaceMarshaller<IExplorerCommand>.ConvertToUnmanaged(subCmd);
#endif
            
            ptr[0] = pInterface;
            
            _current++;
            pceltFetched = 1;
            
            return (celt == 1) ? 0 : 1; // S_OK if satisfied, S_FALSE otherwise
        }
        
        return 1; // S_FALSE
    }

    public int Skip(uint celt)
    {
        var remaining = _items.Count - _current;
        if (celt <= remaining)
        {
            _current += (int)celt;
            return 0; // S_OK
        }
        else
        {
            _current = _items.Count;
            return 1; // S_FALSE
        }
    }

    public void Reset()
    {
        _current = 0;
    }

    public void Clone(out IEnumExplorerCommand ppEnum)
    {
        ppEnum = new ExplorerCommandEnumerator(_items);
    }
}

#if !DEBUG
[GeneratedComClass]
#endif
[ComVisible(true)]
[Guid("4a132515-3843-4a84-9092-23c2184084f7")] // Arbitrary GUID for internal class
public partial class SubExplorerCommand : IExplorerCommand
{
    private readonly ContextMenuItem _item;

    public SubExplorerCommand(ContextMenuItem item)
    {
        _item = item;
    }

    private void LogStatic(string message)
    {
        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KitopiaContextMenu_Entry.log");
            System.IO.File.AppendAllText(logPath, $"{DateTime.Now}: {message}{Environment.NewLine}");
        }
        catch { }
    }

    public void GetTitle(IShellItemArray? psiItemArray, out string ppszName)
    {
        LogStatic("SubCommand GetTitle called");
        ppszName = _item.Title;
    }

    public void GetIcon(IShellItemArray? psiItemArray, out string ppszIcon)
    {
        LogStatic("SubCommand GetIcon called");
        ppszIcon = CommandHelper.ResolvePath(_item.Icon);
    }

    public void GetToolTip(IShellItemArray? psiItemArray, out string ppszInfotip)
    {
        LogStatic("SubCommand GetToolTip called");
        ppszInfotip = _item.Title;
    }

    public void GetCanonicalName(out Guid pguidCommandName)
    {
        LogStatic("SubCommand GetCanonicalName called");
        pguidCommandName = Guid.NewGuid(); // Should probably be stable, but generated for now
    }

    public void GetState(IShellItemArray? psiItemArray, bool fOkToBeSlow, out uint pCmdState)
    {
        LogStatic("SubCommand GetState called");
        pCmdState = (uint)EXPCMDSTATE.ECS_ENABLED;
    }

    public void Invoke(IShellItemArray? psiItemArray, object? pbc)
    {
        LogStatic("SubCommand Invoke called");
        
        var paths = new List<string>();
        if (psiItemArray != null)
        {
            try
            {
                psiItemArray.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        psiItemArray.GetItemAt(i, out IShellItem shellItem);
                        if (shellItem != null)
                        {
                            // SIGDN_FILESYSPATH = 0x80058000
                            shellItem.GetDisplayName((uint)SIGDN.SIGDN_FILESYSPATH, out IntPtr ppszName);
                            var path = Marshal.PtrToStringUni(ppszName);
                            Marshal.FreeCoTaskMem(ppszName);
                            
                            LogStatic($"Item path retrieved: {path}");

                            if (!string.IsNullOrEmpty(path))
                            {
                                paths.Add(path);
                            }
                        }
                    }
                    catch { /* Ignore items we can't get path for */ }
                }
            }
            catch { }
        }

        CommandHelper.ExecuteCommand(_item, paths, LogStatic);
    }

    public void GetFlags(out uint pFlags)
    {
        pFlags = (uint)EXPCMDFLAGS.ECF_DEFAULT;
        if (_item.SubItems != null && _item.SubItems.Count > 0)
        {
            pFlags |= (uint)EXPCMDFLAGS.ECF_HASSUBCOMMANDS;
        }
    }

    public void EnumSubCommands(out IEnumExplorerCommand? ppEnum)
    {
        if (_item.SubItems != null && _item.SubItems.Count > 0)
        {
            ppEnum = new ExplorerCommandEnumerator(_item.SubItems);
        }
        else
        {
            ppEnum = null;
        }
    }
}
