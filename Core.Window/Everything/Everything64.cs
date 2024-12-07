#region

using System.Runtime.InteropServices;
using System.Text;

#endregion

namespace Core.Window.Everything;

public class Everything64
{
    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    public static extern int Everything_SetSearchW(string lpSearchString);

    [DllImport("Everything64.dll")]
    public static extern void Everything_SetMatchPath(bool bEnable);

    [DllImport("Everything64.dll")]
    public static extern void Everything_SetMatchCase(bool bEnable);

    [DllImport("Everything64.dll")]
    public static extern void Everything_SetMatchWholeWord(bool bEnable);

    [DllImport("Everything64.dll")]
    public static extern void Everything_SetRegex(bool bEnable);

    [DllImport("Everything64.dll")]
    public static extern void Everything_SetMax(int dwMax);

    [DllImport("Everything64.dll")]
    public static extern void Everything_SetOffset(int dwOffset);

    [DllImport("Everything64.dll")]
    public static extern void Everything_SetReplyWindow(IntPtr hWnd);

    [DllImport("Everything64.dll")]
    public static extern void Everything_SetReplyID(int nId);

    [DllImport("Everything64.dll")]
    public static extern bool Everything_GetMatchPath();

    [DllImport("Everything64.dll")]
    public static extern bool Everything_GetMatchCase();

    [DllImport("Everything64.dll")]
    public static extern bool Everything_GetMatchWholeWord();

    [DllImport("Everything64.dll")]
    public static extern bool Everything_GetRegex();

    [DllImport("Everything64.dll")]
    public static extern uint Everything_GetMax();

    [DllImport("Everything64.dll")]
    public static extern uint Everything_GetOffset();

    [DllImport("Everything64.dll")]
    public static extern string Everything_GetSearch();

    [DllImport("Everything64.dll")]
    public static extern int Everything_GetLastError();

    [DllImport("Everything64.dll")]
    public static extern IntPtr Everything_GetReplyWindow();

    [DllImport("Everything64.dll")]
    public static extern int Everything_GetReplyID();

    [DllImport("Everything64.dll")]
    public static extern bool Everything_QueryW(bool bWait);

    [DllImport("Everything64.dll")]
    public static extern bool Everything_IsQueryReply(int message, IntPtr wParam, IntPtr lParam, uint nId);

    [DllImport("Everything64.dll")]
    public static extern void Everything_SortResultsByPath();

    [DllImport("Everything64.dll")]
    public static extern int Everything_GetNumFileResults();

    [DllImport("Everything64.dll")]
    public static extern int Everything_GetNumFolderResults();

    [DllImport("Everything64.dll")]
    public static extern int Everything_GetNumResults();

    [DllImport("Everything64.dll")]
    public static extern int Everything_GetTotFileResults();

    [DllImport("Everything64.dll")]
    public static extern int Everything_GetTotFolderResults();

    [DllImport("Everything64.dll")]
    public static extern int Everything_GetTotResults();

    [DllImport("Everything64.dll")]
    public static extern bool Everything_IsVolumeResult(int nIndex);

    [DllImport("Everything64.dll")]
    public static extern bool Everything_IsFolderResult(int nIndex);

    [DllImport("Everything64.dll")]
    public static extern bool Everything_IsFileResult(int nIndex);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    public static extern void Everything_GetResultFullPathNameW(int nIndex, StringBuilder lpString, int nMaxCount);

    [DllImport("Everything64.dll")]
    public static extern void Everything_Reset();
}