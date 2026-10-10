using System.Reflection;
using System.Collections.Concurrent;
using System.Text.Json;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.Semantic;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using Microsoft.Data.Sqlite;
using OpenCvSharp;
using PluginCore.Config;
using PluginCore.Onnx;
using FileType = PluginCore.FileType;

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
    public async Task EmbedAsync_OfficialNpuPlugin_MatchesTextReferencesAcrossShapes()
    {
        using var runtime = new OnnxRuntime.OpenVino.OnnxRuntimeGpuWin();
        using (var probe = new OnnxRuntime.OpenVino.NPUOVInferenceSession(runtime))
        {
            try { Assert.AreEqual(true, probe.CheckAvailability()); }
            catch (InvalidOperationException exception) when (
                exception.Message == "OpenVINO did not report an available NPU device.")
            {
                Assert.Inconclusive("An Intel NPU is required for this model test.");
            }
        }
        PluginOverall.OnnxRuntimes["EG2-Test"]["NPU(OpenVino)"] =
            () => new OnnxRuntime.OpenVino.NPUOVInferenceSession(runtime);
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "NPU(OpenVino)";
        try
        {
            var rows = _reference.RootElement.GetProperty("texts").EnumerateArray().Take(2).ToArray();
            foreach (var row in rows)
            {
                var vectors = await _service.EmbedAsync([row.GetProperty("text").GetString()!], 512, CancellationToken.None);
                AssertVector(row, vectors[0], 0.9999);
            }
            var batch = await _service.EmbedAsync(Enumerable.Range(0, 8)
                    .Select(index => rows[index % rows.Length].GetProperty("text").GetString()!).ToArray(),
                512, CancellationToken.None);
            for (var index = 0; index < batch.Count; index++) AssertVector(rows[index % rows.Length], batch[index], 0.9999);
            var longTexts = new[]
            {
                string.Concat(Enumerable.Repeat("Apples grow in orchards. ", 200)),
                string.Concat(Enumerable.Repeat("Satellites orbit the Earth. ", 200)),
                string.Concat(Enumerable.Repeat("This document describes a software project. ", 200)),
                string.Concat(Enumerable.Repeat("Flowers grow in the garden. ", 200))
            };
            ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "EG2-Test-CPU";
            var expected = await _service.EmbedAsync(longTexts, 1024, CancellationToken.None);
            ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "NPU(OpenVino)";
            var concurrent = await _service.EmbedAsync(longTexts, 1024, CancellationToken.None);
            for (var index = 0; index < expected.Count; index++)
            {
                var cosine = concurrent[index].Zip(expected[index], (left, right) => (double)left * right).Sum();
                Assert.IsTrue(cosine > 0.9999, $"Long text {index}: cosine {cosine}.");
            }
        }
        finally { await _service.ReleaseSessionsAsync(); }
    }

    [TestMethod]
    public async Task EmbedAsync_Npu_BindsFixedShapesPadsUnusedModalitiesAndReusesMatchingSession()
    {
        var sessions = new List<SchedulingSession>();
        PluginOverall.OnnxRuntimes["EG2-Test"]["NPU(OpenVino)"] = () =>
        {
            var session = new SchedulingSession { Device = "NPU(OpenVino)" };
            sessions.Add(session);
            return session;
        };
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "NPU(OpenVino)";

        await _service.EmbedAsync(["apple"], 512, CancellationToken.None);
        await _service.EmbedAsync(["apple"], 512, CancellationToken.None);
        Assert.HasCount(1, sessions);
        Assert.HasCount(2, sessions[0].Batches);
        Assert.IsFalse(sessions[0].IsDisposed);
        Assert.AreEqual(1L, sessions[0].FreeDimensionOverrides!["batch_size"]);
        Assert.AreEqual((long)sessions[0].Batches.First().Length, sessions[0].FreeDimensionOverrides!["sequence_length"]);
        Assert.AreEqual(1024L, sessions[0].FreeDimensionOverrides["sequence_length"]);
        Assert.AreEqual(280L, sessions[0].FreeDimensionOverrides["num_image_tokens"]);
        foreach (var name in new[] { "num_video_tokens", "num_audio_tokens" })
            Assert.AreEqual(1L, sessions[0].FreeDimensionOverrides![name]);
        Assert.IsTrue(sessions[0].FeatureLengths.All(lengths => lengths == (280 * 512, 512, 512)));

        await _service.EmbedAsync(["apple", "apple"], 512, CancellationToken.None);
        Assert.HasCount(1, sessions);
        Assert.HasCount(4, sessions[0].Batches);
        Assert.IsTrue(sessions[0].Batches.All(batch => batch.Count == 1));
        Assert.IsFalse(sessions[0].IsDisposed);

        await _service.EmbedAsync([string.Concat(Enumerable.Repeat(" apple", 20))], 512, CancellationToken.None);
        Assert.HasCount(1, sessions);
        Assert.HasCount(5, sessions[0].Batches);
        await _service.EmbedAsync([string.Concat(Enumerable.Repeat(" apple", 1200))], 8192, CancellationToken.None);
        Assert.HasCount(2, sessions);
        Assert.IsTrue(sessions[0].IsDisposed);
        Assert.IsFalse(sessions[1].IsDisposed);
        Assert.AreEqual(2048L, sessions[1].FreeDimensionOverrides!["sequence_length"]);
        await _service.EmbedAsync(["apple"], 512, CancellationToken.None);
        Assert.HasCount(3, sessions);
        Assert.IsTrue(sessions[1].IsDisposed);
        Assert.IsFalse(sessions[2].IsDisposed);
        Assert.HasCount(1, sessions[2].Batches);
        await _service.EmbedAsync([string.Concat(Enumerable.Repeat(" apple", 2500))], 8192, CancellationToken.None);
        Assert.HasCount(4, sessions);
        Assert.IsTrue(sessions[2].IsDisposed);
        Assert.IsFalse(sessions[3].IsDisposed);
        await _service.ReleaseSessionsAsync();
        Assert.IsTrue(sessions.All(session => session.IsDisposed));
    }

    [TestMethod]
    public async Task EmbedAsync_NpuPadding_TextAndDifferentImageSizes_MatchReferencesWithOneTextSession()
    {
        var creations = 0;
        // Execute the real graph on CPU to isolate padding correctness from NPU compilation.
        PluginOverall.OnnxRuntimes["EG2-Test"]["NPU(OpenVino)"] = () =>
        {
            creations++;
            return new OnnxRuntime.CPU.MInferenceSession();
        };
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "NPU(OpenVino)";
        var texts = _reference.RootElement.GetProperty("texts").EnumerateArray().Take(2).ToArray();
        foreach (var row in texts)
        {
            var vector = (await _service.EmbedAsync([row.GetProperty("text").GetString()!], 512, CancellationToken.None))[0];
            AssertVector(row, vector, 0.999999);
        }
        foreach (var row in _reference.RootElement.GetProperty("images").EnumerateArray())
        {
            var path = Path.Combine(_referenceDirectory, row.GetProperty("file").GetString()!);
            var vector = (await _service.EmbedImagesAsync([path], CancellationToken.None))[0];
            AssertVector(row, vector, 0.999);
        }
        Assert.AreEqual(1, creations, "Text and all image sizes must share the same bounded text session.");
    }

    [TestMethod]
    public async Task EmbedImagesAsync_Npu_BindsVisionShapesAndReusesSession()
    {
        var row = _reference.RootElement.GetProperty("images")[0];
        var featureLength = row.GetProperty("soft_tokens").GetInt32() * 512;
        var path = Path.Combine(_referenceDirectory, row.GetProperty("file").GetString()!);
        using var vision = new SchedulingSession { Device = "NPU(OpenVino)", OutputLength = 280 * 512 };
        using var text = new SchedulingSession();
        var visionCreations = 0;
        PluginOverall.OnnxRuntimes["EG2-Test"]["NPU(OpenVino)"] = () =>
        {
            visionCreations++;
            return vision;
        };
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => text;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.VisionModelSignName] = "NPU(OpenVino)";

        var vectors = await _service.EmbedImagesAsync([path, path], CancellationToken.None);

        Assert.HasCount(2, vectors);
        Assert.AreEqual(1, visionCreations);
        Assert.AreEqual(1L, vision.FreeDimensionOverrides!["s11"]);
        Assert.AreEqual((long)EmbeddingGemmaImageProcessor.MaximumPatches, vision.FreeDimensionOverrides!["s35"]);
        Assert.AreEqual(280L, vision.FreeDimensionOverrides!["u0"]);
        Assert.HasCount(2, vision.Batches);
        Assert.IsTrue(vision.Batches.All(batch => batch == (1, EmbeddingGemmaImageProcessor.MaximumPatches)));
        Assert.IsTrue(text.ImageFeatureLengths.All(length => length == featureLength));
        await _service.ReleaseSessionsAsync();
        Assert.IsTrue(vision.IsDisposed);
        Assert.IsTrue(text.IsDisposed);
    }

    [TestMethod]
    public async Task EmbedImagesAsync_BoundedNpuGraph_MatchesImageReferencesAndSharesTextSession()
    {
        var creations = 0;
        PluginOverall.OnnxRuntimes["EG2-Test"]["NPU(OpenVino)"] = () =>
        {
            creations++;
            return new OnnxRuntime.CPU.MInferenceSession();
        };
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.VisionModelSignName] = "NPU(OpenVino)";
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "NPU(OpenVino)";
        foreach (var row in _reference.RootElement.GetProperty("images").EnumerateArray())
        {
            var path = Path.Combine(_referenceDirectory, row.GetProperty("file").GetString()!);
            var vector = (await _service.EmbedImagesAsync([path], CancellationToken.None))[0];
            AssertVector(row, vector, 0.999);
        }
        var text = _reference.RootElement.GetProperty("texts")[0];
        var result = await _service.EmbedAsync([text.GetProperty("text").GetString()!], 512, CancellationToken.None);
        AssertVector(text, result[0], 0.999999);
        Assert.AreEqual(2, creations, "All images and text must reuse one vision and one text session.");
    }

    [TestMethod]
    [TestCategory("NPU")]
    public async Task EmbedImagesAsync_OfficialNpuPlugin_MatchesImageReferencesAcrossShapes()
    {
        using var runtime = new OnnxRuntime.OpenVino.OnnxRuntimeGpuWin();
        using (var probe = new OnnxRuntime.OpenVino.NPUOVInferenceSession(runtime))
        {
            try { Assert.AreEqual(true, probe.CheckAvailability()); }
            catch (InvalidOperationException exception) when (
                exception.Message == "OpenVINO did not report an available NPU device.")
            {
                Assert.Inconclusive("An Intel NPU is required for this image model test.");
            }
        }
        PluginOverall.OnnxRuntimes["EG2-Test"]["NPU(OpenVino)"] =
            () => new OnnxRuntime.OpenVino.NPUOVInferenceSession(runtime);
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.VisionModelSignName] = "NPU(OpenVino)";
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "NPU(OpenVino)";
        try
        {
            var rows = _reference.RootElement.GetProperty("images").EnumerateArray().ToArray();
            var paths = rows.Select(row => Path.Combine(_referenceDirectory, row.GetProperty("file").GetString()!)).ToArray();
            var vectors = await _service.EmbedImagesAsync(paths, CancellationToken.None);
            for (var index = 0; index < rows.Length; index++) AssertVector(rows[index], vectors[index], 0.999);
            var cacheDirectory = Path.Combine(Path.GetTempPath(), "Kitopia", "openvino-npu-cache");
            var cached = Directory.GetFiles(cacheDirectory, "*.blob").ToDictionary(file => file, File.GetLastWriteTimeUtc);
            await _service.ReleaseSessionsAsync();
            var repeated = await _service.EmbedImagesAsync([paths[0]], CancellationToken.None);
            AssertVector(rows[0], repeated[0], 0.999);
            var changed = Directory.GetFiles(cacheDirectory, "*.blob").Where(file =>
                !cached.TryGetValue(file, out var time) || time != File.GetLastWriteTimeUtc(file)).ToArray();
            Assert.HasCount(0, changed, $"Reload compiled new graphs: {string.Join(", ", changed)}.");
        }
        finally { await _service.ReleaseSessionsAsync(); }
    }

    [TestMethod]
    public async Task EmbedAsync_Npu_LongRequestsOverlapAndReleaseWaitsForAll()
    {
        using var started = new CountdownEvent(4);
        using var resume = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var probe = new SchedulingSession
        {
            Device = "NPU(OpenVino)",
            BeforeInference = _ =>
            {
                started.Signal();
                resume.Wait(cancellation.Token);
            }
        };
        PluginOverall.OnnxRuntimes["EG2-Test"]["NPU(OpenVino)"] = () => probe;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "NPU(OpenVino)";
        var text = string.Concat(Enumerable.Repeat(" apple", 800));
        var inference = _service.EmbedAsync([text, text, text, text], 1024, cancellation.Token);
        try
        {
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(10)), "Four batch=1 requests must overlap.");
            var release = _service.ReleaseSessionsAsync();
            Assert.IsFalse(release.IsCompleted);
            Assert.IsFalse(probe.IsDisposed);
            resume.Set();
            var vectors = await inference;
            await release;
            Assert.HasCount(4, vectors);
            Assert.HasCount(4, probe.Batches);
            Assert.IsTrue(probe.Batches.All(batch => batch == (1, 1024)));
            Assert.IsTrue(probe.IsDisposed);
        }
        finally
        {
            resume.Set();
            await inference;
        }
    }

    [TestMethod]
    public async Task EmbedAsync_Npu_CancellationStopsConcurrentRequestsAndAllowsRelease()
    {
        using var started = new CountdownEvent(4);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var probe = new SchedulingSession
        {
            Device = "NPU(OpenVino)",
            BeforeInference = _ =>
            {
                started.Signal();
                cancellation.Token.WaitHandle.WaitOne();
                cancellation.Token.ThrowIfCancellationRequested();
            }
        };
        PluginOverall.OnnxRuntimes["EG2-Test"]["NPU(OpenVino)"] = () => probe;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "NPU(OpenVino)";
        var inference = _service.EmbedAsync(["apple", "apple", "apple", "apple"], 512, cancellation.Token);
        try
        {
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(10)));
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => inference);
            await _service.ReleaseSessionsAsync();
            Assert.IsTrue(probe.IsDisposed);
        }
        finally
        {
            cancellation.Cancel();
            try { await inference; }
            catch (OperationCanceledException) { }
        }
    }

    [TestMethod]
    public async Task EmbedAsync_Npu_BackendChanged_DisposesBothCachedSessions()
    {
        var sessions = new List<SchedulingSession>();
        PluginOverall.OnnxRuntimes["EG2-Test"]["NPU(OpenVino)"] = () =>
        {
            var session = new SchedulingSession { Device = "NPU(OpenVino)" };
            sessions.Add(session);
            return session;
        };
        using var cpu = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => cpu;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "NPU(OpenVino)";
        await _service.EmbedAsync(["apple"], 512, CancellationToken.None);
        await _service.EmbedAsync([string.Concat(Enumerable.Repeat(" apple", 1200))], 8192, CancellationToken.None);
        Assert.HasCount(2, sessions);
        Assert.IsTrue(sessions[0].IsDisposed);
        Assert.IsFalse(sessions[1].IsDisposed);
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "EG2-Test-CPU";
        await _service.EmbedAsync(["apple"], 512, CancellationToken.None);
        Assert.IsTrue(sessions.All(session => session.IsDisposed));
        Assert.HasCount(1, cpu.Batches);
    }

    [TestMethod]
    public async Task EmbedAsync_OfficialOpenVinoPlugin_MatchesTextAndImageReferences()
    {
        using var runtime = new OnnxRuntime.OpenVino.OnnxRuntimeGpuWin();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] =
            () => new OnnxRuntime.OpenVino.CPUOVInferenceSession(runtime);
        try
        {
            var rows = _reference.RootElement.GetProperty("texts").EnumerateArray().ToArray();
            var vectors = await _service.EmbedAsync(
                rows.Take(4).Select(row => row.GetProperty("text").GetString()!).ToArray(), 8192, CancellationToken.None);
            for (var index = 0; index < vectors.Count; index++) AssertVector(rows[index], vectors[index], 0.9999);
            var truncated = await _service.EmbedAsync([rows[4].GetProperty("text").GetString()!], 24, CancellationToken.None);
            AssertVector(rows[4], truncated[0], 0.9999);
            foreach (var row in _reference.RootElement.GetProperty("images").EnumerateArray())
            {
                var path = Path.Combine(_referenceDirectory, row.GetProperty("file").GetString()!);
                var vector = (await _service.EmbedImagesAsync([path], CancellationToken.None))[0];
                AssertVector(row, vector, 0.999);
            }
            await _service.ReleaseSessionsAsync();
            var reloaded = await _service.EmbedAsync([rows[0].GetProperty("text").GetString()!], 512, CancellationToken.None);
            AssertVector(rows[0], reloaded[0], 0.9999);
        }
        finally
        {
            await _service.ReleaseSessionsAsync();
        }
    }

    [TestMethod]
    public async Task EmbedDocumentAsync_LongDocument_RetainsIndependentChunkVectors()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-eg2-chunks-{Guid.NewGuid():N}.md");
        try
        {
            var firstParagraph = string.Concat(Enumerable.Repeat("Apples grow in orchards. ", 200));
            var secondParagraph = string.Concat(Enumerable.Repeat("Satellites orbit the Earth. ", 80));
            await File.WriteAllTextAsync(path, firstParagraph + "\n\n" + secondParagraph);
            Assert.IsTrue(DocumentTextExtractor.TryCreateSource(path, out var source));
            var title = Path.GetFileName(path);
            var chunks = new List<string>();
            await foreach (var chunk in DocumentTextExtractor.ExtractChunksAsync(
                               source, text => _service.CountDocumentTokens(text, title), CancellationToken.None))
                chunks.Add(EmbeddingGemmaEmbeddingService.FormatDocument(chunk, title));
            Assert.IsGreaterThan(1, chunks.Count);
            Assert.IsTrue(chunks.All(chunk => _service.EncodeInput(chunk, 8192).Length <= 1024));
            var method = typeof(IndexService).GetMethod("EmbedDocumentAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
            var task = (Task<IReadOnlyList<float[]>>)method.Invoke(null, [source, _service, CancellationToken.None])!;
            var vectors = await task;
            Assert.HasCount(chunks.Count, vectors);
            foreach (var vector in vectors)
                Assert.AreEqual(1d, Math.Sqrt(vector.Sum(value => (double)value * value)), 1e-6);

            var expected = await _service.EmbedAsync(chunks, 1024, CancellationToken.None);
            for (var index = 0; index < vectors.Count; index++)
                Assert.IsTrue(vectors[index].Zip(expected[index], (left, right) => (double)left * right).Sum() > 0.99999);
            Assert.IsTrue(vectors[0].Zip(vectors[^1], (left, right) => (double)left * right).Sum() < 0.999);
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

    [TestMethod]
    public async Task EmbedAsync_LongSequences_BoundsAttentionWithoutTruncatingDocuments()
    {
        using var probe = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        var text = string.Concat(Enumerable.Repeat("Apples grow in orchards. ", 550));
        var length = _service.EncodeInput(text, 8192).Length;
        Assert.IsGreaterThan(2048, length);

        var vectors = await _service.EmbedAsync([text, text], 8192, CancellationToken.None);

        Assert.HasCount(2, vectors);
        CollectionAssert.AreEqual(new[] { (1, length), (1, length) }, probe.Batches.ToArray());
        Assert.AreEqual(Math.Max(1, Environment.ProcessorCount * 50 / 100), probe.WorkerCount);
        Assert.IsTrue(probe.UseCpuMemoryArena);
        Assert.AreEqual(8192L * 1024 * 1024, probe.GpuMemoryLimitBytes);
    }

    [TestMethod]
    public async Task EmbedAsync_QueryQueuedDuringIndexBatch_RunsBeforeNextBatch()
    {
        using var started = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var probe = new SchedulingSession
        {
            BeforeInference = count =>
            {
                if (count != 1) return;
                started.Set();
                resume.Wait(cancellation.Token);
            }
        };
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        var indexing = _service.EmbedAsync(Enumerable.Repeat("Indexed document", 16).ToArray(), 512, cancellation.Token);
        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
        var query = _service.EmbedAsync(["task: search result | query: hello"], 512, cancellation.Token);
        resume.Set();
        await Task.WhenAll(indexing, query);

        CollectionAssert.AreEqual(new[] { 4, 1, 4, 4, 4 }, probe.Batches.Select(batch => batch.Count).ToArray());
    }

    [TestMethod]
    [DataRow(512, 4, 4)]
    [DataRow(1024, 4, 1)]
    public async Task EmbedAsync_IndexingInputs_BoundsBatchAttention(int length, int count, int batchSize)
    {
        using var probe = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        var text = string.Concat(Enumerable.Repeat(" apple", length - 2));
        Assert.AreEqual(length, _service.EncodeInput(text, 1024).Length);

        var vectors = await _service.EmbedAsync(Enumerable.Repeat(text, count).ToArray(), 1024, CancellationToken.None);

        Assert.HasCount(count, vectors);
        Assert.IsTrue(probe.Batches.All(batch => batch.Count == batchSize && batch.Length == length));
    }

    [TestMethod]
    public async Task EmbedAsync_VaryingIndexingLengths_ReusesPaddingShapeAndMasksOnlyRealTokens()
    {
        using var probe = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        var texts = new[] { 513, 750, 1000 }
            .Select(length => string.Concat(Enumerable.Repeat(" apple", length - 2))).ToArray();

        await _service.EmbedAsync(texts, 1024, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { (1, 1024), (1, 1024), (1, 1024) }, probe.Batches.ToArray());
        CollectionAssert.AreEqual(new[] { 513, 750, 1000 }, probe.MaskLengths.ToArray());
        for (var index = 0; index < texts.Length; index++)
        {
            var expected = _service.EncodeInput(texts[index], 1024);
            Assert.IsTrue(probe.InputRows.ElementAt(index).Span[..expected.Length].SequenceEqual(expected));
            Assert.IsTrue(probe.InputRows.ElementAt(index).Span[expected.Length..].IndexOfAnyExcept(0L) < 0);
        }
    }

    [TestMethod]
    [DataRow("Apples grow in orchards. ")]
    [DataRow("\u4e2d\u6587\u6587\u6863\u7d22\u5f15\u68c0\u7d22\u5185\u5bb9 ")]
    public async Task ExtractChunksAsync_MultilingualDocuments_BoundsActualTokensAndPreservesTail(string sentence)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-eg2-token-budget-{Guid.NewGuid():N}.md");
        try
        {
            var text = "begin_marker " + string.Concat(Enumerable.Repeat(sentence, 600)) + " unique_tail";
            await File.WriteAllTextAsync(path, text);
            Assert.IsTrue(DocumentTextExtractor.TryCreateSource(path, out var source));
            var chunks = new List<string>();
            var title = Path.GetFileName(path);
            await foreach (var chunk in DocumentTextExtractor.ExtractChunksAsync(
                               source, content => _service.CountDocumentTokens(content, title), CancellationToken.None))
                chunks.Add(chunk);

            Assert.IsGreaterThan(1, chunks.Count);
            Assert.IsTrue(chunks.All(chunk => _service.CountDocumentTokens(chunk, title) <= 1024));
            StringAssert.StartsWith(chunks[0], "begin_marker");
            StringAssert.EndsWith(chunks[^1], "unique_tail");
            Assert.IsTrue(chunks.Sum(chunk => _service.CountDocumentTokens(chunk, title)) >= _service.CountDocumentTokens(text, title));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IndexFileVectorsAsync_ImageIndex_UsesOnlyImageVectors(bool force)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia-image-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var index = new IndexService();
        var embeddingField = typeof(IndexService).GetField("_embeddingService", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            var source = Path.Combine(_referenceDirectory, _reference.RootElement.GetProperty("images")[0].GetProperty("file").GetString()!);
            var path = Path.Combine(directory, Path.GetFileName(source));
            File.Copy(source, path);
            var info = new FileInfo(path);
            var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path)));
            var vector = new float[768];
            vector[0] = 1;
            var store = new IndexVectorStore(Path.Combine(directory, "index.db"));
            await store.SynchronizeFileSourceAsync(IndexSource.Manual, [path], new HashSet<string>(), CancellationToken.None);
            await store.UpsertImageAsync(path, "current", _service.ModelId, vector, CancellationToken.None);
            await store.UpsertFileStateAsync(new FileIndexState(
                path, IndexFileKind.Image, info.Length, info.LastWriteTimeUtc.Ticks, contentHash, true, "old-ocr"), CancellationToken.None);
            store = new IndexVectorStore(Path.Combine(directory, "index.db"));
            typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index, store);
            embeddingField.SetValue(index, _service);
            ConfigManger.Config.enableSemanticSearch = true;
            var method = typeof(IndexService).GetMethod("IndexFileVectorsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

            await (Task)method.Invoke(index, [false, true, force, CancellationToken.None])!;

            Assert.AreEqual((0, 1), await store.GetCountsAsync(CancellationToken.None));
            Assert.AreEqual(0, index.GetStatus().FailedImages);
            var state = await store.GetFileStateAsync(path, IndexFileKind.Image, CancellationToken.None);
            Assert.IsNotNull(state);
            Assert.AreEqual(contentHash, state.ContentHash);
            Assert.IsFalse(state.OcrCompleted);
            Assert.IsNull(state.OcrModelId);
        }
        finally
        {
            embeddingField.SetValue(index, null);
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    [DataRow(0.9, 0.6, 0.6, false, false)]
    [DataRow(0.8, 0.3, 0.95, false, false)]
    [DataRow(0.9, 0.6, 0.6, true, true)]
    public async Task SearchAsync_LegacyOcrVectors_IgnoreOcrAndPreserveImageAndPinyinRanking(
        double singleSimilarity, double dualImageSimilarity, double ocrSimilarity, bool lexicalMatch, bool dualWins)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia-eg2-fusion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var probe = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        using var index = new IndexService();
        var embeddingField = typeof(IndexService).GetField("_embeddingService", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            var store = new IndexVectorStore(Path.Combine(directory, "index.db"));
            typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index, store);
            embeddingField.SetValue(index, _service);
            var single = Path.Combine(directory, "single.png");
            var dual = Path.Combine(directory, "dual.png");
            index.TryAdd(new SearchEntry { OnlyKey = single, DisplayName = "sample1", FileType = FileType.文件 }, IndexSource.Image);
            index.TryAdd(new SearchEntry { OnlyKey = dual, DisplayName = lexicalMatch ? "people" : "sample2", FileType = FileType.文件 }, IndexSource.Image);
            var similarities = new[] { singleSimilarity, dualImageSimilarity, ocrSimilarity };
            var vectors = new float[similarities.Length][];
            for (var position = 0; position < similarities.Length; position++)
            {
                vectors[position] = new float[EmbeddingGemmaEmbeddingService.VectorDimensions];
                vectors[position][0] = (float)similarities[position];
                vectors[position][1] = (float)Math.Sqrt(1 - similarities[position] * similarities[position]);
            }
            await store.UpsertImageAsync(single, "fixture", _service.ModelId, vectors[0], CancellationToken.None);
            await store.UpsertImageAsync(dual, "fixture", _service.ModelId, vectors[1], CancellationToken.None);
            await store.UpsertTextChunksAsync(dual, _service.ModelId, [vectors[2]], CancellationToken.None, TextContentKind.ImageOcr);
            store = new IndexVectorStore(Path.Combine(directory, "index.db"));
            typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index, store);
            await index.RebuildPinyinSearcherAsync();
            Assert.HasCount(lexicalMatch ? 1 : 0, index.SearchPinyin("people", 10, CancellationToken.None));

            var results = await index.SearchAsync("people", 10, CancellationToken.None);

            Assert.HasCount(2, results);
            Assert.AreEqual(dualWins ? dual : single, results[0].Source.OnlyKey);
            Assert.AreEqual(singleSimilarity / 61, results.Single(result => result.Source.OnlyKey == single).Weight, 1e-6);
            Assert.AreEqual(dualImageSimilarity / 62 + (lexicalMatch ? 1d / 61 : 0),
                results.Single(result => result.Source.OnlyKey == dual).Weight, 1e-6);
            if (lexicalMatch) Assert.IsNotNull(results[0].CharMatchResults);
        }
        finally
        {
            embeddingField.SetValue(index, null);
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SearchAsync_IndexOperationInProgress_SearchesStoredVectorsOnSharedSession(bool paused)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia-eg2-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var started = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var probe = new SchedulingSession
        {
            BeforeInference = count =>
            {
                if (count != 1) return;
                started.Set();
                resume.Wait(cancellation.Token);
            }
        };
        var sessionCreations = 0;
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () =>
        {
            sessionCreations++;
            return probe;
        };
        using var index = new IndexService();
        var embeddingField = typeof(IndexService).GetField("_embeddingService", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task<IReadOnlyList<float[]>>? indexingInference = null;
        try
        {
            var document = Path.Combine(directory, "archive.txt");
            var image = Path.Combine(directory, "portrait.png");
            await File.WriteAllTextAsync(document, "data", cancellation.Token);
            await File.WriteAllTextAsync(image, "data", cancellation.Token);
            var store = new IndexVectorStore(Path.Combine(directory, "index.db"));
            await store.SynchronizeFileSourceAsync(
                IndexSource.Manual, [document, image], new HashSet<string>(), cancellation.Token);
            var vector = new float[EmbeddingGemmaEmbeddingService.VectorDimensions];
            vector[0] = 1;
            await store.UpsertTextAsync(document, _service.ModelId, vector, cancellation.Token);
            await store.UpsertImageAsync(image, "1:1", _service.ModelId, vector, cancellation.Token);
            typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index, store);
            embeddingField.SetValue(index, _service);
            typeof(IndexService).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(index, IndexStatusSnapshot.Empty with { IsRebuilding = true });
            index.SetForegroundPause(paused);
            Assert.HasCount(0, index.SearchPinyin("people", 10, cancellation.Token));

            indexingInference = _service.EmbedAsync(["Indexed document"], 1024, cancellation.Token);
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
            var search = index.SearchAsync("people", 10, cancellation.Token);
            Assert.IsFalse(search.IsCompleted, "Search must wait for the shared session's active inference.");
            resume.Set();
            var results = await search;
            await indexingInference;

            CollectionAssert.AreEquivalent(new[] { document, image }, results.Select(result => result.Source.OnlyKey).ToArray());
            Assert.AreEqual(1, sessionCreations);
            Assert.HasCount(2, probe.Batches);
            var expected = _service.EncodeInput(EmbeddingGemmaEmbeddingService.QueryInstruction + "people", 512);
            Assert.IsTrue(probe.InputRows.Last().Span[..expected.Length].SequenceEqual(expected));
            Assert.IsFalse(probe.IsDisposed);
            Assert.IsTrue(index.GetStatus().IsRebuilding);
            Assert.AreEqual(paused, index.GetStatus().IsPaused);
        }
        finally
        {
            resume.Set();
            if (indexingInference is not null) await indexingInference;
            embeddingField.SetValue(index, null);
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task EmbedAsync_BackendChanged_DisposesPreviousSessionAndUsesNewRuntime()
    {
        using var previous = new SchedulingSession();
        using var replacement = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => previous;
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-Replacement"] = () => replacement;
        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "EG2-Test-Replacement";

        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);

        Assert.IsTrue(previous.IsDisposed);
        Assert.HasCount(1, replacement.Batches);
        await _service.ReleaseSessionsAsync();
        Assert.IsTrue(replacement.IsDisposed);
    }

    [TestMethod]
    public async Task ReleaseSessionsAsync_InferenceActive_WaitsBeforeDisposingSession()
    {
        using var started = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var probe = new SchedulingSession
        {
            BeforeInference = _ =>
            {
                started.Set();
                resume.Wait(cancellation.Token);
            }
        };
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        var inference = _service.EmbedAsync(["hello"], 512, cancellation.Token);
        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));

        var release = _service.ReleaseSessionsAsync();
        Assert.IsFalse(release.IsCompleted);
        Assert.IsFalse(probe.IsDisposed);
        resume.Set();
        await Task.WhenAll(inference, release);

        Assert.IsTrue(probe.IsDisposed);
    }

    [TestMethod]
    public async Task EmbedAsync_MultipleBatches_ReusesSessionUntilReleased()
    {
        var sessions = new List<SchedulingSession>();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () =>
        {
            var session = new SchedulingSession();
            sessions.Add(session);
            return session;
        };

        await _service.EmbedAsync(Enumerable.Repeat("Indexed document", 16).ToArray(), 512, CancellationToken.None);
        await _service.EmbedAsync(["task: search result | query: hello"], 512, CancellationToken.None);

        Assert.HasCount(1, sessions);
        Assert.HasCount(5, sessions[0].Batches);
        Assert.IsFalse(sessions[0].IsDisposed);
        await _service.ReleaseSessionsAsync();
        Assert.IsTrue(sessions[0].IsDisposed);
        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);
        Assert.HasCount(2, sessions);
        Assert.IsFalse(sessions[1].IsDisposed);
    }

    [TestMethod]
    public async Task EmbedAsync_CudaAllocationFails_ReloadsOnceWithoutTruncatingAndReusesSession()
    {
        using var previous = new SchedulingSession
        {
            Device = "GPU(CUDA)",
            BeforeInference = count =>
            {
                if (count == 2) throw new OutOfMemoryException("Fragmented CUDA arena");
            }
        };
        using var replacement = new SchedulingSession { Device = "GPU(CUDA)" };
        var creations = 0;
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () => ++creations == 1 ? previous : replacement;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";
        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);
        var text = string.Concat(Enumerable.Repeat("Apples grow in orchards. ", 550));
        var length = _service.EncodeInput(text, 8192).Length;

        var vectors = await _service.EmbedAsync([text], 8192, CancellationToken.None);
        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);

        Assert.HasCount(1, vectors);
        Assert.AreEqual(2, creations);
        Assert.IsTrue(previous.IsDisposed);
        Assert.IsFalse(replacement.IsDisposed);
        Assert.AreEqual((1, length), replacement.Batches.First());
        Assert.HasCount(2, replacement.Batches);
    }

    [TestMethod]
    public async Task EmbedAsync_CudaAllocationStillFails_UsesCpuUntilReleasedThenRetriesConfiguredDevice()
    {
        var gpuSessions = new List<SchedulingSession>();
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            var session = new SchedulingSession
            {
                Device = "GPU(CUDA)",
                BeforeInference = gpuSessions.Count < 2 ? _ => throw new OutOfMemoryException("CUDA arena limit") : null
            };
            gpuSessions.Add(session);
            return session;
        };
        using var cpu = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["CPU"] = () => cpu;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";

        var vectors = await _service.EmbedAsync(["hello"], 512, CancellationToken.None);
        await _service.EmbedAsync(["hello again"], 512, CancellationToken.None);

        Assert.HasCount(1, vectors);
        Assert.HasCount(2, gpuSessions);
        Assert.IsTrue(gpuSessions.All(session => session.IsDisposed));
        Assert.HasCount(2, cpu.Batches);
        Assert.AreEqual("GPU(CUDA)", ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName]);
        await _service.ReleaseSessionsAsync();
        Assert.IsTrue(cpu.IsDisposed);
        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);
        Assert.HasCount(3, gpuSessions);
        Assert.HasCount(1, gpuSessions[2].Batches);
        Assert.IsFalse(gpuSessions[2].IsDisposed);
    }

    [TestMethod]
    public async Task EmbedAsync_CudaAllocationFailsWithoutCpu_StopsAfterOneReload()
    {
        var creations = 0;
        var failure = new OutOfMemoryException("CUDA arena limit");
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            creations++;
            return new SchedulingSession { Device = "GPU(CUDA)", BeforeInference = _ => throw failure };
        };
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";

        var exception = await Assert.ThrowsAsync<OutOfMemoryException>(() => _service.EmbedAsync(["hello"], 512, CancellationToken.None));

        Assert.AreSame(failure, exception);
        Assert.AreEqual(2, creations);
    }

    [TestMethod]
    public async Task EmbedAsync_CanceledDuringCudaFailure_DoesNotReloadOrUseCpu()
    {
        using var cancellation = new CancellationTokenSource();
        using var gpu = new SchedulingSession
        {
            Device = "GPU(CUDA)",
            BeforeInference = _ =>
            {
                cancellation.Cancel();
                throw new OutOfMemoryException("CUDA arena limit");
            }
        };
        var creations = 0;
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            creations++;
            return gpu;
        };
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";

        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.EmbedAsync(["hello"], 512, cancellation.Token));

        Assert.AreEqual(1, creations);
        Assert.IsTrue(gpu.IsDisposed);
    }

    [TestMethod]
    public async Task EmbedAsync_NonAllocationCudaFailure_DoesNotRetry()
    {
        var creations = 0;
        var failure = new InvalidOperationException("Invalid model input");
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            creations++;
            return new SchedulingSession { Device = "GPU(CUDA)", BeforeInference = _ => throw failure };
        };
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.EmbedAsync(["hello"], 512, CancellationToken.None));

        Assert.AreSame(failure, exception);
        Assert.AreEqual(1, creations);
    }

    [TestMethod]
    public async Task EmbedAsync_CpuAllocationFails_DoesNotRetry()
    {
        var creations = 0;
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () =>
        {
            creations++;
            return new SchedulingSession { BeforeInference = _ => throw new OutOfMemoryException("CPU allocation failed") };
        };

        await Assert.ThrowsAsync<OutOfMemoryException>(() => _service.EmbedAsync(["hello"], 512, CancellationToken.None));

        Assert.AreEqual(1, creations);
    }

    [TestMethod]
    public async Task EmbedAsync_CudaModelLoadingAllocationFails_ReloadsOnceThenUsesCpu()
    {
        var failedSessions = new List<SchedulingSession>();
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            var session = new SchedulingSession
            {
                Device = "GPU(CUDA)", BeforeInitialization = () => throw new OutOfMemoryException("CUDA model allocation failed")
            };
            failedSessions.Add(session);
            return session;
        };
        using var cpu = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["CPU"] = () => cpu;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";

        var vectors = await _service.EmbedAsync(["hello"], 512, CancellationToken.None);

        Assert.HasCount(1, vectors);
        Assert.HasCount(2, failedSessions);
        Assert.IsTrue(failedSessions.All(session => session.IsDisposed));
        Assert.IsTrue(failedSessions.All(session => session.Batches.Count == 0));
        Assert.HasCount(1, cpu.Batches);
    }

    [TestMethod]
    public async Task EmbedImagesAsync_CudaTextAllocationFails_ReleasesVisionSessionAndPreservesImageFeatures()
    {
        var row = _reference.RootElement.GetProperty("images")[0];
        var path = Path.Combine(_referenceDirectory, row.GetProperty("file").GetString()!);
        var featureLength = row.GetProperty("soft_tokens").GetInt32() * 512;
        using var vision = new SchedulingSession { Device = "GPU(CUDA)", OutputLength = featureLength };
        var failedTexts = new List<SchedulingSession>();
        using var cpu = new SchedulingSession();
        var textCreations = 0;
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            if (textCreations++ == 0) return vision;
            var session = new SchedulingSession
            {
                Device = "GPU(CUDA)", BeforeInference = _ => throw new OutOfMemoryException("CUDA arena limit")
            };
            failedTexts.Add(session);
            return session;
        };
        PluginOverall.OnnxRuntimes["EG2-Test"]["CPU"] = () => cpu;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.VisionModelSignName] = "GPU(CUDA)";

        var vectors = await _service.EmbedImagesAsync([path], CancellationToken.None);

        Assert.HasCount(1, vectors);
        Assert.IsTrue(vision.IsDisposed);
        Assert.HasCount(2, failedTexts);
        Assert.IsTrue(failedTexts.All(session => session.IsDisposed));
        Assert.HasCount(1, cpu.Batches);
        Assert.AreEqual(featureLength, cpu.ImageFeatureLengths.First());
    }

    private sealed class SchedulingSession : IInferenceSession
    {
        public string Device { get; init; } = "CPU";
        public IReadOnlyList<string> InputNames => [];
        public IReadOnlyList<int[]> OutputShape => [];
        public ConcurrentQueue<(int Count, int Length)> Batches { get; } = [];
        public ConcurrentQueue<ReadOnlyMemory<long>> InputRows { get; } = [];
        public ConcurrentQueue<int> MaskLengths { get; } = [];
        public ConcurrentQueue<int> ImageFeatureLengths { get; } = [];
        public ConcurrentQueue<(int Image, int Video, int Audio)> FeatureLengths { get; } = [];
        public IReadOnlyDictionary<string, long>? FreeDimensionOverrides { get; private set; }
        public int? OutputLength { get; init; }
        public Action<int>? BeforeInference { get; init; }
        public Action? BeforeInitialization { get; init; }
        public int WorkerCount { get; private set; }
        public bool UseCpuMemoryArena { get; private set; }
        public long GpuMemoryLimitBytes { get; private set; }
        public bool IsDisposed { get; private set; }
        private int _inferenceCount;
        public void InitSession(string modelPath) => throw new NotSupportedException();
        public void InitSession(byte[] modelData) => throw new NotSupportedException();
        public void InitSession(string modelPath, bool useCpuMemoryArena, int intraOpNumThreads, long gpuMemoryLimitBytes)
        {
            BeforeInitialization?.Invoke();
            UseCpuMemoryArena = useCpuMemoryArena;
            WorkerCount = intraOpNumThreads;
            GpuMemoryLimitBytes = gpuMemoryLimitBytes;
        }
        public void InitSession(string modelPath, bool useCpuMemoryArena, int intraOpNumThreads,
            long gpuMemoryLimitBytes, IReadOnlyDictionary<string, long>? freeDimensionOverrides)
        {
            InitSession(modelPath, useCpuMemoryArena, intraOpNumThreads, gpuMemoryLimitBytes);
            FreeDimensionOverrides = freeDimensionOverrides;
        }
        public Memory<float> Infer(List<(string, Memory<int>, Memory<float>)> inputs) => throw new NotSupportedException();
        public Memory<float> Infer(List<(string, Memory<int>, Memory<long>)> int64Inputs,
            List<(string, Memory<int>, Memory<float>)> floatInputs, string outputName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shape = int64Inputs[0].Item2.Span;
            Batches.Enqueue((shape[0], shape[1]));
            for (var index = 0; index < shape[0]; index++)
            {
                InputRows.Enqueue(int64Inputs[0].Item3.Slice(index * shape[1], shape[1]));
                if (int64Inputs.Count > 1 && int64Inputs[1].Item1 == "attention_mask")
                    MaskLengths.Enqueue(int64Inputs[1].Item3.Span.Slice(index * shape[1], shape[1]).Count(1L));
            }
            ImageFeatureLengths.Enqueue(floatInputs[0].Item3.Length);
            if (floatInputs.Count == 3)
                FeatureLengths.Enqueue((floatInputs[0].Item3.Length, floatInputs[1].Item3.Length, floatInputs[2].Item3.Length));
            BeforeInference?.Invoke(Interlocked.Increment(ref _inferenceCount));
            var vectors = new float[OutputLength ?? shape[0] * 768];
            for (var index = 0; index < shape[0]; index++) vectors[index * 768] = 1;
            return vectors;
        }
        public void Dispose() => IsDisposed = true;
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
