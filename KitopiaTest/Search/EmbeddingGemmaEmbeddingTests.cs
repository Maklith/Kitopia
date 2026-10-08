using System.Reflection;
using System.Text.Json;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Search.Semantic;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore.Config;

namespace KitopiaTest.Search;

[TestClass]
[DoNotParallelize]
public sealed class EmbeddingGemmaEmbeddingTests
{
    private string _referenceDirectory = null!;
    private JsonDocument _reference = null!;
    private EmbeddingGemmaEmbeddingService _service = null!;
    private Dictionary<string, ConfigBase> _originalConfigs = null!;

    [TestInitialize]
    public void Initialize()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kitopia.sln"))) root = root.Parent;
        Assert.IsNotNull(root);
        var models = Path.Combine(root.FullName, "Kitopia.Desktop.Features", "Assets", "EmbeddingGemma2");
        Assert.IsTrue(EmbeddingGemmaModelPackage.IsComplete(models), "The bundled Q4 graphs and external weight shards must be present.");
        _referenceDirectory = Path.Combine(root.FullName, "KitopiaTest", "Search", "EmbeddingGemmaReference");
        _reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(_referenceDirectory, "reference.json")));
        _originalConfigs = ConfigManger.Configs;
        ConfigManger.Configs = new Dictionary<string, ConfigBase>
        {
            ["KitopiaConfig"] = new KitopiaConfig
            {
                Name = "KitopiaConfig",
                OnnxTargetDevices = new Dictionary<string, string>
                {
                    [EmbeddingGemmaModelPackage.TextModelSignName] = "EG2-Test-CPU",
                    [EmbeddingGemmaModelPackage.VisionModelSignName] = "EG2-Test-CPU"
                }
            }
        };
        PluginOverall.OnnxRuntimes.Add("EG2-Test", new() { ["EG2-Test-CPU"] = () => new OnnxRuntime.CPU.MInferenceSession() });
        _service = new EmbeddingGemmaEmbeddingService(models);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _service?.Dispose();
        _reference?.Dispose();
        PluginOverall.OnnxRuntimes.Remove("EG2-Test");
        if (_originalConfigs is not null) ConfigManger.Configs = _originalConfigs;
    }

    [TestMethod]
    public void EncodeInput_MultilingualAndTruncatedInputs_MatchesOfficialTokenizer()
    {
        foreach (var row in _reference.RootElement.GetProperty("texts").EnumerateArray())
        {
            var expected = row.GetProperty("ids").EnumerateArray().Select(value => value.GetInt64()).ToArray();
            CollectionAssert.AreEqual(expected, _service.EncodeInput(row.GetProperty("text").GetString()!, row.GetProperty("limit").GetInt32()));
        }
        Assert.AreEqual("title: none | text: body", EmbeddingGemmaEmbeddingService.FormatDocument("body"));
        Assert.AreEqual("title: name | text: body", EmbeddingGemmaEmbeddingService.FormatDocument("body", "name"));
    }

    [TestMethod]
    public async Task EmbedAsync_PaddedBatchAndSessionReload_MatchesOfficialQ4Vectors()
    {
        var rows = _reference.RootElement.GetProperty("texts").EnumerateArray().ToArray();
        var texts = rows.Take(4).Select(row => row.GetProperty("text").GetString()!).ToArray();
        var vectors = await _service.EmbedAsync(texts, 8192, CancellationToken.None);
        for (var index = 0; index < vectors.Count; index++) AssertVector(rows[index], vectors[index], 0.999999);
        var truncated = await _service.EmbedAsync([rows[4].GetProperty("text").GetString()!], 24, CancellationToken.None);
        AssertVector(rows[4], truncated[0], 0.999999);
        await _service.ReleaseSessionsAsync();
        var reloaded = await _service.EmbedAsync([texts[0]], 512, CancellationToken.None);
        AssertVector(rows[0], reloaded[0], 0.999999);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.EmbedAsync(texts, 512, canceled.Token));
    }

    [TestMethod]
    public async Task EmbedDocumentAsync_LongDocument_RetainsIndependentChunkVectors()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-eg2-chunks-{Guid.NewGuid():N}.md");
        try
        {
            var firstParagraph = string.Concat(Enumerable.Repeat("Apples grow in orchards. ", 600));
            var secondParagraph = string.Concat(Enumerable.Repeat("Satellites orbit the Earth. ", 200));
            await File.WriteAllTextAsync(path, firstParagraph + "\n\n" + secondParagraph);
            Assert.IsTrue(DocumentTextExtractor.TryCreateSource(path, out var source));
            var method = typeof(IndexService).GetMethod("EmbedDocumentAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
            var task = (Task<IReadOnlyList<float[]>>)method.Invoke(null, [source, _service, CancellationToken.None])!;
            var vectors = await task;
            Assert.HasCount(2, vectors);
            foreach (var vector in vectors)
                Assert.AreEqual(1d, Math.Sqrt(vector.Sum(value => (double)value * value)), 1e-6);

            var expected = (await _service.EmbedAsync(
                [EmbeddingGemmaEmbeddingService.FormatDocument(firstParagraph.Trim(), Path.GetFileName(path))],
                EmbeddingGemmaEmbeddingService.DocumentMaximumTokens, CancellationToken.None))[0];
            Assert.IsTrue(vectors[0].Zip(expected, (left, right) => (double)left * right).Sum() > 0.99999);
            Assert.IsTrue(vectors[0].Zip(vectors[1], (left, right) => (double)left * right).Sum() < 0.999);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task EmbedImagesAsync_PatchOrderPaddingAndVectors_MatchesOfficialProcessor()
    {
        foreach (var row in _reference.RootElement.GetProperty("images").EnumerateArray())
        {
            var path = Path.Combine(_referenceDirectory, row.GetProperty("file").GetString()!);
            var (pixels, positions, softTokens) = EmbeddingGemmaImageProcessor.Process(path);
            Assert.AreEqual(row.GetProperty("soft_tokens").GetInt32(), softTokens);
            var expectedPositions = row.GetProperty("positions").EnumerateArray()
                .SelectMany(position => position.EnumerateArray().Select(value => value.GetInt64())).ToArray();
            CollectionAssert.AreEqual(expectedPositions, positions);
            var offsets = row.GetProperty("pixel_offsets").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            var expectedPixels = row.GetProperty("pixel_values").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            var maximumDifference = offsets.Select((offset, index) => Math.Abs(pixels[offset] - expectedPixels[index])).Max();
            Assert.IsTrue(maximumDifference <= 2f / 255f, $"Pixel difference: {maximumDifference}");
            Assert.IsTrue(pixels.AsSpan(softTokens * 9 * 768).ToArray().All(value => value == 0));
            var vector = (await _service.EmbedImagesAsync([path], CancellationToken.None))[0];
            AssertVector(row, vector, 0.999);
        }
    }

    [TestMethod]
    [DataRow(1, 100000, 48, 13440)]
    [DataRow(100000, 1, 13440, 48)]
    [DataRow(800, 800, 768, 768)]
    public void GetTargetSize_ExtremeAspectRatios_StaysWithinOfficialPatchBudget(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.AreEqual((expectedWidth, expectedHeight), EmbeddingGemmaImageProcessor.GetTargetSize(width, height));
    }

    private static void AssertVector(JsonElement row, float[] vector, double minimumCosine)
    {
        Assert.HasCount(768, vector);
        var expected = row.GetProperty("vector").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        var norm = Math.Sqrt(vector.Sum(value => (double)value * value));
        var cosine = vector.Select((value, index) => value * expected[index]).Sum() / norm;
        Assert.AreEqual(1d, norm, 1e-6);
        Assert.IsTrue(cosine >= minimumCosine, $"Embedding cosine: {cosine}");
    }
}
