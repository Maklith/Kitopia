using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Kitopia.Desktop.Platform.Windows;

namespace KitopiaTest.Services;

[TestClass]
public sealed class StartupRegistrationTests
{
    [TestMethod]
    public async Task ReadStartupTaskAsync_ExistingTask_ReadsXmlWithoutChangingRegistration()
    {
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
        dynamic? folder = null;
        dynamic? tasks = null;
        dynamic? task = null;
        string taskName;
        string expectedXml;
        try
        {
            scheduler.Connect();
            folder = scheduler.GetFolder(@"\");
            tasks = folder.GetTasks(1);
            if ((int)tasks.Count == 0) Assert.Inconclusive("There is no readable task in the root folder to query.");
            task = tasks[1];
            taskName = (string)task.Name;
            expectedXml = (string)task.Xml;
        }
        finally
        {
            if (task is not null) Marshal.FinalReleaseComObject(task);
            if (tasks is not null) Marshal.FinalReleaseComObject(tasks);
            if (folder is not null) Marshal.FinalReleaseComObject(folder);
            Marshal.FinalReleaseComObject(scheduler);
        }
        var service = new StartupApplicationService();
        Assert.AreEqual(expectedXml, await service.ReadNativeStartupTaskAsync(taskName));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task ReadStartupTaskAsync_MissingTask_ReturnsNullWithoutThrowingExpectedExceptions()
    {
        var exceptions = new ConcurrentQueue<Exception>();
        EventHandler<FirstChanceExceptionEventArgs> capture = (_, args) =>
        {
            if (args.Exception.HResult is unchecked((int)0x80070002) or unchecked((int)0x80070003))
                exceptions.Enqueue(args.Exception);
        };
        AppDomain.CurrentDomain.FirstChanceException += capture;
        try
        {
            var service = new StartupApplicationService();
            var xml = await service.ReadNativeStartupTaskAsync("Kitopia.Missing." + Guid.NewGuid().ToString("N"));
            Assert.IsNull(xml);
            Assert.HasCount(0, exceptions, "A missing task is normal and must not cause first-chance file-not-found exceptions.");
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= capture;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeAutoStartAsync_SettingsOverlapReconciliation_ExplicitSettingsWin(bool settingsFirst)
    {
        var service = new StartupApplicationService
        {
            Command = $"\"{Environment.ProcessPath}\"",
            FirstTaskReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            ContinueFirstTaskRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var first = service.ChangeAutoStartAsync(true, settingsFirst, settingsFirst);
        await service.FirstTaskReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.ChangeAutoStartAsync(true, !settingsFirst, !settingsFirst);
        try
        {
            Assert.AreEqual(1, service.TaskReads, "The second request must wait for the first transaction.");
            Assert.IsFalse(second.IsCompleted);
        }
        finally
        {
            service.ContinueFirstTaskRead.SetResult();
        }
        Assert.IsTrue(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsNull(service.Command);
        Assert.IsNotNull(service.TaskXml);
        Assert.AreEqual(settingsFirst ? 1 : 2, service.TaskReads);
    }

    [TestMethod]
    public void CreateStartupTaskXml_WindowsTaskDefinition_ValidatesWithoutRegisteringATask()
    {
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
        dynamic? definition = null;
        try
        {
            scheduler.Connect();
            definition = scheduler.NewTask(0);
            var xml = ApplicationService.CreateStartupTaskXml("S-1-5-21-123", Environment.ProcessPath!);
            definition.XmlText = xml;
            Assert.IsTrue(ApplicationService.StartupTaskMatches((string)definition.XmlText, xml));
        }
        finally
        {
            if (definition is not null) Marshal.FinalReleaseComObject(definition);
            Marshal.FinalReleaseComObject(scheduler);
        }
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 1)]
    [DataRow(0, 2)]
    [DataRow(1, 0)]
    [DataRow(1, 1)]
    [DataRow(1, 2)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    [DataRow(2, 2)]
    public async Task ChangeAutoStartAsync_ModeTransitions_LeavesExactlyTheRequestedEntry(int from, int to)
    {
        var service = new StartupApplicationService();
        Assert.IsTrue(await service.ChangeAutoStartAsync(from != 0, from == 2, true));
        service.Operations.Clear();
        Assert.IsTrue(await service.ChangeAutoStartAsync(to != 0, to == 2, true));
        Assert.AreEqual(to == 1, service.Command is not null);
        Assert.AreEqual(to == 2, service.TaskXml is not null);
        if (from == to) Assert.HasCount(0, service.Operations);
        if (from == 1 && to == 2) CollectionAssert.AreEqual(new[] { "task:create:True", "run:delete" }, service.Operations);
        if (from == 2 && to == 1) CollectionAssert.AreEqual(new[] { "run:create", "task:delete:True" }, service.Operations);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeAutoStartAsync_TaskCreationFailsOrUacCancelled_PreservesNormalStartup(bool allowElevation)
    {
        var service = new StartupApplicationService { Command = "original", FailTaskWrite = true };
        Assert.IsFalse(await service.ChangeAutoStartAsync(true, true, allowElevation));
        Assert.AreEqual("original", service.Command);
        Assert.IsNull(service.TaskXml);
        CollectionAssert.AreEqual(new[] { "task:create:" + allowElevation }, service.Operations);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeAutoStartAsync_TaskRemovalFails_RestoresPreviousRunEntry(bool keepNormalStartup)
    {
        var service = new StartupApplicationService();
        Assert.IsTrue(await service.ChangeAutoStartAsync(true, true, true));
        var previousTask = service.TaskXml;
        service.FailTaskWrite = true;
        Assert.IsFalse(await service.ChangeAutoStartAsync(keepNormalStartup, false, true));
        Assert.IsNull(service.Command);
        Assert.AreEqual(previousTask, service.TaskXml);
    }

    [TestMethod]
    public async Task ChangeAutoStartAsync_RunRemovalFails_RestoresPreviousTaskAndCommand()
    {
        var service = new StartupApplicationService { Command = "original", FailCommandWriteOnce = true };
        Assert.IsFalse(await service.ChangeAutoStartAsync(true, true, true));
        Assert.AreEqual("original", service.Command);
        Assert.IsNull(service.TaskXml);
        CollectionAssert.AreEqual(new[] { "task:create:True", "run:delete", "run:create", "task:delete:True" }, service.Operations);
    }

    [TestMethod]
    public async Task ChangeAutoStartAsync_Reconciliation_UpdatesOldExecutableWithoutElevation()
    {
        var service = new StartupApplicationService
        {
            TaskXml = ApplicationService.CreateStartupTaskXml("S-1-5-21-123", @"C:\old\Kitopia.exe")
        };
        Assert.IsTrue(await service.ChangeAutoStartAsync(true, true, false));
        Assert.IsTrue(ApplicationService.StartupTaskMatches(service.TaskXml!,
            ApplicationService.CreateStartupTaskXml("S-1-5-21-123", Environment.ProcessPath!)));
        CollectionAssert.AreEqual(new[] { "task:create:False" }, service.Operations);
    }

    [TestMethod]
    public void CreateStartupTaskXml_InteractiveLogon_UsesOriginalUserNormalPermissionsAndEscapedPath()
    {
        var executable = @"C:\Apps & Tools\Kitopia\Kitopia.Desktop.exe";
        var xml = ApplicationService.CreateStartupTaskXml("S-1-5-21-123", executable);
        var task = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.AreEqual(executable, task.Descendants(ns + "Command").Single().Value);
        Assert.AreEqual(Path.GetDirectoryName(executable), task.Descendants(ns + "WorkingDirectory").Single().Value);
        Assert.IsTrue(task.Descendants(ns + "UserId").All(user => user.Value == "S-1-5-21-123"));
        Assert.AreEqual("InteractiveToken", task.Descendants(ns + "LogonType").Single().Value);
        Assert.AreEqual("LeastPrivilege", task.Descendants(ns + "RunLevel").Single().Value);
        Assert.AreEqual("6", task.Descendants(ns + "Priority").Single().Value);
        Assert.AreEqual("PT0S", task.Descendants(ns + "ExecutionTimeLimit").Single().Value);
        Assert.AreEqual("false", task.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.AreEqual("false", task.Descendants(ns + "StopIfGoingOnBatteries").Single().Value);
        Assert.IsFalse(task.Descendants(ns + "Delay").Any());
        Assert.IsTrue(ApplicationService.StartupTaskMatches(xml, xml));
        var delayed = XDocument.Parse(xml);
        delayed.Descendants(ns + "LogonTrigger").Single().Add(new XElement(ns + "Delay", "PT30S"));
        Assert.IsFalse(ApplicationService.StartupTaskMatches(delayed.ToString(), xml));
        Assert.IsFalse(ApplicationService.StartupTaskMatches(xml.Replace("LeastPrivilege", "HighestAvailable"), xml));
    }

    private sealed class StartupApplicationService : ApplicationService
    {
        public Task<string?> ReadNativeStartupTaskAsync(string taskName) => base.ReadStartupTaskAsync(taskName);
        public string? Command { get; set; }
        public string? TaskXml { get; set; }
        public bool FailTaskWrite { get; set; }
        public bool FailCommandWriteOnce { get; set; }
        public TaskCompletionSource? FirstTaskReadStarted { get; init; }
        public TaskCompletionSource? ContinueFirstTaskRead { get; init; }
        public int TaskReads { get; private set; }
        public List<string> Operations { get; } = [];
        protected override string GetStartupUserId() => "S-1-5-21-123";
        protected override string? ReadStartupCommand() => Command;
        protected override async Task<string?> ReadStartupTaskAsync(string taskName)
        {
            TaskReads++;
            if (TaskReads == 1 && ContinueFirstTaskRead is not null)
            {
                FirstTaskReadStarted!.SetResult();
                await ContinueFirstTaskRead.Task;
            }
            return TaskXml;
        }
        protected override void WriteStartupCommand(string? command)
        {
            Operations.Add(command is null ? "run:delete" : "run:create");
            if (FailCommandWriteOnce)
            {
                FailCommandWriteOnce = false;
                throw new UnauthorizedAccessException();
            }
            Command = command;
        }
        protected override Task<bool> WriteStartupTaskAsync(string taskName, string? xml, bool allowElevation)
        {
            Assert.AreEqual("Kitopia.Startup.S-1-5-21-123", taskName);
            Operations.Add($"task:{(xml is null ? "delete" : "create")}:{allowElevation}");
            if (FailTaskWrite) return Task.FromResult(false);
            TaskXml = xml;
            return Task.FromResult(true);
        }
    }
}
