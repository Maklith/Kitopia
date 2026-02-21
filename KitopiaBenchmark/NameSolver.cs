using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;


namespace KitopiaBenchmark;

[RPlotExporter]
public partial class NameSolver
{
    [GeneratedRegex("[^A-Z]")]
    private static partial Regex MyRegex();

    [GeneratedRegex("[\u4e00-\u9fa5]")]
    private static partial Regex ChineseRegex();

    [Benchmark]
    public void Test1()
    {
        var name = "Windows Copilot Frame MSIX Pack";
        var keys = new List<string>();
        var initials = name.Split(" ", StringSplitOptions.RemoveEmptyEntries);
    }


    [Benchmark]
    public void Test2()
    {
        var name = "Windows Copilot Frame MSIX Pack";
        var keys = new List<string>();
        var initials = name.Split(" ", StringSplitOptions.RemoveEmptyEntries);
    }


    private static void AddUtil(List<string> keys, string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length <= 1) return;


        if (!keys.Contains(name)) keys.Add(name);
    }
}