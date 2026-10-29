using System.Reflection;
using System.Runtime.InteropServices;
using Kitopia.Desktop.Platform.Windows;
using static Vanara.PInvoke.Shell32;

namespace KitopiaTest.Search;

[TestClass]
public sealed class ExplorerFileSelectionTests
{
    [TestMethod]
    [DataRow("directory")]
    [DataRow("root")]
    [DataRow("virtual")]
    public void ReadDirectory_NativeShellView_ResolvesOnlyFilesystemLocations(string kind)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows Shell COM requires Windows.");
            return;
        }

        var temporaryDirectory = kind == "directory" ? Directory.CreateTempSubdirectory("Kitopia 目录搜索 ") : null;
        var expected = kind == "root" ? Path.GetPathRoot(Environment.SystemDirectory) : temporaryDirectory?.FullName;
        try
        {
            using var location = expected is null ? new PIDL(KNOWNFOLDERID.FOLDERID_ComputerFolder) : new PIDL(expected);
            var folder = SHBindToObject<IShellFolder>(null, location);
            IShellView? view = null;
            try
            {
                var info = new SFV_CREATE { cbSize = (uint)Marshal.SizeOf<SFV_CREATE>(), pshf = folder };
                SHCreateShellFolderView(in info, out view).ThrowIfFailed();
                Assert.IsNotNull(view);
                var readDirectory = typeof(ExplorerFileSelection)
                    .GetMethod("ReadDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;

                var actual = (string?)readDirectory.Invoke(null, [(IFolderView2)view]);

                Assert.AreEqual(expected, actual);
            }
            finally
            {
                if (view is not null) Marshal.ReleaseComObject(view);
                Marshal.ReleaseComObject(folder);
            }
        }
        finally { temporaryDirectory?.Delete(); }
    }
}
