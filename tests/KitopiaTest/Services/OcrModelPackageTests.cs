using System.Globalization;
using Kitopia.Desktop.Converter.OnnxModelManagerPage;
using Kitopia.Desktop.Features.Ocr;

namespace KitopiaTest.Services;

[TestClass]
public sealed class OcrModelPackageTests
{
    [TestMethod]
    public void ModelManager_OcrEntries_ReportIndividualSizesAndDependencies()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"KitopiaTest_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var models = OcrModelPackage.CreateModelInfos();
            foreach (var wrapper in models)
            {
                wrapper.Model.ModelPath = Path.Combine(directory, Path.GetFileName(wrapper.Model.ModelPath));
                wrapper.Model.RequiredFiles = wrapper.Model.RequiredFiles
                    .Select(path => Path.Combine(directory, Path.GetFileName(path))).ToArray();
            }

            var detector = models.Single(wrapper => wrapper.Model.SignName == OcrModelPackage.DetectorSignName);
            var recognizer = models.Single(wrapper => wrapper.Model.SignName == OcrModelPackage.RecognizerSignName);
            var dictionaryPath = Path.Combine(directory, Path.GetFileName(OcrModelPackage.DictionaryPath));
            using (var file = File.Create(detector.Model.ModelPath)) file.SetLength(1024 * 1024);
            using (var file = File.Create(recognizer.Model.ModelPath)) file.SetLength(2 * 1024 * 1024);
            using (var file = File.Create(dictionaryPath)) file.SetLength(256 * 1024);

            var converter = new OnnxModelInfoWrapperToModelSizeCtr();
            Assert.AreEqual("1 MiB", converter.Convert(detector, typeof(string), null, CultureInfo.InvariantCulture));
            Assert.AreEqual("2.25 MiB", converter.Convert(recognizer, typeof(string), null, CultureInfo.InvariantCulture));
            Assert.IsTrue(DataGridModelSizeComparer.Default.Compare(detector, recognizer) < 0);
            Assert.IsFalse(detector.Model.NeedDownload);
            Assert.IsFalse(recognizer.Model.NeedDownload);

            File.Delete(dictionaryPath);
            Assert.IsFalse(detector.Model.NeedDownload);
            Assert.IsTrue(recognizer.Model.NeedDownload);

            File.Delete(recognizer.Model.ModelPath);
            Assert.AreEqual("1 MiB", converter.Convert(detector, typeof(string), null, CultureInfo.InvariantCulture));
            Assert.IsFalse(detector.Model.NeedDownload);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
