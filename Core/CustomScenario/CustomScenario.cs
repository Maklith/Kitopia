#region

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services;
using Core.Services.HotKey;
using Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;

#endregion

namespace Core.CustomScenario;

public partial class CustomScenario : ObservableRecipient
{
    private static ILogger Log = LogManager.Logger.ForContext<CustomScenario>();

    [JsonIgnore] [ObservableProperty] private ObservableCollection<string> _autoTriggers = new();
    private CancellationTokenSource _cancellationTokenSource = new();

    [JsonIgnore] [ObservableProperty] private string _description = "";

    [property: JsonIgnore] [JsonIgnore] [ObservableProperty]
    private Bitmap? _icon;

    private Dictionary<ScenarioNodeBase, Thread?> _initTasks = new();

    [JsonIgnore] [ObservableProperty] private bool _isRunning = false;

    [JsonIgnore] [ObservableProperty] private DateTime _lastRun;

    [JsonIgnore] [ObservableProperty] private string _name = "情景";

    private Dictionary<ScenarioNodeBase, Thread?> _tickTasks = new();
    private TickUtil? _tickUtil;

    /// <summary>
    ///     手动执行
    /// </summary>
    [JsonIgnore] [ObservableProperty] private bool executionManual = true;

    [JsonIgnore] [ObservableProperty] private bool hasInit = true;
    [JsonIgnore] [ObservableProperty] private string? initError;
    [JsonIgnore] [ObservableProperty] private ObservableDictionary<string, CustomScenarioValue> inputValue = new();
    private bool InTick;

    [JsonIgnore] [ObservableProperty] private bool isHaveInputValue = false;

    [JsonIgnore] [ObservableProperty] private ObservableCollection<string> keys = new();

    //ActiveHotKey
    [JsonIgnore] [ObservableProperty] public HotKeyModel runHotKey;

    [JsonIgnore] [ObservableProperty] public HotKeyModel stopHotKey;
    [JsonIgnore] [ObservableProperty] private ObservableDictionary<string, CustomScenarioValue> tempValue = new();


    [JsonIgnore] [ObservableProperty] private double? tickIntervalSecond = 5;

    [JsonIgnore] [ObservableProperty] private ObservableDictionary<string, CustomScenarioValue> values = new();

    public CustomScenario()
    {
        PropertyChanged += PropertyChangedEventHandler();
        runHotKey = new HotKeyModel
        {
            MainName = "Kitopia情景", Name = $"{UUID}_开始快捷键", IsSelectCtrl = false, IsSelectAlt = false,
            IsSelectWin = false,
            IsSelectShift = false, SelectKey = EKey.未设置
        };

        stopHotKey = new HotKeyModel
        {
            MainName = "Kitopia情景", Name = $"{UUID}_停止快捷键", IsSelectCtrl = false, IsSelectAlt = false,
            IsSelectWin = false,
            IsSelectShift = false, SelectKey = EKey.未设置
        };
        InitHotKey();

        WeakReferenceMessenger.Default.Register<string, string>(this, "hotkey", (recipient, message) =>
        {
            if (stopHotKey.UUID == message)
            {
                stopHotKey = HotKeyManager.HotKetImpl.GetByUuid(message).Value;
                CustomScenarioManger.Save(this);
            }

            if (runHotKey.UUID == message)
            {
                runHotKey = HotKeyManager.HotKetImpl.GetByUuid(message).Value;
                CustomScenarioManger.Save(this);
            }
        });
        InputValue.CollectionChanged += OnInputValueOnCollectionChanged;
    }

    public string UUID { get; init; } = Guid.NewGuid()
        .ToString();


    public ObservableCollection<ScenarioNodeBase> nodes { get; set; } = new();

    public ObservableCollection<ConnectionItem> connections { get; set; } = new();
    public event EventHandler Saved;

    internal void NotifySaved()
    {
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void InitHotKeyUiCommand(HotKeyModel? hotKeyModel)
    {
        if (hotKeyModel == null) return;
        if (hotKeyModel.Value.UUID == RunHotKey.UUID)
            HotKeyManager.HotKetImpl.Add(hotKeyModel.Value, e => Run(), false);

        if (hotKeyModel.Value.UUID == StopHotKey.UUID)
            HotKeyManager.HotKetImpl.Add(hotKeyModel.Value, e => Stop(), false);
    }

    public void InitHotKey()
    {
        if (RunHotKey.IsEnabled)
            if (!HotKeyManager.HotKetImpl.Add(RunHotKey, e => Run()))
                ServiceManager.Services.GetService<IContentDialog>()!.ShowDialogAsync(null, new DialogContent
                {
                    Title = $"快捷键{RunHotKey.SignName}设置失败",
                    Content = "请重新设置快捷键，按键与系统其他程序冲突",
                    CloseButtonText = "关闭"
                });

        if (StopHotKey.IsEnabled)
            if (!HotKeyManager.HotKetImpl.Add(StopHotKey, e => Stop()))
                ServiceManager.Services.GetService<IContentDialog>().ShowDialogAsync(null, new DialogContent
                {
                    Title = $"快捷键{StopHotKey.SignName}设置失败",
                    Content = "请重新设置快捷键，按键与系统其他程序冲突",
                    CloseButtonText = "关闭"
                });
    }

    public void UnRegisterHotKey()
    {
        HotKeyManager.HotKetImpl.DeleteCompletely(RunHotKey.UUID);
        HotKeyManager.HotKetImpl.DeleteCompletely(StopHotKey.UUID);
    }


    private void OnInputValueOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        var outputs = ((ScenarioMethodNode)nodes.First())
            .Output;
        for (var i = outputs.Count - 1; i >= 1; i--) outputs.RemoveAt(i);

        if (sender is ObservableDictionary<string, object> dictionary)
            foreach (var (key, value) in dictionary)
                outputs.Add(new ConnectorItem
                {
                    ConnectorType = ConnectorType.Output,
                    Source = nodes.First(),
                    InputObject = new CustomScenarioValue { Type = value.GetType() },
                    Title = key
                });
    }

    private PropertyChangedEventHandler? PropertyChangedEventHandler()
    {
        return (e, s) =>
        {
            if (s.PropertyName == nameof(IsRunning)) return;

            WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg
                { Type = 1, Name = nameof(e), CustomScenario = this });
        };
    }

    public void Dispose()
    {
        try
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
        catch (Exception e)
        {
        }

        _tickTasks = null;
        _initTasks = null;
        nodes.Clear();
        //Log.Debug(Name + " Dispose");
    }

    partial void OnTickIntervalSecondChanged(double? oldValue)
    {
        if (oldValue is null) TickIntervalSecond = 0.1;
    }


    public void Run(bool realTime = false, bool onExit = false, params object[] inputValues)
    {
        if (IsHaveInputValue)
            if (inputValues.Length != InputValue.Count)
                return;

        StartRun(!realTime, onExit, inputValues);
    }

    private void StartRun(bool notRealTime, bool onExit = false, params object[] inputValues)
    {
        if (IsRunning || !HasInit) return;

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        if (notRealTime)
        {
            IsRunning = true;
            LastRun = DateTime.Now;
            CustomScenarioManger.Save(this);
        }


        foreach (var task in _initTasks) task.Value?.Join();

        foreach (var task in _tickTasks) task.Value?.Join();

        _initTasks.Clear();
        _tickTasks.Clear();
        if (notRealTime)
        {
            foreach (var pointItem in nodes) pointItem.ResetData();

            for (var i = 0; i < inputValues.Length; i++)
                ((ScenarioMethodNode)nodes[0])!
                    .Output[i + 1].InputObject!.Value = inputValues[i];
        }

        for (var i = nodes.Count - 1; i >= 1; i--)
            if (!nodes[i].IsUsed(connections))
                nodes[i].Status = NodeStatus.Unverified;

        try
        {
            //_initTasks.Add( nodes[0], null);
            ParsePointItem(_initTasks, nodes[0], false, notRealTime, _cancellationTokenSource.Token);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        //监听任务是否结束
        if (notRealTime)
            new Task(() =>
            {
                while (true)
                {
                    Thread.Sleep(100);
                    var f = true;
                    foreach (var (_, value) in _initTasks)
                    {
                        if (value is null) continue;

                        if (!value.IsAlive) continue;

                        f = false;
                        break;
                    }

                    if (!f) continue;

                    if (!notRealTime) return;

                    if (_cancellationTokenSource.IsCancellationRequested) return;

                    var connectionItem = nodes[1].GetForwardNodes(connections);
                    if (!connectionItem.Any() || onExit)
                    {
                        //当没有tick时直接结束
                        if (notRealTime) _cancellationTokenSource.Cancel();

                        IsRunning = false;
                        ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show("情景",
                            $"情景\'{Name}\'运行完成");
                        Log.Debug($"情景运行完成:{Name}");
                        break;
                    }

                    ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show("情景",
                        $"情景\'{Name}\'进入Tick");
                    Log.Debug($"情景进入Tick:{Name}");
                    try
                    {
                        _tickUtil = new TickUtil(1000, (uint)(tickIntervalSecond * 1000 * 1000), 1, TickMethod);
                        _tickUtil.Open();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }

                    break;
                }
            }).Start();
    }

    private void TickMethod(object sender, long JumpPeriod, long interval)
    {
        if (InTick) return;

        var nowPointItem = nodes[1];
        ParsePointItem(_tickTasks, nowPointItem, false, true, _cancellationTokenSource.Token);

        while (true)
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested)
            {
                InTick = false;
                _tickUtil.Dispose();
                break;
            }

            Thread.Sleep(100);
            var f = true;
            foreach (var (_, value) in _tickTasks)
            {
                if (value is null) continue;

                if (!value.IsAlive) continue;

                f = false;
                break;
            }

            if (!f) continue;

            //tick完成一次
            InTick = false;
            _tickTasks.Clear();
            break;
        }
    }

    public void Stop(bool inTickError = false)
    {
        if (!IsRunning) return;


        try
        {
            _tickUtil?.Dispose();
            _cancellationTokenSource.Cancel();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        if (!inTickError)
        {
            foreach (var task in _initTasks) task.Value?.Join();

            foreach (var task in _tickTasks) task.Value?.Join();
        }


        _initTasks.Clear();
        _tickTasks.Clear();
        IsRunning = false;
        if (inTickError)
        {
            ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!)
                .Show("情景", $"情景\'{Name}\'由于出现错误被停止");
            Log.Debug($"情景\'{Name}\'由于出现错误被停止");
        }
        else
        {
            ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!)
                .Show("情景", $"情景\'{Name}\'被用户停止");
            Log.Debug($"情景\'{Name}\'被用户停止");
        }
    }


    private void ParsePointItem(Dictionary<ScenarioNodeBase, Thread?> threads,
        ScenarioNodeBase nowScenarioMethodNode, bool onlyBackward,
        bool notRealTime,
        CancellationToken cancellationToken)
    {
        Log.Debug($"解析节点:{nowScenarioMethodNode.Title}");
        var valid = true;
        List<Thread> sourceDataTask = new();
        valid = nowScenarioMethodNode.InputDataIsEnough(connections);
        if (!valid) goto finnish;
        foreach (var sourceSource in nowScenarioMethodNode.GetBackwardNodes(connections))
            lock (threads)
            {
                if (threads.TryGetValue(sourceSource, out var task1))
                {
                    if (task1 is not null) sourceDataTask.Add(task1);
                }
                else
                {
                    var task = new Thread(() =>
                    {
                        ParsePointItem(threads, sourceSource, true, notRealTime, cancellationToken);
                    });

                    // Log.Debug(sourceSource.Title);
                    threads.Add(sourceSource, task);
                    sourceDataTask.Add(task);
                    task.Start();
                }
            }
        //源数据全部生成

        foreach (var thread in sourceDataTask) thread.Join();

        //这是连接当前节点的节点
        if (cancellationToken.IsCancellationRequested) return;

        // foreach (var connectorItem in nowScenarioMethodNode.Input)
        // foreach (var sourceSource in connectorItem.GetSourceOrNextPointItems(connections))
        //     if (sourceSource.Status == NodeStatus.Error)
        //         valid = false;


        if (!valid) goto finnish;


        if (notRealTime)
            try
            {
                Log.Debug($"执行节点:{nowScenarioMethodNode.Title}");
                var invoke = nowScenarioMethodNode.Invoke(cancellationToken, connections, Values, TempValue);
                if (!invoke)
                {
                    //如果执行失败
                    valid = false;
                    nowScenarioMethodNode.Status = NodeStatus.Error;
                    Log.Debug($"执行节点失败:{nowScenarioMethodNode.Title}");
                }
                else
                {
                    Log.Debug($"执行节点完成:{nowScenarioMethodNode.Title}");
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "错误");
                ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show("情景",
                    e.InnerException is not null
                        ? $"情景{Name}出现错误\n{e.InnerException?.Message}"
                        : $"情景{Name}出现错误\n{e.Message}");

                Task.Run(() => { Stop(true); });

                valid = false;
                goto finnish;
            }

        if (cancellationToken.IsCancellationRequested) return;
        finnish:
        if (valid)
        {
            nowScenarioMethodNode.Status = notRealTime ? NodeStatus.Verified : NodeStatus.PreliminaryVerified;
            Log.Debug($"解析节点完成:{nowScenarioMethodNode.Title}");
        }
        else
        {
            nowScenarioMethodNode.Status = NodeStatus.Error;
            Log.Debug($"解析节点失败:{nowScenarioMethodNode.Title}");
        }

        if (!onlyBackward)
            foreach (var nextPointItem in nowScenarioMethodNode.GetForwardNodes(connections))
                lock (threads)
                {
                    if (threads.ContainsKey(nextPointItem)) return;

                    var task = new Thread(() =>
                    {
                        ParsePointItem(threads, nextPointItem, false, notRealTime, cancellationToken);
                    });

                    threads.Add(nextPointItem, task);
                    task.Start();
                }
    }

    public bool IsUseThePlugin(string plugStr)
    {
        var pluginManger = ServiceManager.Services.GetService<IPluginManger>()!;

        return nodes.Any(e => e.IsUseThePlugin(plugStr)) ||
               Enumerable.Any<KeyValuePair<string, CustomScenarioValue>>(InputValue, e =>
                   pluginManger.IsTypeFromThePlugin(e.Value.Type, plugStr) ||
                   pluginManger.IsTypeFromThePlugin(e.Value.RealType, plugStr)) ||
               Enumerable.Any<KeyValuePair<string, CustomScenarioValue>>(Values, e =>
                   pluginManger.IsTypeFromThePlugin(e.Value.Type, plugStr) ||
                   pluginManger.IsTypeFromThePlugin(e.Value.RealType, plugStr));
    }

    public void OnDeserialized() //反序列化时hotkeys的默认值会被添加,需要先清空
    {
        PropertyChanged += PropertyChangedEventHandler();
        InputValue.CollectionChanged += OnInputValueOnCollectionChanged;
    }
}