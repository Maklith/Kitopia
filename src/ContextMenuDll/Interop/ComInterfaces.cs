using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ContextMenuDll.Interop;

#if DEBUG
[ComImport]
#else
[GeneratedComInterface]
#endif
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IExplorerCommand
{
    void GetTitle([MarshalUsing(typeof(ComInterfaceMarshaller<IShellItemArray>))] IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
    void GetIcon([MarshalUsing(typeof(ComInterfaceMarshaller<IShellItemArray>))] IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszIcon);
    void GetToolTip([MarshalUsing(typeof(ComInterfaceMarshaller<IShellItemArray>))] IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszInfotip);
    void GetCanonicalName(out Guid pguidCommandName);
    void GetState([MarshalUsing(typeof(ComInterfaceMarshaller<IShellItemArray>))] IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out uint pCmdState);
    void Invoke([MarshalUsing(typeof(ComInterfaceMarshaller<IShellItemArray>))] IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.Interface)] object? pbc);
    void GetFlags(out uint pFlags);
    void EnumSubCommands([MarshalUsing(typeof(ComInterfaceMarshaller<IEnumExplorerCommand>))] out IEnumExplorerCommand? ppEnum);
}

#if DEBUG
[ComImport]
#else
[GeneratedComInterface]
#endif
[Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IEnumExplorerCommand
{
    // Arrays not supported by source generator yet, use IntPtr
    [PreserveSig]
    int Next(uint celt, IntPtr pElements, out uint pceltFetched);
    [PreserveSig]
    int Skip(uint celt);
    void Reset();
    void Clone([MarshalUsing(typeof(ComInterfaceMarshaller<IEnumExplorerCommand>))] out IEnumExplorerCommand ppEnum);
}

#if DEBUG
[ComImport]
#else
[GeneratedComInterface]
#endif
[Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IShellItemArray
{
    void BindToHandler([MarshalAs(UnmanagedType.Interface)] object pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvOut);
    void GetPropertyStore(int flags, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
    void GetPropertyDescriptionList(ref Guid keyType, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
    void GetAttributes(int AttribFlags, int sfgaoMask, out int psfgaoAttribs);
    void GetCount(out uint pdwNumItems);
    void GetItemAt(uint dwIndex, [MarshalUsing(typeof(ComInterfaceMarshaller<IShellItem>))] out IShellItem ppsi);
    void EnumItems([MarshalAs(UnmanagedType.Interface)] out object ppenumShellItems);
}

#if DEBUG
[ComImport]
#else
[GeneratedComInterface]
#endif
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IShellItem
{
    void BindToHandler([MarshalAs(UnmanagedType.Interface)] object pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
    void GetParent([MarshalUsing(typeof(ComInterfaceMarshaller<IShellItem>))] out IShellItem ppsi);
    void GetDisplayName(uint sigdnName, out IntPtr ppszName);
    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    void Compare([MarshalUsing(typeof(ComInterfaceMarshaller<IShellItem>))] IShellItem psi, uint hint, out int piOrder);
}

public enum EXPCMDSTATE : uint
{
    ECS_ENABLED = 0x0,
    ECS_DISABLED = 0x1,
    ECS_HIDDEN = 0x2,
    ECS_CHECKBOX = 0x4,
    ECS_CHECKED = 0x8,
    ECS_RADIOCHECK = 0x10
}

[Flags]
public enum EXPCMDFLAGS : uint
{
    ECF_DEFAULT = 0x0,
    ECF_HASSUBCOMMANDS = 0x1,
    ECF_HASSPLITBUTTON = 0x2,
    ECF_HIDELABEL = 0x4,
    ECF_ISSEPARATOR = 0x8,
    ECF_HASLUASHIELD = 0x10,
    ECF_SEPARATORBEFORE = 0x20,
    ECF_SEPARATORAFTER = 0x40,
    ECF_ISDROPDOWN = 0x80,
    ECF_TOGGLEABLE = 0x100,
    ECF_AUTOMENUICONS = 0x200
}

public enum SIGDN : uint
{
    SIGDN_NORMALDISPLAY = 0,
    SIGDN_PARENTRELATIVEPARSING = 0x80018001,
    SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000,
    SIGDN_PARENTRELATIVEEDITING = 0x80031001,
    SIGDN_DESKTOPABSOLUTEEDITING = 0x8004c000,
    SIGDN_FILESYSPATH = 0x80058000,
    SIGDN_URL = 0x80068000,
    SIGDN_PARENTRELATIVEFORADDRESSBAR = 0x8007c001,
    SIGDN_PARENTRELATIVE = 0x80080001
}

#if DEBUG
[ComImport]
#else
[GeneratedComInterface]
#endif
[Guid("00000001-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IClassFactory
{
    void CreateInstance([MarshalAs(UnmanagedType.Interface)] object? pUnkOuter, ref Guid riid, out IntPtr ppvObject);
    void LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
}

#if DEBUG
[ComImport]
#else
[GeneratedComInterface]
#endif
[Guid("000214e4-0000-0000-c000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IContextMenu
{
    [PreserveSig]
    int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    
    [PreserveSig]
    int InvokeCommand(IntPtr pici);
    
    [PreserveSig]
    int GetCommandString(nuint idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
}

#if DEBUG
[ComImport]
#else
[GeneratedComInterface]
#endif
[Guid("000214e8-0000-0000-c000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IShellExtInit
{
    [PreserveSig]
    int Initialize(IntPtr pidlFolder, IntPtr pdtobj, IntPtr hKeyProgID);
}

public enum CMF : uint
{
    CMF_NORMAL = 0x00000000,
    CMF_DEFAULTONLY = 0x00000001,
    CMF_VERBSONLY = 0x00000002,
    CMF_EXPLORE = 0x00000004,
    CMF_NOVERBS = 0x00000008,
    CMF_CANRENAME = 0x00000010,
    CMF_NODEFAULT = 0x00000020,
    CMF_INCLUDESTATIC = 0x00000040,
    CMF_ITEMMENU = 0x00000080,
    CMF_EXTENDEDVERBS = 0x00000100,
    CMF_DISABLEDVERBS = 0x00000200,
    CMF_ASYNCVERBSTATE = 0x00000400,
    CMF_OPTIMIZEFORINVOKE = 0x00000800,
    CMF_SYNCCASCADEMENU = 0x00001000,
    CMF_DONOTPICKDEFAULT = 0x00002000,
    CMF_RESERVED = 0xffff0000
}

public enum GCS : uint
{
    GCS_VERBA = 0x00000000,
    GCS_HELPTEXTA = 0x00000001,
    GCS_VALIDATEA = 0x00000002,
    GCS_VERBW = 0x00000004,
    GCS_HELPTEXTW = 0x00000005,
    GCS_VALIDATEW = 0x00000006,
    GCS_VERBICONW = 0x00000014,
    GCS_UNICODE = 0x00000004
}
