using Kitopia.Desktop.Features.Search.Semantic;
using PluginCore.Onnx;

namespace KitopiaTest.Search;

[TestClass]
public sealed class BgeModelPackageTests
{
    [TestMethod]
    public void IsComplete_RequiresQuantizedModelDataAndTokenizer()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"KitopiaTest_{Guid.NewGuid():N}");
        try
        {
            var quantizedDirectory = Path.Combine(directory, "quantized");
            Directory.CreateDirectory(quantizedDirectory);
            File.WriteAllText(Path.Combine(quantizedDirectory, "model_quantized.onnx"), "model");
            File.WriteAllText(Path.Combine(directory, "tokenizer.json"), "tokenizer");

            Assert.IsFalse(BgeModelPackage.IsComplete(directory));

            File.WriteAllText(Path.Combine(quantizedDirectory, "model_quantized.onnx_data"), "data");

            Assert.IsTrue(BgeModelPackage.IsComplete(directory));
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
    public void CreateModelInfo_DescribesBundledModelPackage()
    {
        var model = BgeModelPackage.CreateModelInfo();

        Assert.IsTrue(model.IsBundled);
        Assert.IsFalse(model.CanDownload);
        Assert.AreEqual("中文语义搜索模型（BGE）", model.Name);
        Assert.AreEqual("用于理解搜索词与内容的语义关联，提升搜索结果相关性。模型已随应用安装，无需下载。", model.Description);
        CollectionAssert.AreEqual(
            new[] { BgeModelPackage.ModelDataPath, BgeModelPackage.TokenizerPath },
            model.RequiredFiles.ToArray());
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
