using System.Diagnostics;
using Kitopia.Desktop.Features.Search.ProcessUsage;
using PluginCore;

namespace KitopiaTest.Search;

[TestClass]
public sealed class ProcessUsageTests
{
    [TestMethod]
    [DataRow(0d, "0.0%")]
    [DataRow(3.25d, "3.3%")]
    [DataRow(100d, "100.0%")]
    public void FormatCpu_SampledPercent_KeepsOneDecimal(double percent, string expected)
    {
        Assert.AreEqual(expected, ProcessUsageService.FormatCpu(percent));
    }

    [TestMethod]
    public void FormatCpu_FirstSampleBeforeCpuIsKnown_ShowsPlaceholder()
    {
        Assert.AreEqual("--", ProcessUsageService.FormatCpu(null));
    }

    [TestMethod]
    [DataRow(0L, "0 MB")]
    [DataRow(52L * 1024 * 1024, "52 MB")]
    [DataRow(1536L * 1024 * 1024, "1.5 GB")]
    [DataRow(-1L, "0 MB")]
    public void FormatMemory_WorkingSet_ScalesToLargestUnit(long bytes, string expected)
    {
        Assert.AreEqual(expected, ProcessUsageService.FormatMemory(bytes));
    }

    [TestMethod]
    [DataRow("C:\\Program Files\\App\\app.exe", "app")]
    [DataRow("C:\\App\\APP.EXE", "APP")]
    public void TryResolveProcessName_ExecutablePath_ReturnsFileNameWithoutExtension(
        string path, string expected)
    {
        var item = new SearchViewItem { ItemDisplayName = "App", OnlyKey = path, FileType = FileType.应用程序 };

        Assert.IsTrue(ProcessUsageService.TryResolveProcessName(item, out var name));
        Assert.AreEqual(expected, name);
    }

    [TestMethod]
    public void TryResolveProcessName_PlainExeFile_IsAccepted()
    {
        var item = new SearchViewItem
        {
            ItemDisplayName = "tool.exe",
            OnlyKey = "D:\\tools\\tool.exe",
            FileType = FileType.文件
        };

        Assert.IsTrue(ProcessUsageService.TryResolveProcessName(item, out var name));
        Assert.AreEqual("tool", name);
    }

    [TestMethod]
    [DataRow("C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\app.lnk", FileType.应用程序)]
    [DataRow("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", FileType.UWP应用)]
    [DataRow("https://example.com", FileType.URL)]
    [DataRow("C:\\docs\\report.docx", FileType.Word文档)]
    public void TryResolveProcessName_NonExecutableKey_IsRejected(string key, FileType fileType)
    {
        var item = new SearchViewItem { ItemDisplayName = "Item", OnlyKey = key, FileType = fileType };

        Assert.IsFalse(ProcessUsageService.TryResolveProcessName(item, out var name));
        Assert.AreEqual(string.Empty, name);
    }

    [TestMethod]
    public void TryResolveProcessName_NullItem_IsRejected()
    {
        Assert.IsFalse(ProcessUsageService.TryResolveProcessName(null, out var name));
        Assert.AreEqual(string.Empty, name);
    }

    [TestMethod]
    public void SetTargets_TrimmedNames_AreRemovedFromSnapshots()
    {
        using var service = new ProcessUsageService();

        // No sampler runs while disabled, so the service must stay empty and harmless.
        service.SetTargets(["app", "", " "]);
        Assert.IsFalse(service.TryGet("app", out _));
        Assert.IsFalse(service.Enabled);
    }

    [TestMethod]
    public void Enabled_TogglingBackOff_ClearsSampledSnapshots()
    {
        using var service = new ProcessUsageService();
        service.Enabled = true;
        service.Enabled = false;

        Assert.IsFalse(service.Enabled);
        Assert.IsFalse(service.TryGet("app", out _));
    }

    [TestMethod]
    public async Task Sampling_IdleProcess_ReportsZeroCpuAfterFirstSample()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The idle process fixture uses the Windows command interpreter.");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "Kitopia CPU " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "KitopiaCpu" + Guid.NewGuid().ToString("N") + ".exe");
        // A unique executable name isolates the sample from other command interpreters.
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), executable);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable)
            {
                Arguments = "/d /q /c \"echo ready&set /p kitopiaIdle=\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            })!;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                Assert.AreEqual("ready", await process.StandardOutput.ReadLineAsync(timeout.Token));
                using var service = new ProcessUsageService();
                var name = process.ProcessName;
                service.SetTargets([name]);
                service.Enabled = true;

                while (!timeout.IsCancellationRequested)
                {
                    if (service.TryGet(name, out var snapshot) && snapshot.CpuPercent == 0d)
                    {
                        Assert.AreEqual("0.0%", ProcessUsageService.FormatCpu(snapshot.CpuPercent));
                        Assert.IsGreaterThan(0L, snapshot.WorkingSetBytes);
                        return;
                    }

                    await Task.Delay(50);
                }

                Assert.Fail("An idle process must report 0% CPU after its baseline has been sampled.");
            }
            finally
            {
                if (!process.HasExited) process.Kill();
                await process.WaitForExitAsync();
            }
        }
        finally
        {
            // Windows may retain the executable mapping briefly after the process exits.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                    break;
                }
                catch (Exception exception) when (attempt < 20 &&
                    exception is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(50);
                }
            }
        }
    }
}
