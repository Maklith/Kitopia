using System.Text.Json;
using Kitopia.Desktop.Features.Services.Plugin;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class PluginPackageTests
{
    [TestMethod]
    public void Activate_DisposedBeforeCompletion_RestoresOldDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "plugin");
        var staging = Path.Combine(root, ".staging");
        Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, "old.txt"), "old");
        try
        {
            PreparePackage(staging);
            using (var package = new PluginPackage(staging, "test", "1.0.0"))
            {
                package.Activate(live);
                Assert.IsFalse(File.Exists(Path.Combine(live, "old.txt")));
                Assert.IsTrue(File.Exists(Path.Combine(live, "manifest.json")));
            }
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(live, "old.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(live, "manifest.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Activate_Completed_DoesNotRetainRemovedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "plugin");
        var staging = Path.Combine(root, ".staging");
        Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, "removed.dll"), "old");
        try
        {
            PreparePackage(staging);
            using (var package = new PluginPackage(staging, "test", "1.0.0"))
            {
                package.Activate(live);
                package.Complete();
            }
            Assert.IsFalse(File.Exists(Path.Combine(live, "removed.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(live, "manifest.json")));
            Assert.HasCount(1, Directory.GetDirectories(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Discovery_DuplicateIdentity_SkipsBothCandidates()
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            PreparePackage(Path.Combine(root, "first"));
            PreparePackage(Path.Combine(root, "second"));
            Assert.IsEmpty(PluginDiscoveryService.DiscoverPlugins(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Package_WrongIdentity_IsRejectedBeforeLiveDirectoryChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            PreparePackage(root);
            Assert.ThrowsExactly<InvalidDataException>(() => new PluginPackage(root, "other", "1.0.0"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow("0.9.0")]
    [DataRow("1.0.1")]
    [DataRow("1.0.0-rc.1")]
    [DataRow("*")]
    [DataRow("[1.0.0,2.0.0)")]
    public void Package_DifferentRequestedVersion_IsRejected(string requestedVersion)
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            PreparePackage(root);
            Assert.ThrowsExactly<InvalidDataException>(() => new PluginPackage(root, "test", requestedVersion));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow("1.0")]
    [DataRow("1.0.0.0")]
    [DataRow("1.0.0+build.2")]
    public void Package_EquivalentNuGetVersion_IsAccepted(string requestedVersion)
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            PreparePackage(root);
            using var package = new PluginPackage(root, "test", requestedVersion);
            Assert.AreEqual("1.0.0", package.Info.PluginBaseInfo.Version);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow("Kitopia", " ^0.0.0 ", "*")]
    [DataRow("dependency", "^1.2.3", "[1.2.3, 2.0.0)")]
    [DataRow("dependency", "^0.3.0", "[0.3.0, 0.4.0)")]
    [DataRow("dependency", "^0.0.3", "[0.0.3, 0.0.4)")]
    [DataRow("dependency", "^0.0.0", "[0.0.0, 0.0.1)")]
    [DataRow("dependency", "^1.0.0-beta.1", "[1.0.0-beta.1, 2.0.0)")]
    [DataRow("dependency", "[1.2.3]", "[1.2.3]")]
    [DataRow("dependency", "1.2.3", "1.2.3")]
    [DataRow("dependency", "1.2.*", "1.2.*")]
    public void Discovery_LegacyCaretRange_ConvertsAtManifestBoundary(string dependency, string range, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            PreparePackage(root, dependencies: new Dictionary<string, string> { [dependency] = range });
            var plugin = PluginDiscoveryService.ReadPlugin(root);
            Assert.AreEqual(expected, plugin.PluginBaseInfo.Dependencies[dependency]);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("^invalid")]
    [DataRow(">=1.0.0")]
    [DataRow("[2.0.0,1.0.0]")]
    public void Discovery_InvalidDependencyRange_IsRejected(string range)
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            PreparePackage(root, dependencies: new Dictionary<string, string> { ["dependency"] = range });
            var exception = Assert.ThrowsExactly<InvalidDataException>(() => PluginDiscoveryService.ReadPlugin(root));
            StringAssert.Contains(exception.Message, "dependency");
            StringAssert.Contains(exception.Message, "NuGet");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Activate_LockedTarget_PreservesOldDirectoryAndAllowsRetry()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file sharing semantics are required.");
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "live");
        var staging = Path.Combine(root, "staging");
        try
        {
            PreparePackage(live);
            PreparePackage(staging);
            using var package = new PluginPackage(staging, "test", "1.0.0");
            using (File.Open(Path.Combine(live, "entry.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsExactly<IOException>(() => package.Activate(live));
                Assert.IsTrue(File.Exists(Path.Combine(live, "manifest.json")));
                Assert.IsTrue(Directory.Exists(staging));
                Assert.IsEmpty(Directory.GetDirectories(root, ".backup-*"));
            }
            package.Activate(live);
            package.Complete();
            Assert.IsTrue(File.Exists(Path.Combine(live, "manifest.json")));
            Assert.IsEmpty(Directory.GetDirectories(root, ".backup-*"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void RemovePluginDirectory_LockedFile_PreservesManifest()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file sharing semantics are required.");
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "live");
        try
        {
            PreparePackage(live);
            using (File.Open(Path.Combine(live, "entry.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsExactly<IOException>(() => PluginDiscoveryService.RemovePluginDirectory(live));
                Assert.IsTrue(File.Exists(Path.Combine(live, "manifest.json")));
                Assert.IsTrue(File.Exists(Path.Combine(live, "entry.dll")));
            }
            PluginDiscoveryService.RemovePluginDirectory(live);
            Assert.IsEmpty(Directory.GetDirectories(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Discovery_PartiallyRemovedPluginWithPendingUpdate_PreservesUpdateRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "test");
        try
        {
            PreparePackage(live);
            File.Delete(Path.Combine(live, "manifest.json"));
            File.WriteAllText(Path.Combine(live, ".remove"), "");
            File.WriteAllText(Path.Combine(live, ".update"), "2.0.0");

            Assert.IsEmpty(PluginDiscoveryService.DiscoverPlugins(root, handleRemovals: true));
            Assert.AreEqual("2.0.0", File.ReadAllText(Path.Combine(live, ".update")));
            Assert.IsTrue(File.Exists(Path.Combine(live, "entry.dll")));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Discovery_InterruptedRemoval_CleansHiddenDirectoryOnStartup()
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        var removed = Path.Combine(root, ".remove-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            PreparePackage(removed);
            Assert.IsEmpty(PluginDiscoveryService.DiscoverPlugins(root, handleRemovals: true));
            Assert.IsEmpty(Directory.GetDirectories(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Rollback_LockedReplacement_DefersRecoveryUntilStartup(bool hasOldVersion)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file sharing semantics are required.");
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "test");
        var staging = Path.Combine(root, ".staging");
        try
        {
            if (hasOldVersion)
            {
                PreparePackage(live);
                File.WriteAllText(Path.Combine(live, "old.txt"), "old version");
            }
            PreparePackage(staging);
            using var package = new PluginPackage(staging, "test", "1.0.0");
            package.Activate(live);
            using (File.Open(Path.Combine(live, "entry.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsExactly<IOException>(() => package.Dispose());
                Assert.IsTrue(File.Exists(Path.Combine(live, hasOldVersion ? ".rollback" : ".remove")));
            }
            var discovered = PluginDiscoveryService.DiscoverPlugins(root, handleRemovals: true);
            if (hasOldVersion)
            {
                Assert.HasCount(1, discovered);
                Assert.AreEqual("old version", File.ReadAllText(Path.Combine(live, "old.txt")));
                Assert.IsFalse(File.Exists(Path.Combine(live, ".rollback")));
            }
            else
            {
                Assert.IsEmpty(discovered);
                Assert.IsFalse(Directory.Exists(live));
            }
            Assert.IsEmpty(Directory.GetDirectories(root, ".backup-*"));
            package.Dispose();
            Assert.AreEqual(hasOldVersion, Directory.Exists(live));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Discovery_InterruptedDirectorySwitch_RestoresBackupOnStartup()
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-package-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(root, ".backup-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            PreparePackage(backup);
            var discovered = PluginDiscoveryService.DiscoverPlugins(root, handleRemovals: true);
            Assert.HasCount(1, discovered);
            Assert.AreEqual("test", discovered[0].ToPlgString());
            Assert.IsTrue(File.Exists(Path.Combine(root, "test", "manifest.json")));
            Assert.IsFalse(Directory.Exists(backup));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void PreparePackage(string directory, Dictionary<string, string>? dependencies = null)
    {
        Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "PluginFixture", "PluginLifecycle.dll"), Path.Combine(directory, "entry.dll"));
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new PluginManifest
        {
            Name = "Test", NameSign = "test", Version = "1.0.0", Description = "Test", Main = "entry.dll",
            Dependencies = dependencies ?? []
        }));
    }
}
