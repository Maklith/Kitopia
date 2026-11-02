#if WINDOWS
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ContextMenuDll;

namespace KitopiaTest.Services;

[TestClass]
public sealed class ExplorerContextMenuIconTests
{
    [TestMethod]
    [DoNotParallelize]
    public void MenuBackground_LightAndDarkThemes_ReturnUsableColorsWithoutLeakingGdiObjects()
    {
        var method = typeof(KitopiaExplorerCommand).GetMethod("GetLegacyMenuBackgroundColor", BindingFlags.Static | BindingFlags.NonPublic)!;
        var light = (uint)method.Invoke(null, [false])!;
        var dark = (uint)method.Invoke(null, [true])!;
        Assert.AreNotEqual(uint.MaxValue, light);
        Assert.AreNotEqual(uint.MaxValue, dark);
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var before = GetGuiResources(process.Handle, 0);
        for (var i = 0; i < 100; i++) method.Invoke(null, [i % 2 == 0]);
        Assert.IsTrue(GetGuiResources(process.Handle, 0) <= before + 2, "Theme sampling must release its bitmap, DC and theme handles.");
    }

    [TestMethod]
    public void QueryContextMenu_TransparentIcon_CompositesBackgroundAndPreservesBlackArtwork()
    {
        var iconPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ico");
        try
        {
            using (var writer = new BinaryWriter(File.Create(iconPath)))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)1);
                writer.Write((byte)16);
                writer.Write((byte)16);
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(40 + 16 * 16 * 4 + 16 * 4);
                writer.Write(22);
                writer.Write(40);
                writer.Write(16);
                writer.Write(32); // ICO DIB height includes the AND mask.
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(new byte[24]);
                for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                {
                    var border = x < 3 || x >= 13 || y < 3 || y >= 13;
                    var black = x >= 5 && x <= 10 && y >= 5 && y <= 10;
                    writer.Write((byte)(border || black ? 0 : 200));
                    writer.Write((byte)(border || black ? 0 : 160));
                    writer.Write((byte)(border || black ? 0 : 240));
                    writer.Write((byte)(border ? 0 : black ? 255 : 128));
                }
                writer.Write(new byte[16 * 4]);
            }

            var command = new KitopiaClassicContextMenu();
            typeof(KitopiaExplorerCommand).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(command, new ContextMenuConfig { Items = [new ContextMenuItem { Title = "Transparent icon", Icon = iconPath }] });
            var menu = CreatePopupMenu();
            try
            {
                Assert.IsGreaterThan(0, command.QueryContextMenu(menu, 0, 1, 32767, 0));
                var bitmap = ReadMenuItem(ReadMenuItem(menu, 0).hSubMenu, 0).hbmpItem;
                AssertBitmap(bitmap);
                GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), out var info);
                var pixels = new byte[info.WidthBytes * info.Height];
                Marshal.Copy(info.Bits, pixels, 0, pixels.Length);
                var transparent = false;
                var translucent = false;
                var opaqueBlack = false;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    transparent |= pixels[i + 3] == 0;
                    translucent |= pixels[i + 3] is > 0 and < 255;
                    // Shell resizing can round alpha to 254; compositing then adds at most one to black.
                    opaqueBlack |= pixels[i + 3] == 255 && pixels[i] <= 1 && pixels[i + 1] <= 1 && pixels[i + 2] <= 1;
                }
                Assert.IsFalse(transparent, "The surrogate must receive opaque pixels with the background already composited.");
                Assert.IsFalse(translucent, "The surrogate cannot preserve per-pixel alpha.");
                Assert.IsTrue(opaqueBlack, "Opaque black is artwork and must not become transparent.");
                var color = (uint)typeof(KitopiaExplorerCommand).GetField("_legacyMenuBackgroundColor", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(command)!;
                Assert.AreEqual((byte)(color >> 16), pixels[0]);
                Assert.AreEqual((byte)(color >> 8), pixels[1]);
                Assert.AreEqual((byte)color, pixels[2]);
                Assert.AreEqual((byte)255, pixels[3]);
            }
            finally
            {
                DestroyMenu(menu);
                GC.KeepAlive(command);
            }
        }
        finally
        {
            File.Delete(iconPath);
        }
    }

    [TestMethod]
    public void QueryContextMenu_RootAndNestedItems_AttachesIconsAndKeepsMissingIconsOptional()
    {
        var command = new KitopiaClassicContextMenu();
        command.GetIcon(null, out var iconPath);
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var config = new ContextMenuConfig
        {
            Items =
            [
                new ContextMenuItem { Title = "Icon file", Icon = iconPath },
                new ContextMenuItem
                {
                    Title = "Executable group", Icon = executable,
                    SubItems = [new ContextMenuItem { Title = "Nested item", Icon = executable }]
                },
                new ContextMenuItem { Title = "No icon" },
                new ContextMenuItem { Title = "Missing icon", Icon = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ico") }
            ]
        };
        typeof(KitopiaExplorerCommand).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(command, config);

        var menu = CreatePopupMenu();
        var secondMenu = CreatePopupMenu();
        try
        {
            Assert.AreEqual(6, command.QueryContextMenu(menu, 0, 1, 32767, 0));
            Assert.AreEqual(1, GetMenuItemCount(menu));
            var root = ReadMenuItem(menu, 0);
            AssertBitmap(root.hbmpItem);
            Assert.AreEqual(4, GetMenuItemCount(root.hSubMenu));
            Assert.AreEqual(root.hbmpItem, ReadMenuItem(root.hSubMenu, 0).hbmpItem);
            var group = ReadMenuItem(root.hSubMenu, 1);
            AssertBitmap(group.hbmpItem);
            Assert.AreEqual(group.hbmpItem, ReadMenuItem(group.hSubMenu, 0).hbmpItem);
            Assert.AreEqual(IntPtr.Zero, ReadMenuItem(root.hSubMenu, 2).hbmpItem);
            Assert.AreEqual(IntPtr.Zero, ReadMenuItem(root.hSubMenu, 3).hbmpItem);

            Assert.AreEqual(6, command.QueryContextMenu(secondMenu, 0, 1, 32767, 0));
            Assert.AreEqual(root.hbmpItem, ReadMenuItem(secondMenu, 0).hbmpItem);
            AssertBitmap(root.hbmpItem); // A second query must not invalidate the first menu's bitmaps.
        }
        finally
        {
            DestroyMenu(secondMenu);
            DestroyMenu(menu);
            GC.KeepAlive(command);
        }
    }

    [TestMethod]
    public void MenuBitmap_HandlerCollected_ReleasesGdiHandle()
    {
        var bitmap = CreateMenuAndReleaseHandler();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.AreEqual(0, GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), out _));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IntPtr CreateMenuAndReleaseHandler()
    {
        var command = new KitopiaClassicContextMenu();
        typeof(KitopiaExplorerCommand).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(command, new ContextMenuConfig { Items = [new ContextMenuItem { Title = "Test item" }] });
        var menu = CreatePopupMenu();
        try
        {
            Assert.IsGreaterThan(0, command.QueryContextMenu(menu, 0, 1, 32767, 0));
            var bitmap = ReadMenuItem(menu, 0).hbmpItem;
            AssertBitmap(bitmap);
            return bitmap;
        }
        finally
        {
            DestroyMenu(menu);
            GC.KeepAlive(command);
        }
    }

    private static KitopiaExplorerCommand.MENUITEMINFO ReadMenuItem(IntPtr menu, uint index)
    {
        var item = new KitopiaExplorerCommand.MENUITEMINFO
        {
            cbSize = (uint)Marshal.SizeOf<KitopiaExplorerCommand.MENUITEMINFO>(),
            fMask = 0x4 | 0x80 // MIIM_SUBMENU | MIIM_BITMAP
        };
        Assert.IsTrue(GetMenuItemInfo(menu, index, true, ref item));
        return item;
    }

    private static void AssertBitmap(IntPtr bitmap)
    {
        Assert.AreNotEqual(IntPtr.Zero, bitmap);
        Assert.IsGreaterThan(0, GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), out var info));
        Assert.IsGreaterThan(0, info.Width);
        Assert.IsGreaterThan(0, info.Height);
        Assert.AreEqual((ushort)32, info.BitsPixel);
        Assert.AreNotEqual(IntPtr.Zero, info.Bits);
        var pixels = new byte[info.WidthBytes * info.Height];
        Marshal.Copy(info.Bits, pixels, 0, pixels.Length);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            Assert.AreEqual((byte)255, alpha, "MSIX classic menu bitmaps must be composited before marshalling.");
            Assert.IsTrue(pixels[i] <= alpha && pixels[i + 1] <= alpha && pixels[i + 2] <= alpha,
                $"Menu bitmaps require premultiplied BGRA; pixel {i / 4} has BGRA ({pixels[i]}, {pixels[i + 1]}, {pixels[i + 2]}, {alpha}).");
        }
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

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMenuItemInfo(IntPtr menu, uint item, bool byPosition, ref KitopiaExplorerCommand.MENUITEMINFO info);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetObject(IntPtr bitmap, int size, out NativeBitmap info);
}
#endif
