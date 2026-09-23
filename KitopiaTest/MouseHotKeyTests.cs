using System.Collections.Concurrent;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.ViewModel.Pages;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.HotKey;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Platform.Windows;
using Kitopia.Desktop.Controls;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using SharpHook;
using SharpHook.Data;
using MouseButton = Avalonia.Input.MouseButton;
using HookMouseButton = SharpHook.Data.MouseButton;

namespace KitopiaTest;

[TestClass]
[DoNotParallelize]
public sealed class MouseHotKeyTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    public Task Register_MouseHotkey_CanDisableAndEnableAgain() => RunAsync(service =>
    {
        var model = new HotKeyModel { Type = HotKeyType.Mouse, IsEnabled = true, MouseButton = 1 };
        Assert.IsTrue(service.Register(model, _ => { }));
        Assert.IsTrue(service.IsActive(model.UUID));
        Assert.IsTrue(service.UnRegister(model.UUID));
        Assert.IsFalse(service.IsActive(model.UUID));
        model.IsEnabled = true;
        Assert.IsTrue(service.Modify(model));
        Assert.IsTrue(service.IsActive(model.UUID));
        Assert.IsTrue(service.Remove(model.UUID));
        Assert.IsNull(service.GetByUuid(model.UUID));
        return Task.CompletedTask;
    });

    [TestMethod]
    [DataRow(HookMouseButton.Button1, MouseHookType.LeftButton)]
    [DataRow(HookMouseButton.Button2, MouseHookType.RightButton)]
    [DataRow(HookMouseButton.Button3, MouseHookType.MiddleButton)]
    [DataRow(HookMouseButton.Button4, MouseHookType.XButton1)]
    [DataRow(HookMouseButton.Button5, MouseHookType.XButton2)]
    public Task MousePressed_UsesConfiguredButtonAndReleaseCancelsHold(HookMouseButton button, MouseHookType configuredButton)
        => RunAsync(service =>
        {
            var model = new HotKeyModel
            {
                Type = HotKeyType.Mouse, IsEnabled = true, MouseButton = (ushort)configuredButton, PressTimeMillis = 5000
            };
            service.Register(model, _ => Assert.Fail("A released mouse button must not trigger."));
            var info = GetHotkey(model.UUID);
            var otherButton = button == HookMouseButton.Button1 ? HookMouseButton.Button2 : HookMouseButton.Button1;
            SendMouseEvent(otherButton, true);
            Assert.IsNull(info.Timer);
            SendMouseEvent(button, true);
            Assert.IsNotNull(info.Timer);
            Assert.IsTrue(info.Timer.IsEnabled);
            SendMouseEvent(otherButton, false);
            Assert.IsTrue(info.Timer.IsEnabled);
            SendMouseEvent(button, false);
            Assert.IsFalse(info.Timer.IsEnabled);
            return Task.CompletedTask;
        });

    [TestMethod]
    public Task Modify_MouseHoldDuration_ReplacesPendingTimer() => RunAsync(service =>
    {
        var model = new HotKeyModel
        {
            Type = HotKeyType.Mouse, IsEnabled = true, MouseButton = 1, PressTimeMillis = 5000
        };
        service.Register(model, _ => { });
        SendMouseEvent(HookMouseButton.Button1, true);
        var oldTimer = GetHotkey(model.UUID).Timer!;
        model.PressTimeMillis = 2000;
        Assert.IsTrue(service.Modify(model));
        Assert.IsFalse(oldTimer.IsEnabled);
        SendMouseEvent(HookMouseButton.Button1, true);
        Assert.AreEqual(TimeSpan.FromMilliseconds(2000), GetHotkey(model.UUID).Timer!.Interval);
        SendMouseEvent(HookMouseButton.Button1, false);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task MousePressed_HeldUntilThreshold_InvokesRegisteredAction() => RunAsync(async service =>
    {
        var invoked = new TaskCompletionSource<HotKeyModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new HotKeyModel
        {
            Type = HotKeyType.Mouse, IsEnabled = true, MouseButton = 1, PressTimeMillis = 100
        };
        service.Register(model, hotkey => invoked.TrySetResult(hotkey));
        ConfigManger.Configs["KitopiaConfig"] = new KitopiaConfig { mouseCapture = true };
        try
        {
            RaiseMouseEvent(HookMouseButton.Button1, true);
            Assert.AreSame(model, await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsFalse(GetHotkey(model.UUID).Timer!.IsEnabled);
        }
        finally
        {
            ConfigManger.Configs.Clear();
        }
    });

    [TestMethod]
    public Task UnRegister_AfterModelChangesToKeyboard_UsesOriginalRegistration() => RunAsync(service =>
    {
        var model = new HotKeyModel { Type = HotKeyType.Mouse, IsEnabled = true, MouseButton = 1 };
        service.Register(model, _ => { });
        model.Type = HotKeyType.Keyboard;
        Assert.IsTrue(service.UnRegister(model.UUID));
        Assert.IsFalse(service.IsActive(model.UUID));
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Modify_DisabledShortcut_UpdatesScopeWithoutEnabling() => RunAsync(service =>
    {
        var model = new HotKeyModel { Type = HotKeyType.Keyboard, IsEnabled = false, SelectKey = EKey.K };
        service.Register(model, _ => Assert.Fail("Disabled shortcut must not fire."));
        model.ProcessScope = HotKeyProcessScope.Include;
        model.ProcessNames = ["explorer"];
        Assert.IsTrue(service.Modify(model));
        Assert.IsFalse(service.IsActive(model.UUID));
        Assert.AreEqual(HotKeyProcessScope.Include, service.GetByUuid(model.UUID)!.ProcessScope);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task KeyboardHook_ExcludedProcess_PassesThroughAndIncludedRuleTriggersOnce() => RunAsync(service =>
    {
        var invoked = 0;
        var model = new HotKeyModel
        {
            Type = HotKeyType.Keyboard, IsEnabled = true, SelectKey = EKey.空格,
            ProcessScope = HotKeyProcessScope.Include, ProcessNames = ["kitopia-nonexistent-process"]
        };
        service.Register(model, _ => invoked++);
        var pressed = typeof(HotKeyImpl).GetMethod("OnKeyPressed", BindingFlags.NonPublic | BindingFlags.Static)!;
        var released = typeof(HotKeyImpl).GetMethod("OnKeyReleased", BindingFlags.NonPublic | BindingFlags.Static)!;
        KeyboardHookEventArgs Key() => new(new UioHookEvent
        {
            Keyboard = new KeyboardEventData { KeyCode = KeyCode.VcSpace, RawCode = (ushort)EKey.空格 }
        });
        var blocked = Key();
        pressed.Invoke(null, [null, blocked]);
        Dispatcher.UIThread.RunJobs();
        Assert.IsFalse(blocked.SuppressEvent);
        Assert.AreEqual(0, invoked);
        model.ProcessScope = HotKeyProcessScope.Exclude;
        service.Modify(model);
        var allowed = Key();
        pressed.Invoke(null, [null, allowed]);
        pressed.Invoke(null, [null, Key()]);
        Dispatcher.UIThread.RunJobs();
        Assert.IsTrue(allowed.SuppressEvent);
        Assert.AreEqual(1, invoked);
        var release = Key();
        released.Invoke(null, [null, release]);
        Assert.IsTrue(release.SuppressEvent);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task MousePressed_OutsideProcessScope_DoesNotStartHold() => RunAsync(service =>
    {
        var model = new HotKeyModel
        {
            Type = HotKeyType.Mouse, IsEnabled = true, MouseButton = 1,
            ProcessScope = HotKeyProcessScope.Include, ProcessNames = ["kitopia-nonexistent-process"]
        };
        service.Register(model, _ => Assert.Fail("Out-of-scope shortcut must not fire."));
        SendMouseEvent(HookMouseButton.Button1, true);
        Assert.IsNull(GetHotkey(model.UUID).Timer);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task CompactControl_EditDisabledShortcut_OpensEditorWithoutEnabling() => RunAsync(_ =>
    {
        var model = new HotKeyModel
        {
            IsEnabled = false, SelectKey = EKey.空格, ProcessScope = HotKeyProcessScope.Include, ProcessNames = ["explorer"]
        };
        var recorder = new RecordingHotkeys { Existing = model };
        var originalServices = ServiceManager.Services;
        using var services = new ServiceCollection().AddSingleton<IHotKetImpl>(recorder).BuildServiceProvider();
        try
        {
            ServiceManager.Services = services;
            var control = new HotKeyShow { HotKeyModel = model };
            control.EditHotKey.Execute(null);
            Assert.AreEqual(model.UUID, recorder.RequestedUuid);
            Assert.IsFalse(model.IsEnabled);
            Assert.IsNull(recorder.Modified);
            StringAssert.Contains(control.ScopeDescription, "explorer");
            StringAssert.Contains(control.ScopeDescription, "已停用");
            control.ToggleHotKey.Execute(null);
            Assert.IsTrue(model.IsEnabled);
            Assert.AreNotSame(model, recorder.Modified);
            Assert.AreEqual(model.UUID, recorder.Modified!.UUID);
            Assert.IsFalse(control.ScopeDescription.Contains("已停用"));
        }
        finally { ServiceManager.Services = originalServices; }
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Editor_ProcessNameInput_DoesNotReplaceShortcut() => RunAsync(_ =>
    {
        var recorder = new RecordingHotkeys();
        var originalServices = ServiceManager.Services;
        using var services = new ServiceCollection().AddSingleton<IHotKetImpl>(recorder).BuildServiceProvider();
        var model = new HotKeyModel { IsEnabled = false, SelectKey = EKey.K };
        var window = new HotKeyEditorWindow(model);
        try
        {
            ServiceManager.Services = services;
            window.Show();
            window.FindControl<ComboBox>("ProcessScope")!.SelectedIndex = (int)HotKeyProcessScope.Include;
            window.FindControl<TextBox>("ProcessNames")!.Focus();
            window.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, "e");
            window.FindControl<TextBox>("ProcessNames")!.Text = "explorer.exe";
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsNotNull(recorder.Modified);
            Assert.AreEqual(EKey.K, recorder.Modified.SelectKey);
            Assert.IsFalse(recorder.Modified.IsEnabled);
        }
        finally
        {
            window.Close();
            ServiceManager.Services = originalServices;
        }
        return Task.CompletedTask;
    });

    [TestMethod]
    [DataRow(HotKeyType.Keyboard, HotKeyType.Mouse)]
    [DataRow(HotKeyType.Mouse, HotKeyType.Mouse)]
    [DataRow(HotKeyType.Mouse, HotKeyType.Keyboard)]
    public Task Editor_Save_PersistsSelectedTypeButtonAndDuration(HotKeyType originalType, HotKeyType selectedType) => RunAsync(_ =>
    {
        var recorder = new RecordingHotkeys();
        var originalServices = ServiceManager.Services;
        using var services = new ServiceCollection().AddSingleton<IHotKetImpl>(recorder).BuildServiceProvider();
        var model = new HotKeyModel { Type = originalType, MouseButton = 1, SelectKey = EKey.Q };
        var window = new HotKeyEditorWindow(model);
        try
        {
            ServiceManager.Services = services;
            window.Show();
            var selectedOption = window.FindControl<RadioButton>(selectedType == HotKeyType.Mouse ? "Mouse" : "KeyBoard")!;
            selectedOption.IsChecked = true;
            selectedOption.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.FindControl<Slider>("Slider")!.Value = 2300;
            window.FindControl<ComboBox>("ProcessScope")!.SelectedIndex = (int)HotKeyProcessScope.Exclude;
            window.FindControl<TextBox>("ProcessNames")!.Text = "explorer.exe, CODE.exe; explorer";
            window.UpdateLayout();
            if (selectedType == HotKeyType.Mouse)
            {
                var area = window.FindControl<Border>("MouseCaptureArea")!;
                var point = area.TranslatePoint(new Point(area.Bounds.Width / 2, area.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.XButton1);
                window.MouseUp(point, MouseButton.XButton1);
            }
            else
            {
                window.KeyPress(Key.K, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.K, "K");
            }
            var save = window.FindControl<Button>("SaveButton")!;
            var savePoint = save.TranslatePoint(new Point(save.Bounds.Width / 2, save.Bounds.Height / 2), window)!.Value;
            window.MouseDown(savePoint, MouseButton.Left);
            window.MouseUp(savePoint, MouseButton.Left);
            Assert.IsNotNull(recorder.Modified);
            Assert.AreEqual(selectedType, recorder.Modified.Type);
            Assert.AreEqual(HotKeyProcessScope.Exclude, recorder.Modified.ProcessScope);
            CollectionAssert.AreEqual(new[] { "explorer", "CODE" }, recorder.Modified.ProcessNames);
            if (selectedType == HotKeyType.Mouse)
            {
                Assert.AreEqual((ushort)MouseHookType.XButton1, recorder.Modified.MouseButton);
                Assert.AreEqual((ushort)2300, recorder.Modified.PressTimeMillis);
            }
            else
            {
                Assert.AreEqual(EKey.K, recorder.Modified.SelectKey);
                Assert.IsTrue(recorder.Modified.IsSelectCtrl);
                Assert.IsTrue(recorder.Modified.IsSelectShift);
            }
            Assert.IsFalse(window.IsVisible);
        }
        finally
        {
            window.Close();
            ServiceManager.Services = originalServices;
        }
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Editor_ProcessTags_AddRemoveAndMerge_SavesCorrectly() => RunAsync(_ =>
    {
        var recorder = new RecordingHotkeys();
        var originalServices = ServiceManager.Services;
        using var services = new ServiceCollection().AddSingleton<IHotKetImpl>(recorder).BuildServiceProvider();
        var model = new HotKeyModel
        {
            IsEnabled = true,
            SelectKey = EKey.A,
            ProcessScope = HotKeyProcessScope.Include,
            ProcessNames = ["notepad.exe"]
        };
        var window = new HotKeyEditorWindow(model);
        try
        {
            ServiceManager.Services = services;
            window.Show();
            var tagsControl = window.FindControl<ItemsControl>("ProcessTagsControl")!;
            Assert.IsNotNull(tagsControl.ItemsSource);
            var initialTags = ((System.Collections.IEnumerable)tagsControl.ItemsSource).Cast<string>().ToList();
            CollectionAssert.AreEqual(new[] { "notepad" }, initialTags);

            var input = window.FindControl<TextBox>("ProcessNames")!;
            var addButton = window.FindControl<Button>("AddProcessButton")!;
            input.Text = "custom-app.exe";
            addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var updatedTags = ((System.Collections.IEnumerable)tagsControl.ItemsSource).Cast<string>().ToList();
            CollectionAssert.AreEqual(new[] { "notepad", "custom-app" }, updatedTags);
            Assert.AreEqual(string.Empty, input.Text);

            // Also verify uncommitted text in TextBox is merged on save
            input.Text = "pending-app.exe";
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.IsNotNull(recorder.Modified);
            CollectionAssert.AreEqual(new[] { "notepad", "custom-app", "pending-app" }, recorder.Modified.ProcessNames);
        }
        finally
        {
            window.Close();
            ServiceManager.Services = originalServices;
        }
        return Task.CompletedTask;
    });

    private static HotKeyImpl.HotkeyInfo GetHotkey(string uuid) =>
        ((ConcurrentDictionary<string, HotKeyImpl.HotkeyInfo>)typeof(HotKeyImpl)
            .GetField("HotKeys", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!)[uuid];

    private static void SendMouseEvent(HookMouseButton button, bool pressed)
    {
        ConfigManger.Configs["KitopiaConfig"] = new KitopiaConfig { mouseCapture = true };
        try
        {
            RaiseMouseEvent(button, pressed);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            ConfigManger.Configs.Clear();
        }
    }

    [TestMethod]
    public Task Modify_NewSettings_CommitsOnceAndKeepsBoundModel() => RunAsync(service =>
    {
        var messages = new List<HotKeyChanged>();
        WeakReferenceMessenger.Default.Register<HotKeyChanged>(messages, static (recipient, message) =>
            ((List<HotKeyChanged>)recipient).Add(message));
        try
        {
            var model = new HotKeyModel { Type = HotKeyType.Mouse, MouseButton = 1, IsEnabled = true };
            service.Register(model, _ => { });
            messages.Clear();
            var candidate = new HotKeyModel(model) { MouseButton = 2, PressTimeMillis = 750 };
            Assert.IsTrue(service.Modify(candidate));
            Assert.AreSame(model, service.GetByUuid(model.UUID));
            Assert.AreEqual((ushort)2, model.MouseButton);
            Assert.AreEqual((ushort)750, model.PressTimeMillis);
            Assert.HasCount(1, messages);
            Assert.AreEqual(HotKeyChangeKind.Updated, messages[0].Kind);
        }
        finally { WeakReferenceMessenger.Default.UnregisterAll(messages); }
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Modify_InvalidCandidate_RestoresOriginalRegistration() => RunAsync(service =>
    {
        var model = new HotKeyModel { Type = HotKeyType.Mouse, MouseButton = 1, IsEnabled = true };
        service.Register(model, _ => { });
        var candidate = new HotKeyModel(model) { Type = HotKeyType.Keyboard, SelectKey = EKey.未设置 };
        Assert.IsFalse(service.Modify(candidate));
        Assert.AreSame(model, service.GetByUuid(model.UUID));
        Assert.AreEqual(HotKeyType.Mouse, model.Type);
        Assert.AreEqual((ushort)1, model.MouseButton);
        Assert.IsTrue(model.IsEnabled);
        Assert.AreEqual(0, GetHotkey(model.UUID).Id);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task RegisterAndRemove_OpenPage_UpdatesExistingCollectionAndPreservesEnabledIntent() => RunAsync(service =>
    {
        var previousServices = ServiceManager.Services;
        using var provider = new ServiceCollection().AddSingleton<IHotKetImpl>(service).BuildServiceProvider();
        ServiceManager.Services = provider;
        var viewModel = new HotKeyManagerPageViewModel();
        var collection = viewModel.KeyModels;
        try
        {
            var model = new HotKeyModel { Type = HotKeyType.Mouse, MouseButton = 1, IsEnabled = true };
            Assert.IsTrue(service.Register(model, _ => { }));
            Assert.IsTrue(collection.Contains(model));
            Assert.AreSame(collection, viewModel.KeyModels);
            Assert.IsTrue(service.Remove(model.UUID));
            Assert.IsFalse(collection.Contains(model));
            Assert.IsTrue(model.IsEnabled);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(viewModel);
            ServiceManager.Services = previousServices;
        }
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Modify_UninitializedScenario_DoesNotBreakNotification() => RunAsync(service =>
    {
        using var scenario = new Kitopia.Desktop.Features.CustomScenario.CustomScenario();
        var model = new HotKeyModel { Type = HotKeyType.Mouse, MouseButton = 1, IsEnabled = true };
        service.Register(model, _ => { });
        Assert.IsTrue(service.Modify(new HotKeyModel(model) { MouseButton = 2 }));
        return Task.CompletedTask;
    });

    private static void RaiseMouseEvent(HookMouseButton button, bool pressed) => typeof(HotKeyImpl)
        .GetMethod(pressed ? "OnMousePressed" : "OnMouseReleased", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, [null, new MouseHookEventArgs(new UioHookEvent { Mouse = new MouseEventData { Button = button } })]);

    private static async Task RunAsync(Func<HotKeyImpl, Task> test)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch(async () =>
        {
            var originalConfigs = ConfigManger.Configs;
            ConfigManger.Configs = new Dictionary<string, ConfigBase>();
            var service = new HotKeyImpl();
            try
            {
                await test(service);
            }
            finally
            {
                ConfigManger.Configs.Clear();
                foreach (var model in service.GetAllRegistered()) service.Remove(model.UUID);
                ConfigManger.Configs = originalConfigs;
            }
        }, CancellationToken.None);
    }

    private sealed class RecordingHotkeys : IHotKetImpl
    {
        public HotKeyModel? Existing { get; init; }
        public string? RequestedUuid { get; private set; }
        public HotKeyModel? Modified { get; private set; }
        public bool Modify(HotKeyModel model) { Modified = model; Existing?.ApplySettings(model); return true; }
        public void Init() => throw new NotSupportedException();
        public void StartHook() => throw new NotSupportedException();
        public bool Register(HotKeyModel model, Action<HotKeyModel> callback, bool initHotKey = true) => throw new NotSupportedException();
        public bool UnRegister(string uuid) => throw new NotSupportedException();
        public bool Remove(string uuid) => throw new NotSupportedException();
        public bool RequestUserModify(string uuid) { RequestedUuid = uuid; return true; }
        public HotKeyModel? GetByUuid(string uuid) => Existing;
        public bool IsActive(string uuid) => throw new NotSupportedException();
        public IEnumerable<HotKeyModel> GetAllRegistered() => throw new NotSupportedException();
    }
}
