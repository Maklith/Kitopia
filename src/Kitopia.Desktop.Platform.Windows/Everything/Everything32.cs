#region

using System.Runtime.InteropServices;
using System.Text;

#endregion

namespace Kitopia.Desktop.Platform.Windows.Everything;

public static class Everything32
{
    [DllImport("Everything32.dll", CharSet = CharSet.Unicode)]
    public static extern int Everything_SetSearchW(string lpSearchString);

    [DllImport("Everything32.dll")]
    public static extern void Everything_SetMatchPath(bool bEnable);

    [DllImport("Everything32.dll")]
    public static extern void Everything_SetMatchCase(bool bEnable);

    [DllImport("Everything32.dll")]
    public static extern void Everything_SetMatchWholeWord(bool bEnable);

    [DllImport("Everything32.dll")]
    public static extern void Everything_SetRegex(bool bEnable);

    [DllImport("Everything32.dll")]
    public static extern void Everything_SetMax(int dwMax);

    [DllImport("Everything32.dll")]
    public static extern void Everything_SetOffset(int dwOffset);

    [DllImport("Everything32.dll")]
    public static extern void Everything_SetSort(uint dwSort);

    [DllImport("Everything32.dll")]
    public static extern uint Everything_GetSort();

    [DllImport("Everything32.dll")]
    public static extern void Everything_SetReplyWindow(IntPtr hWnd);

    [DllImport("Everything32.dll")]
    public static extern void Everything_SetReplyID(int nId);

    [DllImport("Everything32.dll")]
    public static extern bool Everything_GetMatchPath();

    [DllImport("Everything32.dll")]
    public static extern bool Everything_GetMatchCase();

    [DllImport("Everything32.dll")]
    public static extern bool Everything_GetMatchWholeWord();

    [DllImport("Everything32.dll")]
    public static extern bool Everything_GetRegex();

    [DllImport("Everything32.dll")]
    public static extern uint Everything_GetMax();

    [DllImport("Everything32.dll")]
    public static extern uint Everything_GetOffset();

    [DllImport("Everything32.dll")]
    public static extern string Everything_GetSearch();

    [DllImport("Everything32.dll")]
    public static extern int Everything_GetLastError();

    [DllImport("Everything32.dll")]
    public static extern IntPtr Everything_GetReplyWindow();

    [DllImport("Everything32.dll")]
    public static extern int Everything_GetReplyID();

    [DllImport("Everything32.dll")]
    public static extern bool Everything_QueryW(bool bWait);

    [DllImport("Everything32.dll")]
    public static extern bool Everything_IsQueryReply(int message, IntPtr wParam, IntPtr lParam, uint nId);

    [DllImport("Everything32.dll")]
    public static extern void Everything_SortResultsByPath();

    [DllImport("Everything32.dll")]
    public static extern int Everything_GetNumFileResults();

    [DllImport("Everything32.dll")]
    public static extern int Everything_GetNumFolderResults();

    [DllImport("Everything32.dll")]
    public static extern int Everything_GetNumResults();

    [DllImport("Everything32.dll")]
    public static extern int Everything_GetTotFileResults();

    [DllImport("Everything32.dll")]
    public static extern int Everything_GetTotFolderResults();

    [DllImport("Everything32.dll")]
    public static extern int Everything_GetTotResults();

    [DllImport("Everything32.dll")]
    public static extern bool Everything_IsVolumeResult(int nIndex);

    [DllImport("Everything32.dll")]
    public static extern bool Everything_IsFolderResult(int nIndex);

    [DllImport("Everything32.dll")]
    public static extern bool Everything_IsFileResult(int nIndex);

    [DllImport("Everything32.dll", CharSet = CharSet.Unicode)]
    public static extern void Everything_GetResultFullPathNameW(int nIndex, StringBuilder lpString, int nMaxCount);

    [DllImport("Everything32.dll")]
    public static extern void Everything_Reset();

    public const uint EVERYTHING_REQUEST_FILE_NAME = 0x00000001;
    public const uint EVERYTHING_REQUEST_PATH = 0x00000002;
    public const uint EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME = 0x00000004;
    public const uint EVERYTHING_REQUEST_EXTENSION = 0x00000008;
    public const uint EVERYTHING_REQUEST_SIZE = 0x00000010;
    public const uint EVERYTHING_REQUEST_DATE_CREATED = 0x00000020;
    public const uint EVERYTHING_REQUEST_DATE_MODIFIED = 0x00000040;
    public const uint EVERYTHING_REQUEST_DATE_ACCESSED = 0x00000080;
    public const uint EVERYTHING_REQUEST_ATTRIBUTES = 0x00000100;

    [DllImport("Everything32.dll")]
    public static extern void Everything_SetRequestFlags(uint dwRequestFlags);

    [DllImport("Everything32.dll")]
    public static extern uint Everything_GetRequestFlags();

    [DllImport("Everything32.dll")]
    public static extern bool Everything_GetResultSize(int nIndex, out long lpFileSize);

    [DllImport("Everything32.dll")]
    public static extern bool Everything_GetResultDateModified(int nIndex, out long lpFileTime);
}
