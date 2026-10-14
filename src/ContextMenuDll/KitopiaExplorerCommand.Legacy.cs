﻿using System.Runtime.InteropServices;
using System.Text;
using ContextMenuDll.Interop;

namespace ContextMenuDll;

public partial class KitopiaExplorerCommand
{
    private List<string> _legacySelectedPaths = new();
    private List<ContextMenuItem> _cmdIdMap = new();

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

        // Create a submenu
        IntPtr hSubMenu = CreatePopupMenu();
        
        int idOffset = 0;
        
        foreach (var item in _config.Items)
        {
            AddLegacyMenuItem(hSubMenu, item, ref idOffset, idCmdFirst);
        }

        // Add the root item "Kitopia"
        var mii = new MENUITEMINFO();
        mii.cbSize = (uint)Marshal.SizeOf(typeof(MENUITEMINFO));
        mii.fMask = MIIM_STRING | MIIM_SUBMENU | MIIM_ID; 
        
        mii.wID = idCmdFirst + (uint)idOffset;
        mii.hSubMenu = hSubMenu;
        mii.dwTypeData = "Kitopia";
        
        InsertMenuItem(hMenu, indexMenu, true, ref mii);
        
        // Add a dummy entry for the root item so we track the ID count
        _cmdIdMap.Add(new ContextMenuItem { Title = "Root" });
        idOffset++;
        
        return idOffset;
    }

    private void AddLegacyMenuItem(IntPtr hMenu, ContextMenuItem item, ref int idOffset, uint idCmdFirst)
    {
        if (item.SubItems != null && item.SubItems.Count > 0)
        {
             IntPtr hSub = CreatePopupMenu();
             foreach(var sub in item.SubItems)
             {
                 AddLegacyMenuItem(hSub, sub, ref idOffset, idCmdFirst);
             }
             
             var mii = new MENUITEMINFO();
             mii.cbSize = (uint)Marshal.SizeOf(typeof(MENUITEMINFO));
             mii.fMask = MIIM_STRING | MIIM_SUBMENU | MIIM_ID;
             mii.wID = idCmdFirst + (uint)idOffset;
             mii.hSubMenu = hSub;
             mii.dwTypeData = item.Title;
             
             InsertMenuItem(hMenu, unchecked((uint)-1), true, ref mii); // Append
             
             _cmdIdMap.Add(item);
             idOffset++;
        }
        else
        {
             var mii = new MENUITEMINFO();
             mii.cbSize = (uint)Marshal.SizeOf(typeof(MENUITEMINFO));
             mii.fMask = MIIM_STRING | MIIM_ID;
             mii.wID = idCmdFirst + (uint)idOffset;
             mii.dwTypeData = item.Title;
             
             InsertMenuItem(hMenu, unchecked((uint)-1), true, ref mii); // Append
             
             _cmdIdMap.Add(item);
             idOffset++;
        }
    }

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
