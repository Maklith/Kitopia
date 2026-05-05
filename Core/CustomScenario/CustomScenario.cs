#region

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services;
using Core.Services.HotKey;
using Core.Services.Interfaces;
using Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;
using Serilog;

#endregion

namespace Core.CustomScenario;

public partial class CustomScenario : ObservableRecipient,IDisposable
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<CustomScenario>();

    [JsonIgnore] [ObservableProperty] private ObservableCollection<string> _autoTriggers = new();
    private CancellationTokenSource _cancellationTokenSource = new();

    [JsonIgnore] [ObservableProperty] private string _description = "";

    [property: JsonIgnore] [JsonIgnore] [ObservableProperty]
    private Bitmap? _icon;

    private Dictionary<ScenarioNodeBase, Thread?> _initTasks = new();

    [JsonIgnore] [ObservableProperty] private bool _isRunning;

    [JsonIgnore] [ObservableProperty] private DateTime _lastRun;

    [JsonIgnore] [ObservableProperty] private string _name = "情景";

    private Dictionary<ScenarioNodeBase, Thread?> _tickTasks = new();
    private TickUtil? _tickUtil;

    /// <summary>
    ///     手动执行
    /// </summary>
    [JsonIgnore] [ObservableProperty] private bool _executionManual = true;

    [JsonIgnore] [ObservableProperty] private bool _hasInit = true;
    [JsonIgnore] [ObservableProperty] private string? _initError;
    [JsonIgnore] [ObservableProperty] private ObservableDictionary<string, CustomScenarioValue> _inputValue = new();
    private bool _inTick;

    [JsonIgnore] [ObservableProperty] private bool _isHaveInputValue;

    [JsonIgnore] [ObservableProperty] private ObservableCollection<string> _keys = new();

    //ActiveHotKey
    [JsonIgnore] [ObservableProperty] private HotKeyModel _runHotKey;

    [JsonIgnore] [ObservableProperty] private HotKeyModel _stopHotKey;
    [JsonIgnore] [ObservableProperty] private ObservableDictionary<string, CustomScenarioValue> _tempValue = new();


    [JsonIgnore] [ObservableProperty] private double? _tickIntervalSecond = 5;

    [JsonIgnore] [ObservableProperty] private ObservableDictionary<string, CustomScenarioValue> _values = new();

    public CustomScenario()
    {
        PropertyChanged += CustomScenarioPropertyChangedEventHandler;
        Nodes.CollectionChanged += (e, s) =>
        {
            if (s.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                if (e is IEnumerable<ScenarioNodeBase> methodNodes)
                {
                    foreach (var scenarioMethodNode in methodNodes)
                    {
                        scenarioMethodNode.PropertyChanged += CustomScenarioPropertyChangedEventHandler;
                        if (scenarioMethodNode is ScenarioMethodNode methodNode)
                        {
                            foreach (var connectorItem in methodNode.Input)
                            {
                                connectorItem.PropertyChanged += CustomScenarioPropertyChangedEventHandler;
                                connectorItem.InputObjectHandler = ((_, _) =>
                                {
                                    WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg
                                    { Type = 0, Name = nameof(e), ConnectorItem = connectorItem,
                                        ScenarioMethodNode = connectorItem.Source as ScenarioMethodNode, CustomScenario = this });

                                });
                                connectorItem.InputObject.PropertyChanged += connectorItem.InputObjectHandler;
                                
                            }
                        }
                    }

                   
                }
            }
            else if (s.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            {
                if (e is IEnumerable<ScenarioNodeBase> methodNodes)
                {
                    foreach (var scenarioMethodNode in methodNodes)
                    {
                        scenarioMethodNode.PropertyChanged -= CustomScenarioPropertyChangedEventHandler;
                        if (scenarioMethodNode is ScenarioMethodNode methodNode)
                        {
                            foreach (var connectorItem in methodNode.Input)
                            {
                                connectorItem.PropertyChanged -= CustomScenarioPropertyChangedEventHandler;
                                if (connectorItem.InputObjectHandler != null)
                                {
                                    connectorItem.InputObject.PropertyChanged -= connectorItem.InputObjectHandler;
                                    connectorItem.InputObjectHandler = null;
                                }
                            }
                        }
                    }

                   
                }
            }

            WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg
                { Type = 0, Name = nameof(Nodes), CustomScenario = this });
        };
        _runHotKey = new HotKeyModel
        {
            MainName = "Kitopia情景", Name = $"{Uuid}_开始快捷键", IsSelectCtrl = false, IsSelectAlt = false,
            IsSelectWin = false,
            IsSelectShift = false, SelectKey = EKey.未设置
        };

        _stopHotKey = new HotKeyModel
        {
            MainName = "Kitopia情景", Name = $"{Uuid}_停止快捷键", IsSelectCtrl = false, IsSelectAlt = false,
            IsSelectWin = false,
            IsSelectShift = false, SelectKey = EKey.未设置
        };
        InitHotKey();

        WeakReferenceMessenger.Default.Register<string, string>(this, "hotkey", (_, message) =>
        {
            if (_stopHotKey.UUID == message)
            {
                _stopHotKey = ServiceManager.Services.GetService<IHotKetImpl>()!.GetByUuid(message).Value;
                CustomScenarioManger.Save(this);
            }

            if (_runHotKey.UUID == message)
            {
                _runHotKey = ServiceManager.Services.GetService<IHotKetImpl>()!.GetByUuid(message).Value;
                CustomScenarioManger.Save(this);
            }
        });
    }

    public string Uuid { get; init; } = Guid.NewGuid()
        .ToString();


    public ObservableCollection<ScenarioNodeBase> Nodes { get; set; } = new();

    public ObservableCollection<ConnectionItem> Connections { get; set; } = new();
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
            ServiceManager.Services.GetService<IHotKetImpl>()!.Add(hotKeyModel.Value, e => Run(), false);

        if (hotKeyModel.Value.UUID == StopHotKey.UUID)
            ServiceManager.Services.GetService<IHotKetImpl>()!.Add(hotKeyModel.Value, e => Stop(), false);
    }

    public void InitHotKey()
    {
        if (RunHotKey.IsEnabled)
            if (!ServiceManager.Services.GetService<IHotKetImpl>()!.Add(RunHotKey, e => Run()))
                ServiceManager.Services.GetService<IToastService>()!.Show(new DialogContent
                {
                    Title = $"快捷键{RunHotKey.SignName}设置失败",
                    Content = "请重新设置快捷键，按键与系统其他程序冲突",
                    CloseButtonText = "关闭"
                }.ToToastRequest());

        if (StopHotKey.IsEnabled)
            if (!ServiceManager.Services.GetService<IHotKetImpl>()!.Add(StopHotKey, e => Stop()))
                ServiceManager.Services.GetService<IToastService>().Show(new DialogContent
                {
                    Title = $"快捷键{StopHotKey.SignName}设置失败",
                    Content = "请重新设置快捷键，按键与系统其他程序冲突",
                    CloseButtonText = "关闭"
                }.ToToastRequest());
    }

    public void UnRegisterHotKey()
    {
        ServiceManager.Services.GetService<IHotKetImpl>()!.DeleteCompletely(RunHotKey.UUID);
        ServiceManager.Services.GetService<IHotKetImpl>()!.DeleteCompletely(StopHotKey.UUID);
    }


    private void CustomScenarioPropertyChangedEventHandler(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsRunning)) return;
        if (s is CustomScenario)
        {
            WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg
                { Type = 1, Name = nameof(e), CustomScenario = this });
        }
        else if (s is ScenarioMethodNode methodNode)
        {
            WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg
                { Type = 0, Name = nameof(e), ScenarioMethodNode = methodNode, CustomScenario = this });
        }
        else if (s is ConnectorItem connectorItem)
        {
            WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg
            { Type = 0, Name = nameof(e), ConnectorItem = connectorItem,
                ScenarioMethodNode = connectorItem.Source as ScenarioMethodNode, CustomScenario = this });
        }
        
        
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
        Nodes.Clear();
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
            foreach (var pointItem in Nodes) pointItem.ResetData();
        }

        for (var i = Nodes.Count - 1; i >= 1; i--)
            if (!Nodes[i].IsUsed(Connections))
                Nodes[i].Status = NodeStatus.Unverified;

        try
        {
            //_initTasks.Add( nodes[0], null);
            ParsePointItem(_initTasks, Nodes[0], false, notRealTime, _cancellationTokenSource.Token);
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

                    var connectionItem = Nodes[1].GetForwardNodes(Connections);
                    if (!connectionItem.Any() || onExit)
                    {
                        //当没有tick时直接结束
                        if (notRealTime) _cancellationTokenSource.Cancel();

                        IsRunning = false;
                        ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show("情景",
                            $"情景\'{Name}\'运行完成");
                        Logger.Debug($"情景运行完成:{Name}");
                        break;
                    }

                    ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show("情景",
                        $"情景\'{Name}\'进入Tick");
                    Logger.Debug($"情景进入Tick:{Name}");
                    try
                    {
                        _tickUtil = new TickUtil(1000, (uint)(_tickIntervalSecond * 1000 * 1000), 1, TickMethod);
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

    private void TickMethod(object sender, long jumpPeriod, long interval)
    {
        if (_inTick) return;

        var nowPointItem = Nodes[1];
        ParsePointItem(_tickTasks, nowPointItem, false, true, _cancellationTokenSource.Token);

        while (true)
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _inTick = false;
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
            _inTick = false;
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
            Logger.Debug($"情景\'{Name}\'由于出现错误被停止");
        }
        else
        {
            ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!)
                .Show("情景", $"情景\'{Name}\'被用户停止");
            Logger.Debug($"情景\'{Name}\'被用户停止");
        }
    }


    private void ParsePointItem(Dictionary<ScenarioNodeBase, Thread?> threads,
        ScenarioNodeBase nowScenarioMethodNode, bool onlyBackward,
        bool notRealTime,
        CancellationToken cancellationToken)
    {
        Logger.Debug($"解析节点:{nowScenarioMethodNode.Title}");
        var valid = true;
        List<Thread> sourceDataTask = new();
        valid = nowScenarioMethodNode.InputDataIsEnough(Connections);
        if (!valid) goto finnish;
        foreach (var sourceSource in nowScenarioMethodNode.GetBackwardNodes(Connections))
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
                Logger.Debug($"执行节点:{nowScenarioMethodNode.Title}");
                var invoke =
                    nowScenarioMethodNode.Invoke(cancellationToken, Connections, Values, TempValue, InputValue);
                if (!invoke)
                {
                    //如果执行失败
                    valid = false;
                    nowScenarioMethodNode.Status = NodeStatus.Error;
                    Logger.Debug($"执行节点失败:{nowScenarioMethodNode.Title}");
                }
                else
                {
                    Logger.Debug($"执行节点完成:{nowScenarioMethodNode.Title}");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "错误");
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
            Logger.Debug($"解析节点完成:{nowScenarioMethodNode.Title}");
        }
        else
        {
            nowScenarioMethodNode.Status = NodeStatus.Error;
            Logger.Debug($"解析节点失败:{nowScenarioMethodNode.Title}");
        }

        if (!onlyBackward)
            foreach (var nextPointItem in nowScenarioMethodNode.GetForwardNodes(Connections))
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

        return Nodes.Any(e => e.IsUseThePlugin(plugStr)) ||
               InputValue.Any<KeyValuePair<string, CustomScenarioValue>>(e =>
                   pluginManger.IsTypeFromThePlugin(e.Value.SerializeType, plugStr) ||
                   pluginManger.IsTypeFromThePlugin(e.Value.ShowType, plugStr)) ||
               Values.Any<KeyValuePair<string, CustomScenarioValue>>(e =>
                   pluginManger.IsTypeFromThePlugin(e.Value.SerializeType, plugStr) ||
                   pluginManger.IsTypeFromThePlugin(e.Value.ShowType, plugStr));
    }

    public void OnDeserialized() //反序列化时hotkeys的默认值会被添加,需要先清空
    {
        PropertyChanged += CustomScenarioPropertyChangedEventHandler;
        foreach (var pointItem in Nodes)
            if (pointItem is ScenarioMethodNode methodNode)
                methodNode.PropertyChanged += CustomScenarioPropertyChangedEventHandler;
    }
}
