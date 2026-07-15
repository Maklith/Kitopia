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
}