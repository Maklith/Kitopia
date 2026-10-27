using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Kitopia.Desktop.Platform.Windows;
using Kitopia.Desktop.Platform.Windows.AppTools;
using PluginCore;

namespace KitopiaTest.AppTools;

[TestClass]
[DoNotParallelize]
public sealed class IconToolsTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions()).UseSkia();

    [TestMethod]
    public void GetIconFromImageList_ReturnedIcon_RemainsUsableAfterNativeHandleIsReleased()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Shell icons require Windows.");
            return;
        }

        using var icon = IconTools.GetIconFromImageList(Environment.SystemDirectory);
        Assert.IsNotNull(icon);
        using var bitmap = icon.ToBitmap();
        Assert.IsTrue(bitmap.Width > 0 && bitmap.Height > 0);
    }

    [TestMethod]
    [DataRow("folder", false)]
    [DataRow("文件夹_📁_𠀀", false)]
    [DataRow("已删除的文件夹_📁", true)]
    public async Task LoadIcon_FolderPath_LoadsAndCachesIcon(string folderName, bool deleteBeforeLoading)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Shell icons require Windows.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"Kitopia-IconTools-{Guid.NewGuid():N}");
        var path = Path.Combine(root, folderName);
        Directory.CreateDirectory(path);
        if (deleteBeforeLoading) Directory.Delete(path);
        try
        {
            await using var session = HeadlessUnitTestSession.StartNew(typeof(IconToolsTests));
            await session.Dispatch(async () =>
            {
                var service = new AppToolService();
                var item = new SearchViewItem { ItemDisplayName = folderName, OnlyKey = path, FileType = FileType.文件夹 };
                var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(SearchViewItem.Icon) && item.Icon != null)
                        loaded.TrySetResult();
                };
                service.LoadIcon(item);
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsNotNull(item.Icon);
                Assert.AreEqual(new PixelSize(64, 64), item.Icon.PixelSize);

                var cachedItem = new SearchViewItem { ItemDisplayName = folderName, OnlyKey = path, FileType = FileType.文件夹 };
                service.LoadIcon(cachedItem);
                Dispatcher.UIThread.RunJobs();
                Assert.AreSame(item.Icon, cachedItem.Icon);
            }, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
