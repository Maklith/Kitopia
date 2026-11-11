using System.Security.Cryptography;
using System.Text.Json;
using Kitopia.Desktop.Features.Search.Semantic;
using PluginCore.Onnx;

namespace KitopiaTest.Search;

[TestClass]
public sealed class EmbeddingGemmaModelPackageTests
{
    [TestMethod]
    public void IsComplete_RequiresBothQ4GraphsExternalDataAndProcessorFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"KitopiaTest_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "onnx"));
            foreach (var file in EmbeddingGemmaModelPackage.RequiredFiles)
            {
                Assert.IsFalse(EmbeddingGemmaModelPackage.IsComplete(directory));
                File.WriteAllText(Path.Combine(directory, file), "asset");
            }
            Assert.IsTrue(EmbeddingGemmaModelPackage.IsComplete(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void DirectoryPath_UsesRegisteredSemanticPluginModel()
    {
        Assert.IsNull(EmbeddingGemmaModelPackage.DirectoryPath);
    }

    [TestMethod]
    public void CreateWithoutSemanticPlugin_ReturnsFalse()
    {
        Assert.IsFalse(EmbeddingGemmaModelPackage.IsComplete());
    }

    [TestMethod]
    public void BundledAssets_NativeShards_MatchChecksumsAndGitHubFileLimit()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kitopia.sln"))) root = root.Parent;
        Assert.IsNotNull(root);
        var directory = Path.Combine(root.FullName, "plugins", "KitopiaSemanticSearch", "EmbeddingGemma2", "onnx");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "shards.json")));
        Assert.AreEqual(EmbeddingGemmaModelPackage.Revision, manifest.RootElement.GetProperty("upstream_revision").GetString());
        var largeShards = 0;
        var shardCount = 0;
        foreach (var model in manifest.RootElement.GetProperty("models").EnumerateObject())
        {
            using var graph = File.OpenRead(Path.Combine(directory, model.Name));
            Assert.AreEqual(model.Value.GetProperty("graph_sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(graph)));
            foreach (var shard in model.Value.GetProperty("shards").EnumerateArray())
            {
                var name = shard.GetProperty("file").GetString()!;
                var path = Path.Combine(directory, name);
                using var data = File.OpenRead(path);
                Assert.AreEqual(shard.GetProperty("bytes").GetInt64(), data.Length);
                Assert.IsTrue(data.Length < 100L * 1024 * 1024, name);
                if (data.Length > 32L * 1024 * 1024)
                {
                    Assert.AreEqual("model_q4.onnx_data.part002", name);
                    Assert.AreEqual(64L * 1024 * 1024, data.Length);
                    largeShards++;
                }
                Assert.AreEqual(shard.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)), name);
                CollectionAssert.Contains(EmbeddingGemmaModelPackage.RequiredFiles, "onnx/" + name);
                shardCount++;
            }
        }
        Assert.AreEqual(1, largeShards);
        Assert.AreEqual(9, shardCount);
        foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(directory)!, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(path) == "model_q4.onnx_data.part002") continue;
            Assert.IsTrue(new FileInfo(path).Length < 40000000, path);
        }
    }

    [TestMethod]
    public void NeedDownload_RequiresEveryDeclaredModelFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"KitopiaTest_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var modelPath = Path.Combine(directory, "model.onnx");
            var modelDataPath = modelPath + "_data";
            var model = new OnnxModelInfo
            {
                ModelPath = modelPath,
                RequiredFiles = [modelDataPath]
            };

            File.WriteAllText(modelPath, "model");
            Assert.IsTrue(model.NeedDownload);

            File.WriteAllText(modelDataPath, "data");
            Assert.IsFalse(model.NeedDownload);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
