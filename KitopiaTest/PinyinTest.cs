// Author: liaom
// SolutionName: Kitopia
// ProjectName: KitopiaTest
// FileName:PinyinTest.cs
// Date: 2025/11/29 15:11
// FileEffect:

using Pinyin.NET;

namespace KitopiaTest;

[TestClass]
public class PinyinTest
{
    public class AppInfo
    {
        public string Name { get; set; }
    }
    [TestMethod]
    public void TestMethod1()
    {
        var apps = new List<AppInfo>
        {
            new AppInfo { Name = "微信" },
            new AppInfo { Name = "网易云音乐" },
            new AppInfo { Name = "Windows相机" }
        };

        var searcher = new PinyinSearcher<AppInfo>(apps, app => app.Name);

        var results = searcher.Search("weixin");
        Assert.AreEqual(results.First().Source.Name, "微信");
        foreach (var result in results)
        {
            Console.WriteLine($"匹配度: {result.Weight}, 应用: {result.Source.Name}");
        }
    }
}