using System.Security.Cryptography;
using KitopiaEx.BatchRename;
using KitopiaEx.ImageCompression;
using KitopiaEx.SearchWindow.InputDataAnalyzer;
using PluginCore.Media;
using PluginCore.SearchWindow.InputData;
using SkiaSharp;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class BatchRenameTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Kitopia rename tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public async Task StartAsync_NamingRules_PreviewsAndMovesFilesWithoutChangingContent()
    {
        var first = CreateFile("old report.txt", "first");
        var second = CreateFile("old photo.JPG", "second");
        using var vm = new BatchRenameViewModel(new Ffmpeg())
        {
            Prefix = "pre-", FindText = "old ", ReplaceText = "new-", Suffix = "-done",
            StartNumber = 7, NumberDigits = 2
        };
        await vm.AddPathsAsync([first, second]);
        Assert.AreEqual("pre-new-report_07-done.txt", vm.Items[0].NewName);
        Assert.AreEqual("pre-new-photo_08-done.JPG", vm.Items[1].NewName);
        Assert.IsTrue(vm.CanStart);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.Items.All(item => item.Status == BatchRenameStatus.Completed));
        Assert.AreEqual("first", File.ReadAllText(vm.Items[0].OutputPath!));
        Assert.AreEqual("second", File.ReadAllText(vm.Items[1].OutputPath!));
        Assert.IsFalse(File.Exists(first));
        Assert.IsFalse(File.Exists(second));
        Assert.IsFalse(vm.CanStart);
    }

    [TestMethod]
    [DataRow(0, 1, "0", "1")]
    [DataRow(7, 3, "007", "008")]
    public async Task StartAsync_WithoutOriginalName_UsesNumbersAndKeepsExtensions(int start, int digits, string firstNumber, string secondNumber)
    {
        var first = CreateFile("1_003.html", "first");
        var second = CreateFile("1_004.HTML", "second");
        using var vm = new BatchRenameViewModel(new Ffmpeg())
        {
            KeepOriginalName = false, StartNumber = start, NumberDigits = digits
        };
        await vm.AddPathsAsync([first, second]);
        Assert.IsTrue(vm.AddNumber);
        Assert.AreEqual(firstNumber + ".html", vm.Items[0].NewName);
        Assert.AreEqual(secondNumber + ".HTML", vm.Items[1].NewName);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.Items.All(item => item.IsCompleted));
        Assert.AreEqual("first", File.ReadAllText(Path.Combine(_directory, firstNumber + ".html")));
        Assert.AreEqual("second", File.ReadAllText(Path.Combine(_directory, secondNumber + ".HTML")));
        Assert.IsFalse(File.Exists(first));
        Assert.IsFalse(File.Exists(second));
    }

    [TestMethod]
    [DataRow(true, "pre-001-done.txt")]
    [DataRow(false, "pre--done.txt")]
    public async Task StartAsync_WithoutOriginalName_PreservesAffixesAndNumberingChoice(bool addNumber, string expectedName)
    {
        var source = CreateFile("source.txt", "content");
        using var vm = new BatchRenameViewModel(new Ffmpeg())
        {
            KeepOriginalName = false, Prefix = "pre-", Suffix = "-done", AddNumber = addNumber
        };
        await vm.AddPathsAsync([source]);
        Assert.AreEqual(addNumber, vm.AddNumber);
        Assert.AreEqual(expectedName, vm.Items[0].NewName);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.Items[0].IsCompleted, vm.Items[0].Error);
        Assert.AreEqual("content", File.ReadAllText(Path.Combine(_directory, expectedName)));
        Assert.IsFalse(File.Exists(source));
    }

    [TestMethod]
    public async Task RefreshPreview_KeepOriginalNameToggle_PreservesOtherRules()
    {
        using var vm = new BatchRenameViewModel(new Ffmpeg()) { Prefix = "pre-", Suffix = "-done" };
        await vm.AddPathsAsync([CreateFile("source.txt")]);
        Assert.IsTrue(vm.KeepOriginalName);
        Assert.AreEqual("pre-source_001-done.txt", vm.Items[0].NewName);
        vm.KeepOriginalName = false;
        Assert.AreEqual("pre-001-done.txt", vm.Items[0].NewName);
        vm.KeepOriginalName = true;
        Assert.AreEqual("pre-source_001-done.txt", vm.Items[0].NewName);
        vm.AddNumber = false;
        vm.KeepOriginalName = false;
        Assert.IsFalse(vm.AddNumber);
        Assert.AreEqual("pre--done.txt", vm.Items[0].NewName);
        vm.BaseName = "custom";
        Assert.AreEqual("pre-custom-done.txt", vm.Items[0].NewName);
        vm.FindText = "custom";
        vm.ReplaceText = "renamed";
        Assert.AreEqual("pre-custom-done.txt", vm.Items[0].NewName);
        vm.KeepOriginalName = true;
        Assert.AreEqual("pre-renamed-done.txt", vm.Items[0].NewName);
        vm.KeepOriginalName = false;
        Assert.AreEqual("pre-custom-done.txt", vm.Items[0].NewName);
        Assert.AreEqual("custom", vm.FindText);
        Assert.AreEqual("renamed", vm.ReplaceText);
    }

    [TestMethod]
    public async Task RefreshPreview_WithoutAnyNameComponents_DisablesStart()
    {
        using var vm = new BatchRenameViewModel(new Ffmpeg()) { KeepOriginalName = false, AddNumber = false };
        await vm.AddPathsAsync([CreateFile("source.txt")]);
        Assert.IsTrue(vm.Items[0].HasError);
        Assert.IsFalse(vm.CanStart);
        vm.Prefix = "renamed";
        Assert.IsFalse(vm.Items[0].HasError);
        Assert.AreEqual("renamed.txt", vm.Items[0].NewName);
        Assert.IsTrue(vm.CanStart);
    }

    [TestMethod]
    public async Task RefreshPreview_DuplicateAndExistingDestinations_DisablesStartWithoutOverwriting()
    {
        var first = CreateFile("first.txt");
        var second = CreateFile("second.txt");
        using var vm = new BatchRenameViewModel(new Ffmpeg()) { BaseName = "same", AddNumber = false };
        await vm.AddPathsAsync([first, second]);
        Assert.IsTrue(vm.Items.All(item => item.HasError));
        Assert.IsFalse(vm.CanStart);
        vm.AddNumber = true;
        Assert.IsTrue(vm.CanStart);
        var existing = CreateFile("same_001.txt", "existing");
        await vm.StartCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.Items[0].HasError);
        Assert.IsTrue(File.Exists(first));
        Assert.IsTrue(File.Exists(second));
        Assert.AreEqual("existing", File.ReadAllText(existing));
    }

    [TestMethod]
    [DataRow("../escape")]
    [DataRow("bad/name")]
    [DataRow("CON")]
    [DataRow("LPT1")]
    public async Task RefreshPreview_InvalidName_DisablesStart(string name)
    {
        using var vm = new BatchRenameViewModel(new Ffmpeg()) { BaseName = name, AddNumber = false };
        await vm.AddPathsAsync([CreateFile("first.txt")]);
        Assert.IsTrue(vm.Items.Single().HasError);
        Assert.IsFalse(vm.StartCommand.CanExecute(null));
        vm.BaseName = "valid";
        Assert.IsTrue(vm.CanStart);
    }

    [TestMethod]
    public async Task AddPathsAsync_FoldersAndOverlappingImports_DeduplicatesAndReindexesAfterRemoval()
    {
        var first = CreateFile("a.txt");
        var second = CreateFile("b.txt");
        Directory.CreateDirectory(Path.Combine(_directory, "nested"));
        using var vm = new BatchRenameViewModel(new Ffmpeg());
        await Task.WhenAll(vm.AddPathsAsync([_directory, first]), vm.AddPathsAsync([second]));
        Assert.AreEqual(2, vm.Items.Count);
        vm.SelectedItem = vm.Items[0];
        vm.RemoveCommand.Execute(vm.SelectedItem);
        Assert.IsNull(vm.SelectedItem);
        Assert.AreEqual("b_001.txt", vm.Items.Single().NewName);
        await vm.AddPathsAsync([first]);
        Assert.AreEqual(2, vm.Items.Count);
        vm.ClearCommand.Execute(null);
        Assert.IsTrue(vm.IsEmpty);
        Assert.IsFalse(vm.CanStart);
    }

    [TestMethod]
    public async Task MoveCommands_SelectedFile_ReordersAndRenumbersBatch()
    {
        var first = CreateFile("first.txt", "first");
        var second = CreateFile("second.txt", "second");
        var third = CreateFile("third.txt", "third");
        using var vm = new BatchRenameViewModel(new Ffmpeg()) { KeepOriginalName = false };
        await vm.AddPathsAsync([first, second, third]);
        Assert.IsFalse(vm.MoveUpCommand.CanExecute(null));
        Assert.IsFalse(vm.MoveDownCommand.CanExecute(null));
        var selected = vm.Items[1];
        vm.SelectedItem = selected;
        vm.MoveUpCommand.Execute(null);
        Assert.AreSame(selected, vm.SelectedItem);
        CollectionAssert.AreEqual(new[] { second, first, third }, vm.Items.Select(item => item.SourcePath).ToArray());
        Assert.AreEqual("001.txt", selected.NewName);
        Assert.IsFalse(vm.MoveUpCommand.CanExecute(null));
        vm.MoveDownCommand.Execute(null);
        Assert.AreSame(selected, vm.SelectedItem);
        vm.SelectedItem = vm.Items[2];
        Assert.IsFalse(vm.MoveDownCommand.CanExecute(null));
        vm.MoveUpCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { first, third, second }, vm.Items.Select(item => item.SourcePath).ToArray());
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual("first", File.ReadAllText(Path.Combine(_directory, "001.txt")));
        Assert.AreEqual("third", File.ReadAllText(Path.Combine(_directory, "002.txt")));
        Assert.AreEqual("second", File.ReadAllText(Path.Combine(_directory, "003.txt")));
        vm.ClearCommand.Execute(null);
        Assert.IsFalse(vm.MoveUpCommand.CanExecute(null));
        Assert.IsFalse(vm.MoveDownCommand.CanExecute(null));
    }

    [TestMethod]
    [DataRow(BatchRenameSortMode.Name, false, "a3.txt", "z2.png", "z10.txt")]
    [DataRow(BatchRenameSortMode.Name, true, "z10.txt", "z2.png", "a3.txt")]
    [DataRow(BatchRenameSortMode.Extension, false, "z2.png", "a3.txt", "z10.txt")]
    [DataRow(BatchRenameSortMode.Extension, true, "z10.txt", "a3.txt", "z2.png")]
    [DataRow(BatchRenameSortMode.Size, false, "z2.png", "a3.txt", "z10.txt")]
    [DataRow(BatchRenameSortMode.Size, true, "z10.txt", "a3.txt", "z2.png")]
    [DataRow(BatchRenameSortMode.ModifiedTime, false, "z2.png", "a3.txt", "z10.txt")]
    [DataRow(BatchRenameSortMode.ModifiedTime, true, "z10.txt", "a3.txt", "z2.png")]
    [DataRow(BatchRenameSortMode.CreatedTime, false, "a3.txt", "z10.txt", "z2.png")]
    [DataRow(BatchRenameSortMode.CreatedTime, true, "z2.png", "z10.txt", "a3.txt")]
    public async Task SortMode_MetadataAndDirection_OrdersAndRenumbersFiles(BatchRenameSortMode mode,
        bool descending, string firstName, string secondName, string thirdName)
    {
        var first = CreateFile("z10.txt", "1234567890");
        var second = CreateFile("z2.png", "12");
        var third = CreateFile("a3.txt", "123");
        var date = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(first, date.AddDays(1));
        File.SetCreationTimeUtc(second, date.AddDays(2));
        File.SetCreationTimeUtc(third, date);
        File.SetLastWriteTimeUtc(first, date.AddDays(2));
        File.SetLastWriteTimeUtc(second, date);
        File.SetLastWriteTimeUtc(third, date.AddDays(1));
        using var vm = new BatchRenameViewModel(new Ffmpeg()) { KeepOriginalName = false };
        await vm.AddPathsAsync([first, second, third]);
        var selected = vm.Items[1];
        vm.SelectedItem = selected;
        vm.SortDescending = descending;
        vm.SortMode = mode;
        Assert.AreSame(selected, vm.SelectedItem);
        CollectionAssert.AreEqual(new[] { firstName, secondName, thirdName }, vm.Items.Select(item => item.FileName).ToArray());
        for (var index = 0; index < vm.Items.Count; index++)
            Assert.AreEqual($"{index + 1:000}{Path.GetExtension(vm.Items[index].SourcePath)}", vm.Items[index].NewName);
        Assert.IsTrue(vm.CanStart, vm.Message);
        vm.SortDescending = !descending;
        CollectionAssert.AreEqual(new[] { thirdName, secondName, firstName }, vm.Items.Select(item => item.FileName).ToArray());
    }

    [TestMethod]
    public async Task MoveCommands_SortedList_SwitchesToManualAndPreservesOrderOnImport()
    {
        var first = CreateFile("photo10.txt");
        var second = CreateFile("photo2.txt");
        using var vm = new BatchRenameViewModel(new Ffmpeg()) { SortMode = BatchRenameSortMode.Name };
        await vm.AddPathsAsync([first, second]);
        CollectionAssert.AreEqual(new[] { second, first }, vm.Items.Select(item => item.SourcePath).ToArray());
        var third = CreateFile("photo1.txt");
        await vm.AddPathsAsync([third]);
        CollectionAssert.AreEqual(new[] { third, second, first }, vm.Items.Select(item => item.SourcePath).ToArray());
        vm.SelectedItem = vm.Items[2];
        vm.MoveUpCommand.Execute(null);
        Assert.AreEqual(BatchRenameSortMode.Manual, vm.SortMode);
        var fourth = CreateFile("photo0.txt");
        await vm.AddPathsAsync([fourth]);
        CollectionAssert.AreEqual(new[] { third, first, second, fourth }, vm.Items.Select(item => item.SourcePath).ToArray());
        Assert.AreEqual("photo0_004.txt", vm.Items[3].NewName);
    }

    [TestMethod]
    public async Task SortMode_MissingFile_PreservesListAndReportsError()
    {
        var first = CreateFile("first.txt");
        var second = CreateFile("second.txt");
        using var vm = new BatchRenameViewModel(new Ffmpeg());
        await vm.AddPathsAsync([first, second]);
        File.Delete(second);
        vm.SortMode = BatchRenameSortMode.Size;
        CollectionAssert.AreEqual(new[] { first, second }, vm.Items.Select(item => item.SourcePath).ToArray());
        Assert.IsFalse(string.IsNullOrEmpty(vm.Message));
    }

    [TestMethod]
    public async Task SortMode_AfterRenaming_PreservesCompletedPathsAndUsesOutputMetadata()
    {
        var first = CreateFile("first.txt", "long content");
        var second = CreateFile("second.txt", "short");
        using var vm = new BatchRenameViewModel(new Ffmpeg()) { KeepOriginalName = false };
        await vm.AddPathsAsync([first, second]);
        vm.SelectedItem = vm.Items[1];
        vm.Items[0].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(BatchRenameItem.Status) || !vm.Items[0].IsRunning) return;
            Assert.IsFalse(vm.CanEdit);
            Assert.IsFalse(vm.MoveUpCommand.CanExecute(null));
            Assert.IsFalse(vm.MoveDownCommand.CanExecute(null));
        };
        await vm.StartCommand.ExecuteAsync(null);
        var firstItem = vm.Items[0];
        var secondItem = vm.Items[1];
        var firstOutput = firstItem.OutputPath;
        var secondOutput = secondItem.OutputPath;
        vm.SortMode = BatchRenameSortMode.Size;
        Assert.AreSame(secondItem, vm.Items[0]);
        Assert.AreSame(firstItem, vm.Items[1]);
        Assert.AreEqual(firstOutput, firstItem.OutputPath);
        Assert.AreEqual(secondOutput, secondItem.OutputPath);
        Assert.IsTrue(vm.Items.All(item => item.IsCompleted));
        Assert.IsTrue(string.IsNullOrEmpty(vm.Message), vm.Message);
    }

    [TestMethod]
    public async Task StartAsync_UnchangedAndCaseOnlyNames_PreservesFiles()
    {
        var source = CreateFile("source.txt", "content");
        using var vm = new BatchRenameViewModel(new Ffmpeg()) { AddNumber = false };
        await vm.AddPathsAsync([source]);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(BatchRenameStatus.Unchanged, vm.Items.Single().Status);
        vm.ClearCommand.Execute(null);
        vm.BaseName = "SOURCE";
        await vm.AddPathsAsync([source]);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(BatchRenameStatus.Completed, vm.Items.Single().Status, vm.Items.Single().Error);
        Assert.AreEqual("SOURCE.txt", Path.GetFileName(Directory.GetFiles(_directory).Single()));
        Assert.AreEqual("content", File.ReadAllText(vm.Items.Single().OutputPath!));
    }

    [TestMethod]
    public async Task StartAsync_CancelAfterFirstFile_PreservesCompletedAndRemainingFiles()
    {
        var first = CreateFile("a.txt");
        var second = CreateFile("b.txt");
        using var vm = new BatchRenameViewModel(new Ffmpeg());
        await vm.AddPathsAsync([first, second]);
        vm.Items[0].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BatchRenameItem.Status) && vm.Items[0].IsCompleted)
                vm.StartCancelCommand.Execute(null);
        };
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(BatchRenameStatus.Completed, vm.Items[0].Status);
        Assert.AreEqual(BatchRenameStatus.Cancelled, vm.Items[1].Status);
        Assert.IsTrue(File.Exists(vm.Items[0].OutputPath));
        Assert.IsTrue(File.Exists(second));
        Assert.IsFalse(vm.IsBusy);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(BatchRenameStatus.Completed, vm.Items[1].Status);
    }

    [TestMethod]
    public async Task StartAsync_ImportDuringRun_WaitsAndKeepsCompletedItems()
    {
        using var vm = new BatchRenameViewModel(new Ffmpeg());
        await vm.AddPathsAsync([CreateFile("a.txt")]);
        var run = vm.StartCommand.ExecuteAsync(null);
        var import = vm.AddPathsAsync([CreateFile("b.txt")]);
        await Task.WhenAll(run, import);
        Assert.AreEqual(BatchRenameStatus.Completed, vm.Items[0].Status);
        Assert.AreEqual("b_002.txt", vm.Items[1].NewName);
        Assert.IsTrue(vm.CanStart);
    }

    [TestMethod]
    [DataRow(ImageCompressionFormat.WebP, true)]
    [DataRow(ImageCompressionFormat.JPEG, true)]
    [DataRow(ImageCompressionFormat.PNG, true)]
    [DataRow(ImageCompressionFormat.WebP, false)]
    [DataRow(ImageCompressionFormat.JPEG, false)]
    [DataRow(ImageCompressionFormat.PNG, false)]
    public async Task StartAsync_ImageCompression_UsesPreviewNameAndFormatAndPreservesOriginal(ImageCompressionFormat format, bool keepOriginalName)
    {
        var source = CreateImage("photo.png");
        var original = SHA256.HashData(File.ReadAllBytes(source));
        var text = CreateFile("notes.txt", "notes");
        using var vm = new BatchRenameViewModel(new Ffmpeg())
        {
            Prefix = "pre-", Suffix = "-done", KeepOriginalName = keepOriginalName,
            CompressImages = true, Format = format, Lossless = false,
            Quality = 50, ResizePercent = 50, OutputDirectory = Path.Combine(_directory, "output")
        };
        await vm.AddPathsAsync([source, text]);
        Assert.IsTrue(vm.CanStart, vm.Message);
        var preview = vm.Items[0].TargetPath;
        Assert.AreEqual(keepOriginalName ? "pre-photo_001-done" : "pre-001-done", Path.GetFileNameWithoutExtension(preview));
        await vm.StartCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.Items.All(item => item.IsCompleted), string.Join(";", vm.Items.Select(item => item.Error)));
        Assert.AreEqual(preview, vm.Items[0].OutputPath);
        CollectionAssert.AreEqual(original, SHA256.HashData(File.ReadAllBytes(source)));
        using var image = SKBitmap.Decode(preview);
        Assert.IsNotNull(image);
        Assert.AreEqual(32, image.Width);
        Assert.AreEqual(24, image.Height);
        Assert.AreEqual("notes", File.ReadAllText(vm.Items[1].OutputPath!));
        Assert.IsFalse(File.Exists(text));
        Assert.AreEqual(2, Directory.GetFiles(vm.OutputDirectory).Length);
    }

    [TestMethod]
    public async Task StartAsync_CompressionOutputMatchesIntermediateName_DoesNotDeleteResult()
    {
        var source = CreateImage("photo.png");
        using var vm = new BatchRenameViewModel(new Ffmpeg())
        {
            Suffix = "_compressed", AddNumber = false, CompressImages = true, ResizePercent = 50
        };
        await vm.AddPathsAsync([source]);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(BatchRenameStatus.Completed, vm.Items[0].Status, vm.Items[0].Error);
        Assert.IsTrue(File.Exists(vm.Items[0].OutputPath));
        Assert.IsTrue(File.Exists(source));
    }

    [TestMethod]
    public async Task StartAsync_TinyImageConvertsToAnotherFormat_DoesNotCopyWrongEncoding()
    {
        var source = CreateImage("tiny.png", 1, 1);
        using var vm = new BatchRenameViewModel(new Ffmpeg()) { CompressImages = true, Format = ImageCompressionFormat.JPEG };
        await vm.AddPathsAsync([source]);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(BatchRenameStatus.Completed, vm.Items[0].Status, vm.Items[0].Error);
        using var codec = SKCodec.Create(vm.Items[0].OutputPath!);
        Assert.AreEqual(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
    }

    [TestMethod]
    public async Task StartAsync_InvalidImage_ContinuesAndRetriesAfterRefresh()
    {
        var source = CreateFile("broken.png");
        using var vm = new BatchRenameViewModel(new Ffmpeg()) { CompressImages = true };
        await vm.AddPathsAsync([source, CreateFile("notes.txt")]);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(BatchRenameStatus.Failed, vm.Items[0].Status);
        Assert.IsTrue(vm.Items[1].IsCompleted);
        Assert.IsTrue(File.Exists(source));
        Assert.AreEqual(0, Directory.GetFiles(_directory, ".kitopia-*").Length);
        CreateImage("broken.png");
        vm.RefreshPreviewCommand.Execute(null);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.Items[0].IsCompleted, vm.Items[0].Error);
        Assert.AreEqual(3, Directory.GetFiles(_directory).Length);
    }

    [TestMethod]
    public async Task StartAsync_TargetCreatedDuringCompression_PreservesBothFilesAndCleansIntermediate()
    {
        var source = CreateImage("photo.png");
        var original = SHA256.HashData(File.ReadAllBytes(source));
        using var vm = new BatchRenameViewModel(new Ffmpeg())
        {
            BaseName = "result", CompressImages = true, ResizePercent = 50
        };
        await vm.AddPathsAsync([source]);
        vm.Items[0].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BatchRenameItem.Status) && vm.Items[0].IsRunning)
                File.WriteAllText(vm.Items[0].TargetPath, "external file");
        };
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(BatchRenameStatus.Failed, vm.Items[0].Status);
        Assert.AreEqual("external file", File.ReadAllText(vm.Items[0].TargetPath));
        CollectionAssert.AreEqual(original, SHA256.HashData(File.ReadAllBytes(source)));
        Assert.AreEqual(2, Directory.GetFiles(_directory).Length);
    }

    [TestMethod]
    public void AnalyzeInputData_FilesAndFolders_OffersOneBatchAction()
    {
        var source = CreateFile("file.txt");
        var analyzer = new BatchRenameAnalyzer();
        var actions = analyzer.AnalyzeInputData(new[] { source, source, _directory, Path.Combine(_directory, "missing") }
            .Select(path => new InputData { InputType = InputType.文件, Data = path })).ToArray();
        Assert.AreEqual(1, actions.Length);
        Assert.IsTrue(actions[0].ShowAsMiniApp);
        Assert.IsNotNull(actions[0].Action);
        Assert.AreEqual(0, analyzer.AnalyzeInputData([]).Count());
    }

    private string CreateFile(string name, string content = "content")
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateImage(string name, int width = 64, int height = 48)
    {
        var path = Path.Combine(_directory, name);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        data.SaveTo(output);
        return path;
    }
}
