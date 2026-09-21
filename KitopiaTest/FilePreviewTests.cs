using System.IO.Compression;
using System.Text;
using Kitopia.Desktop.Features.Search.Preview;
using Kitopia.Desktop.Features.Search.ViewModels;

namespace KitopiaTest;

[TestClass]
public sealed class FilePreviewTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize() => _directory = Directory.CreateTempSubdirectory("Kitopia-file-preview-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    [DataRow(".txt")]
    [DataRow(".md")]
    [DataRow(".cs")]
    [DataRow(".html")]
    public async Task LoadAsync_TextFile_PreviewsFileContentsWithoutExecutingMarkup(string extension)
    {
        var path = Path.Combine(_directory, "预览" + extension);
        const string text = "文件内容\n<script>alert('test')</script>";
        await File.WriteAllTextAsync(path, text, Encoding.UTF8);
        var preview = await FilePreviewLoader.LoadAsync(path);
        Assert.AreEqual(text, preview.Text);
        Assert.IsNull(preview.NativePath);
        Assert.AreEqual(Path.GetFileName(path), preview.Name);
    }

    [TestMethod]
    public async Task LoadAsync_LargeText_LimitsPreviewAndReportsTruncation()
    {
        var path = Path.Combine(_directory, "large.log");
        await File.WriteAllTextAsync(path, new string('x', 300_000));
        var preview = await FilePreviewLoader.LoadAsync(path);
        Assert.AreEqual(256 * 1024, preview.Text!.Length);
        Assert.IsNotNull(preview.Notice);
    }

    [TestMethod]
    public async Task LoadAsync_UnicodeText_DetectsByteOrderMark()
    {
        var path = Path.Combine(_directory, "unicode.txt");
        await File.WriteAllTextAsync(path, "你好，文件速览", Encoding.Unicode);
        Assert.AreEqual("你好，文件速览", (await FilePreviewLoader.LoadAsync(path)).Text);
    }

    [TestMethod]
    public async Task LoadAsync_Zip_ListsEntriesWithoutExtractingPaths()
    {
        var path = Path.Combine(_directory, "archive.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("folder/");
            using var writer = new StreamWriter(archive.CreateEntry("../../outside.txt").Open());
            writer.Write("preview only");
        }
        var preview = await FilePreviewLoader.LoadAsync(path);
        Assert.AreEqual(2, preview.Entries!.Count);
        Assert.IsTrue(preview.Entries[0].IsDirectory);
        Assert.AreEqual("../../outside.txt", preview.Entries[1].Name);
        CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(_directory));
    }

    [TestMethod]
    public async Task LoadAsync_Directory_ListsFilesAndFolders()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "folder"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "file.txt"), "hello");
        var preview = await FilePreviewLoader.LoadAsync(_directory);
        Assert.AreEqual(2, preview.Entries!.Count);
        Assert.IsTrue(preview.Entries.Any(entry => entry.Name == "folder" && entry.IsDirectory));
        Assert.IsTrue(preview.Entries.Any(entry => entry.Name == "file.txt" && !entry.IsDirectory));
    }

    [TestMethod]
    [DataRow(".pdf")]
    [DataRow(".mp4")]
    [DataRow(".wav")]
    [DataRow(".docx")]
    public async Task LoadAsync_NativeFormat_RoutesToFilePreviewHost(string extension)
    {
        var path = Path.Combine(_directory, "file" + extension);
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var preview = await FilePreviewLoader.LoadAsync(path);
        Assert.AreEqual(path, preview.NativePath);
        Assert.IsNull(preview.Text);
    }

    [TestMethod]
    public async Task SetFilesAsync_MultipleFiles_NavigatesAndStopsAtBoundaries()
    {
        var first = Path.Combine(_directory, "first.txt");
        var second = Path.Combine(_directory, "second.txt");
        await File.WriteAllTextAsync(first, "first");
        await File.WriteAllTextAsync(second, "second");
        using var model = new MouseQuickWindowViewModel();
        await model.SetFilesAsync([first, second]);
        Assert.AreEqual("first", model.PreviewText);
        Assert.IsFalse(model.PreviousCommand.CanExecute(null));
        Assert.IsTrue(model.NextCommand.CanExecute(null));
        await model.NextCommand.ExecuteAsync(null);
        Assert.AreEqual("second", model.PreviewText);
        Assert.AreEqual("2 / 2", model.PositionLabel);
        Assert.IsFalse(model.NextCommand.CanExecute(null));
        await model.PreviousCommand.ExecuteAsync(null);
        Assert.AreEqual(first, model.SelectedPath);
    }

    [TestMethod]
    public async Task SetFilesAsync_SelectionChangesDuringLoad_KeepsLatestFile()
    {
        var first = Path.Combine(_directory, "first.txt");
        var second = Path.Combine(_directory, "second.txt");
        await File.WriteAllTextAsync(first, new string('a', 300_000));
        await File.WriteAllTextAsync(second, "latest");
        using var model = new MouseQuickWindowViewModel();
        var firstLoad = model.SetFilesAsync([first]);
        await model.SetFilesAsync([second]);
        await firstLoad;
        Assert.AreEqual(second, model.SelectedPath);
        Assert.AreEqual("latest", model.PreviewText);
        Assert.IsFalse(model.IsLoading);
    }

    [TestMethod]
    public async Task SetFilesAsync_CorruptedImage_ShowsErrorWithoutThrowing()
    {
        var path = Path.Combine(_directory, "broken.png");
        await File.WriteAllBytesAsync(path, [0, 1, 2]);
        using var model = new MouseQuickWindowViewModel();
        await model.SetFilesAsync([path]);
        Assert.IsNotNull(model.Message);
        Assert.IsFalse(model.IsLoading);
        Assert.IsNull(model.PreviewImage);
    }

    [TestMethod]
    public async Task SetFilesAsync_CorruptedArchive_ShowsErrorAndRecovers()
    {
        var archive = Path.Combine(_directory, "broken.zip");
        var text = Path.Combine(_directory, "valid.txt");
        await File.WriteAllBytesAsync(archive, [0, 1, 2]);
        await File.WriteAllTextAsync(text, "valid content");
        using var model = new MouseQuickWindowViewModel();
        await model.SetFilesAsync([archive]);
        Assert.IsNotNull(model.Message);
        Assert.IsFalse(model.IsLoading);
        await model.SetFilesAsync([text]);
        Assert.IsNull(model.Message);
        Assert.AreEqual("valid content", model.PreviewText);
    }

    [TestMethod]
    public async Task SetFilesAsync_MissingFile_ShowsErrorAndCanLoadAnotherSelection()
    {
        using var model = new MouseQuickWindowViewModel();
        await model.SetFilesAsync([Path.Combine(_directory, "missing.png")]);
        Assert.IsNotNull(model.Message);
        Assert.IsFalse(model.IsLoading);
        await model.SetFilesAsync([]);
        Assert.IsFalse(model.OpenFileCommand.CanExecute(null));
        Assert.IsFalse(model.NextCommand.CanExecute(null));
    }
}
