using System.Runtime.InteropServices;
using System.Text;
using ContextMenuDll.Interop;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ContextMenuDll;

public partial class KitopiaExplorerCommand
{
    private List<string> _legacySelectedPaths = new();
    private List<ContextMenuItem> _cmdIdMap = new();
    private readonly Dictionary<string, SafeMenuBitmap> _legacyMenuBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private uint _legacyMenuBackgroundColor;

    public int Initialize(IntPtr pidlFolder, IntPtr pdtobj, IntPtr hKeyProgID)
    {
        Log("IShellExtInit.Initialize called");
        _legacySelectedPaths.Clear();
        
        if (pdtobj == IntPtr.Zero) return 0;

        try
        {
             object? obj = null;
             try { obj = Marshal.GetObjectForIUnknown(pdtobj); } catch {}

             if (obj is System.Runtime.InteropServices.ComTypes.IDataObject dataObject)
             {
                 var fmt = new System.Runtime.InteropServices.ComTypes.FORMATETC
                 {
                     cfFormat = 15, // CF_HDROP
                     ptd = IntPtr.Zero,
                     dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                     lindex = -1,
                     tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
                 };
                 
                 var stg = new System.Runtime.InteropServices.ComTypes.STGMEDIUM();
                 try
                 {
                     dataObject.GetData(ref fmt, out stg);
                     
                     if (stg.unionmember != IntPtr.Zero)
                     {
                         IntPtr hDrop = stg.unionmember;
                         uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                         for (uint i = 0; i < count; i++)
                         {
                             uint len = DragQueryFile(hDrop, i, null, 0);
                             var sb = new StringBuilder((int)len + 1);
                             DragQueryFile(hDrop, i, sb, len + 1);
                             _legacySelectedPaths.Add(sb.ToString());
                         }
                     }
                 }
                 finally
                 {
                     if (stg.unionmember != IntPtr.Zero)
                        ReleaseStgMedium(ref stg);
                 }
             }
        }
        catch (Exception ex)
        {
            Log($"IShellExtInit Error: {ex}");
        }
        
        return 0; // S_OK
    }

    public int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags)
    {
        // Log($"QueryContextMenu called. Flags: {uFlags}");
        
        if (((uint)CMF.CMF_DEFAULTONLY & uFlags) != 0)
        {
            return 0; 
        }

        _cmdIdMap.Clear();
        
        if (_config == null || _config.Items.Count == 0) return 0;
        using (var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
        {
            _legacyMenuBackgroundColor = GetLegacyMenuBackgroundColor(settings?.GetValue("AppsUseLightTheme") is 0);
        }

        // Create a submenu
        IntPtr hSubMenu = CreatePopupMenu();
        
        int idOffset = 0;
        
        foreach (var item in _config.Items)
        {
            AddLegacyMenuItem(hSubMenu, item, ref idOffset, idCmdFirst);
        }

        // Add the root item "Kitopia"
        var mii = new MENUITEMINFO();
        mii.cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>();
        mii.fMask = MIIM_STRING | MIIM_SUBMENU | MIIM_ID | MIIM_BITMAP;
        
        mii.wID = idCmdFirst + (uint)idOffset;
        mii.hSubMenu = hSubMenu;
        mii.dwTypeData = "Kitopia";
        GetIcon(null, out var iconPath);
        mii.hbmpItem = GetLegacyMenuBitmap(iconPath);
        
        InsertMenuItem(hMenu, indexMenu, true, ref mii);
        
        // Add a dummy entry for the root item so we track the ID count
        _cmdIdMap.Add(new ContextMenuItem { Title = "Root" });
        idOffset++;
        
        return idOffset;
    }

    private void AddLegacyMenuItem(IntPtr hMenu, ContextMenuItem item, ref int idOffset, uint idCmdFirst)
    {
        var mii = new MENUITEMINFO
        {
            cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
            fMask = MIIM_STRING | MIIM_ID | MIIM_BITMAP,
            dwTypeData = item.Title,
            hbmpItem = GetLegacyMenuBitmap(CommandHelper.ResolvePath(item.Icon))
        };

        if (item.SubItems != null && item.SubItems.Count > 0)
        {
             IntPtr hSub = CreatePopupMenu();
             foreach(var sub in item.SubItems)
             {
                 AddLegacyMenuItem(hSub, sub, ref idOffset, idCmdFirst);
             }
             
             mii.fMask |= MIIM_SUBMENU;
             mii.hSubMenu = hSub;
        }

        mii.wID = idCmdFirst + (uint)idOffset;
        InsertMenuItem(hMenu, unchecked((uint)-1), true, ref mii); // Append
        _cmdIdMap.Add(item);
        idOffset++;
    }

    private unsafe IntPtr GetLegacyMenuBitmap(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return IntPtr.Zero;
        iconPath = Environment.ExpandEnvironmentVariables(iconPath);
        var cacheKey = $"{_legacyMenuBackgroundColor:X6}:{iconPath}";
        if (_legacyMenuBitmaps.TryGetValue(cacheKey, out var cached)) return cached.DangerousGetHandle();

        var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"); // IShellItemImageFactory
        if (SHCreateItemFromParsingName(iconPath, IntPtr.Zero, in iid, out var factory) < 0) return IntPtr.Zero;

        try
        {
            var size = new System.Drawing.Size(GetSystemMetrics(71), GetSystemMetrics(72)); // SM_CXMENUCHECK / SM_CYMENUCHECK
            IntPtr bitmap = IntPtr.Zero;
            // Call IShellItemImageFactory.GetImage directly to keep this native boundary AOT-compatible.
            var getImage = (delegate* unmanaged[Stdcall]<IntPtr, System.Drawing.Size, uint, IntPtr*, int>)(*(IntPtr**)factory)[3];
            if (getImage(factory, size, 0x4, &bitmap) < 0 || bitmap == IntPtr.Zero) return IntPtr.Zero; // SIIGBF_ICONONLY

            var ownedBitmap = new SafeMenuBitmap(bitmap);
            if (GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), out var bitmapInfo) == 0 ||
                bitmapInfo.BitsPixel != 32 || bitmapInfo.Bits == IntPtr.Zero)
            {
                ownedBitmap.Dispose();
                return IntPtr.Zero;
            }

            // The packaged IContextMenu proxy drops HBITMAP alpha. Composite before crossing that boundary.
            var pixels = new Span<byte>((void*)bitmapInfo.Bits, checked(bitmapInfo.WidthBytes * bitmapInfo.Height));
            ReadOnlySpan<byte> background =
            [
                (byte)(_legacyMenuBackgroundColor >> 16),
                (byte)(_legacyMenuBackgroundColor >> 8),
                (byte)_legacyMenuBackgroundColor
            ];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var alpha = pixels[i + 3];
                for (var channel = 0; channel < 3; channel++)
                {
                    pixels[i + channel] = (byte)((pixels[i + channel] * alpha + background[channel] * (255 - alpha) + 127) / 255);
                }
                pixels[i + 3] = 255;
            }

            _legacyMenuBitmaps.Add(cacheKey, ownedBitmap);
            return ownedBitmap.DangerousGetHandle();
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    private static uint GetLegacyMenuBackgroundColor(bool darkMode)
    {
        var background = GetSysColor(4); // COLOR_MENU, including high-contrast colors.
        var contrast = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
        if (!SystemParametersInfo(0x42, contrast.Size, ref contrast, 0) || (contrast.Flags & 1) != 0) return background;

        var theme = OpenThemeData(IntPtr.Zero, darkMode ? "DarkMode::Menu" : "Menu");
        if (theme == IntPtr.Zero) return background;
        var dc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            if (dc == IntPtr.Zero) return background;
            using var sample = new SafeMenuBitmap(CreateBitmap(1, 1, 1, 32, IntPtr.Zero));
            if (sample.IsInvalid) return background;
            var previous = SelectObject(dc, sample.DangerousGetHandle());
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) return background;
            try
            {
                // MENU_POPUPBACKGROUND is image-backed; querying TMT_FILLCOLOR is not supported.
                var rect = new NativeRect { Right = 1, Bottom = 1 };
                if (DrawThemeBackground(theme, dc, 9, 0, in rect, IntPtr.Zero) != 0) return background;
                var color = GetPixel(dc, 0, 0);
                return color == uint.MaxValue ? background : color;
            }
            finally
            {
                SelectObject(dc, previous);
            }
        }
        finally
        {
            if (dc != IntPtr.Zero) DeleteDC(dc);
            CloseThemeData(theme);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public IntPtr DefaultScheme;
    }

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref HighContrast value, uint flags);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenThemeData(IntPtr window, string classList);

    [DllImport("uxtheme.dll")]
    private static extern int DrawThemeBackground(IntPtr theme, IntPtr dc, int part, int state, in NativeRect rect, IntPtr clip);

    [DllImport("uxtheme.dll")]
    private static extern int CloseThemeData(IntPtr theme);

    // The shell owns the menus, but the handler must retain and release their HBITMAPs.
    private sealed class SafeMenuBitmap : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeMenuBitmap(IntPtr bitmap) : base(true) => SetHandle(bitmap);
        protected override bool ReleaseHandle() => DeleteObject(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, in Guid iid, out IntPtr factory);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr bitmap, int size, out NativeBitmap info);

    public int InvokeCommand(IntPtr pici)
    {
        try 
        {
            var info = Marshal.PtrToStructure<CMINVOKECOMMANDINFO>(pici);
            
            // Check if high word is 0 (index based)
            if (HighWord((uint)info.lpVerb.ToInt64()) == 0)
            {
                int id = LowWord((uint)info.lpVerb.ToInt64());
                
                if (id >= 0 && id < _cmdIdMap.Count)
                {
                    var item = _cmdIdMap[id];
                    Log($"InvokeCommand: {item.Title}");
                    CommandHelper.ExecuteCommand(item, _legacySelectedPaths, Log);
                }
            }
        }
        catch(Exception ex)
        {
            Log($"InvokeCommand Error: {ex}");
        }
        return 0; // S_OK
    }

    public int GetCommandString(nuint idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax)
    {
        return 0; // S_OK
    }

    private static int HighWord(uint number) => (int)((number >> 16) & 0xffff);
    private static int LowWord(uint number) => (int)(number & 0xffff);
    
    // P/Invokes
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, [Out] StringBuilder? lpszFile, uint cch);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM pmedium);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool InsertMenuItem(IntPtr hMenu, uint uItem, bool fByPosition, [In] ref MENUITEMINFO lpmii);
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MENUITEMINFO
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public string? dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }
    
    private const uint MIIM_STATE = 0x00000001;
    private const uint MIIM_ID = 0x00000002;
    private const uint MIIM_SUBMENU = 0x00000004;
    private const uint MIIM_CHECKMARKS = 0x00000008;
    private const uint MIIM_TYPE = 0x00000010;
    private const uint MIIM_DATA = 0x00000020;
    private const uint MIIM_STRING = 0x00000040;
    private const uint MIIM_BITMAP = 0x00000080;
    private const uint MIIM_FTYPE = 0x00000100;
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct CMINVOKECOMMANDINFO
    {
        public uint cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
    }
}
