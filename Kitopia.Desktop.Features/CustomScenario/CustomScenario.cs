#region

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.HotKey;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;
using Serilog;

#endregion

namespace Kitopia.Desktop.Features.CustomScenario;

public partial class CustomScenario : ObservableRecipient, IDisposable {
    private static readonly ILogger Logger = LogManager.Logger.ForContext<CustomScenario>();

    [JsonIgnore] [ObservableProperty] private ObservableCollection<string> _autoTriggers = new();
    private CancellationTokenSource _cancellationTokenSource = new();

    [JsonIgnore] [ObservableProperty] private string _description = "";

    [property: JsonIgnore] [JsonIgnore] [ObservableProperty]
    private Bitmap? _icon;

    private readonly object _runGate = new();
    private Task? _runTask;

    [JsonIgnore] [ObservableProperty] private bool _isRunning;

    [JsonIgnore] [ObservableProperty] private DateTime _lastRun;

    [JsonIgnore] [ObservableProperty] private string _name = "情景";


    /// <summary>
    ///     手动执行
    /// </summary>
    [JsonIgnore] [ObservableProperty] private bool _executionManual = true;

    [JsonIgnore] [ObservableProperty] private bool _hasInit = true;
    [JsonIgnore] [ObservableProperty] private string? _initError;
    [JsonIgnore] [ObservableProperty] private ObservableDictionary<string, CustomScenarioValue> _inputValue = new();

    [JsonIgnore] [ObservableProperty] private bool _isHaveInputValue;

    [JsonIgnore] [ObservableProperty] private ObservableCollection<string> _keys = new();

    //ActiveHotKey
    [JsonIgnore] [ObservableProperty] private HotKeyModel? _runHotKey;

    [JsonIgnore] [ObservableProperty] private HotKeyModel? _stopHotKey;
    [JsonIgnore] [ObservableProperty] private ObservableDictionary<string, CustomScenarioValue> _tempValue = new();


    [JsonIgnore] [ObservableProperty] private double? _tickIntervalSecond = 5;

    [JsonIgnore] [ObservableProperty] private ObservableDictionary<string, CustomScenarioValue> _values = new();

    public CustomScenario() {
        PropertyChanged += CustomScenarioPropertyChangedEventHandler;
        Nodes.CollectionChanged += (e, s) => {
            if (s.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) {
                if (e is IEnumerable<ScenarioNodeBase> methodNodes) {
                    foreach (var scenarioMethodNode in methodNodes) {
                        scenarioMethodNode.PropertyChanged += CustomScenarioPropertyChangedEventHandler;
                        if (scenarioMethodNode is ScenarioMethodNode methodNode) {
                            foreach (var connectorItem in methodNode.Input) {
                                connectorItem.PropertyChanged += CustomScenarioPropertyChangedEventHandler;
                                connectorItem.InputObjectHandler = ((_, _) => {
                                    WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg {
                                        Type = 0, Name = nameof(e), ConnectorItem = connectorItem,
                                        ScenarioMethodNode = connectorItem.Source as ScenarioMethodNode,
                                        CustomScenario = this
                                    });
                                });
                                connectorItem.InputObject.PropertyChanged += connectorItem.InputObjectHandler;
                            }
                        }
                    }
                }
            }
            else if (s.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove) {
                if (e is IEnumerable<ScenarioNodeBase> methodNodes) {
                    foreach (var scenarioMethodNode in methodNodes) {
                        scenarioMethodNode.PropertyChanged -= CustomScenarioPropertyChangedEventHandler;
                        if (scenarioMethodNode is ScenarioMethodNode methodNode) {
                            foreach (var connectorItem in methodNode.Input) {
                                connectorItem.PropertyChanged -= CustomScenarioPropertyChangedEventHandler;
                                if (connectorItem.InputObjectHandler != null) {
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
        

        WeakReferenceMessenger.Default.Register<HotKeyChanged>(this, static (recipient, message) => {
            var scenario = (CustomScenario)recipient;
            if (message.Kind != HotKeyChangeKind.Updated) return;
            var matchesRun = scenario.RunHotKey?.UUID == message.Uuid;
            var matchesStop = scenario.StopHotKey?.UUID == message.Uuid;
            if (!matchesRun && !matchesStop) return;
            var model = ServiceManager.Services.GetRequiredService<IHotKetImpl>().GetByUuid(message.Uuid);
            if (matchesRun) scenario.RunHotKey = model;
            if (matchesStop) scenario.StopHotKey = model;
            CustomScenarioManger.Save(scenario);
        });
    }

    public string Uuid { get; init; } = Guid.NewGuid()
        .ToString();


    public ObservableCollection<ScenarioNodeBase> Nodes { get; set; } = new();

    public ObservableCollection<ConnectionItem> Connections { get; set; } = new();
    public event EventHandler Saved;

    internal void NotifySaved() {
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void InitHotKeyUiCommand(HotKeyModel? hotKeyModel) {
        if (hotKeyModel == null) return;
        if (hotKeyModel.UUID == RunHotKey.UUID)
            ServiceManager.Services.GetService<IHotKetImpl>()!.Register(hotKeyModel, _ => Run(), false);

        if (hotKeyModel.UUID == StopHotKey.UUID)
            ServiceManager.Services.GetService<IHotKetImpl>()!.Register(hotKeyModel, _ => Stop(), false);
    }

    public void InitHotKey() {
        RunHotKey ??= new HotKeyModel {
            MainName = "Kitopia情景", Name = $"{Uuid}_开始快捷键", IsSelectCtrl = false, IsSelectAlt = false,
            IsSelectWin = false,
            IsSelectShift = false, SelectKey = EKey.未设置
        };
        StopHotKey ??= new HotKeyModel {
            MainName = "Kitopia情景", Name = $"{Uuid}_停止快捷键", IsSelectCtrl = false, IsSelectAlt = false,
            IsSelectWin = false,
            IsSelectShift = false, SelectKey = EKey.未设置
        };
        if (RunHotKey.IsEnabled)
            if (!ServiceManager.Services.GetService<IHotKetImpl>()!.Register(RunHotKey, _ => Run())) {
                
                ServiceManager.Services.GetService<IToastService>()!.Show(new DialogContent {
                    Title = $"快捷键{RunHotKey.Name}设置失败",
                    Content = "请重新设置快捷键，按键与系统其他程序冲突",
                    CloseButtonText = "关闭",
                    PrimaryButtonText = "重新设置",
                    PrimaryAction = (() => {
                        ServiceManager.Services.GetService<IHotKetImpl>()!.RequestUserModify(RunHotKey.UUID);
                    })
                }.ToToastRequest());
            }

        if (StopHotKey.IsEnabled)
            if (!ServiceManager.Services.GetService<IHotKetImpl>()!.Register(StopHotKey, _ => Stop())) {
                
                ServiceManager.Services.GetService<IToastService>()!.Show(new DialogContent {
                    Title = $"快捷键{StopHotKey.Name}设置失败",
                    Content = "请重新设置快捷键，按键与系统其他程序冲突",
                    CloseButtonText = "关闭",
                    PrimaryButtonText = "重新设置",
                    PrimaryAction = (() => {
                        ServiceManager.Services.GetService<IHotKetImpl>()!.RequestUserModify(StopHotKey.UUID);
                    })
                }.ToToastRequest());
            }
    }

    public void UnRegisterHotKey() {
        if (RunHotKey != null) {
            ServiceManager.Services.GetService<IHotKetImpl>()!.Remove(RunHotKey.UUID);
           
        }
        if (StopHotKey != null) {
            ServiceManager.Services.GetService<IHotKetImpl>()!.Remove(StopHotKey.UUID);
        }
    }


    private void CustomScenarioPropertyChangedEventHandler(object? s, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(IsRunning)) return;
        if (s is CustomScenario) {
            WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg
                { Type = 1, Name = nameof(e), CustomScenario = this });
        }
        else if (s is ScenarioMethodNode methodNode) {
            WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg
                { Type = 0, Name = nameof(e), ScenarioMethodNode = methodNode, CustomScenario = this });
        }
        else if (s is ConnectorItem connectorItem) {
            WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg {
                Type = 0, Name = nameof(e), ConnectorItem = connectorItem,
                ScenarioMethodNode = connectorItem.Source as ScenarioMethodNode, CustomScenario = this
            });
        }
    }

    public void Dispose() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        lock (_runGate) {
            if (IsRunning) _cancellationTokenSource.Cancel();
            else if (_runTask is null or { IsCompleted: true }) _cancellationTokenSource.Dispose();
        }
    }

    partial void OnTickIntervalSecondChanged(double? oldValue) {
        if (oldValue is null) TickIntervalSecond = 0.1;
    }


    public void Run(bool realTime = false, bool onExit = false, params object[] inputValues) {
        lock (_runGate) {
            if (_runTask is { IsCompleted: false } || IsRunning) return;
            if (!VerifyGraph()) return;
            if (realTime) return;
            if (!TryCreateRunInputs(inputValues, out var inputs)) {
                Logger.Warning("情景 {Name} 的输入数量或类型不匹配", Name);
                return;
            }

            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellation = _cancellationTokenSource;
            foreach (var node in Nodes) node.ResetData();
            LastRun = DateTime.Now;
            CustomScenarioManger.Save(this);
            IsRunning = true;
            _runTask = Task.Run(() => RunCoreAsync(inputs, onExit, cancellation));
        }
    }

    internal bool VerifyGraph() {
        if (Nodes.Count < 2 || ScenarioGraph.HasCycle(Connections)) {
            HasInit = false;
            InitError = Nodes.Count < 2 ? "情景缺少开始或 Tick 节点" : "情景连接存在循环";
            foreach (var node in Nodes) node.Status = NodeStatus.Error;
            return false;
        }

        HasInit = true;
        InitError = null;
        foreach (var node in Nodes)
            node.Status = node.IsUsed(Connections)
                ? node.InputDataIsEnough(Connections) ? NodeStatus.PreliminaryVerified : NodeStatus.Error
                : NodeStatus.Unverified;
        return true;
    }

    internal bool TryCreateRunInputs(object?[] values,
        out ObservableDictionary<string, CustomScenarioValue> inputs) {
        inputs = new ObservableDictionary<string, CustomScenarioValue>();
        if (!IsHaveInputValue) return values.Length == 0;
        if (values.Length != InputValue.Count) return false;

        var index = 0;
        foreach (var (name, definition) in InputValue) {
            var value = values[index++];
            if (value is CustomScenarioValue oldValue && definition.SerializeType != typeof(CustomScenarioValue))
                value = oldValue.Value;

            var type = definition.SerializeType;
            if ((value is null && type.IsValueType && Nullable.GetUnderlyingType(type) is null) ||
                (value is not null && !type.IsInstanceOfType(value)))
                return false;

            inputs.Add(name, new CustomScenarioValue(type, value!));
        }

        return true;
    }

    public void Stop(bool inTickError = false) {
        lock (_runGate) {
            if (!IsRunning) return;
            _cancellationTokenSource.Cancel();
        }

        if (inTickError) {
            ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!)
                .Show("情景", $"情景'{Name}'由于出现错误被停止");
            Logger.Debug("情景 {Name} 由于错误被停止", Name);
        }
        else {
            ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!)
                .Show("情景", $"情景'{Name}'被用户停止");
            Logger.Debug("情景 {Name} 被用户停止", Name);
        }
    }

    private async Task RunCoreAsync(ObservableDictionary<string, CustomScenarioValue> inputs,
        bool onExit, CancellationTokenSource cancellation) {
        var token = cancellation.Token;
        try {
            var initialized = ExecutePhase(Nodes[0], inputs, token);
            if (!onExit && ScenarioGraph.GetFlowSuccessors(Nodes[1], Connections).Any()) {
                ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show("情景",
                    $"情景'{Name}'进入Tick");
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(0.1, TickIntervalSecond ?? 0.1)));
                do {
                    token.ThrowIfCancellationRequested();
                    ExecutePhase(Nodes[1], inputs, token, initialized);
                } while (await timer.WaitForNextTickAsync(token));
            }

            if (!token.IsCancellationRequested) {
                ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show("情景",
                    $"情景'{Name}'运行完成");
                Logger.Debug("情景运行完成:{Name}", Name);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) {
            Logger.Error(e, "情景运行失败:{Name}", Name);
            ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show("情景",
                $"情景{Name}出现错误\n{e.InnerException?.Message ?? e.Message}");
        }
        finally {
            lock (_runGate) IsRunning = false;
            cancellation.Dispose();
        }
    }

    internal HashSet<ScenarioNodeBase> ExecutePhase(ScenarioNodeBase start,
        ObservableDictionary<string, CustomScenarioValue> inputs, CancellationToken token,
        IReadOnlySet<ScenarioNodeBase>? available = null) {
        if (ScenarioGraph.HasCycle(Connections))
            throw new InvalidOperationException("情景连接存在循环");

        var executed = new HashSet<ScenarioNodeBase>();
        var pending = new Queue<ScenarioNodeBase>();
        pending.Enqueue(start);
        var deferred = 0;
        while (pending.Count > 0) {
            token.ThrowIfCancellationRequested();
            var node = pending.Dequeue();
            if (executed.Contains(node)) continue;

            var result = TryInvokeNode(node, inputs, executed, available, token);
            if (result is null) {
                pending.Enqueue(node);
                if (++deferred >= pending.Count) {
                    node.Status = NodeStatus.Error;
                    throw new InvalidOperationException($"节点 '{node.Title}' 的输入依赖未执行");
                }
                continue;
            }

            deferred = 0;
            if (result == false)
                throw new InvalidOperationException($"节点 '{node.Title}' 缺少输入或执行失败");

            foreach (var next in ScenarioGraph.GetFlowSuccessors(node, Connections))
                if (!executed.Contains(next)) pending.Enqueue(next);
        }

        return executed;
    }

    private bool? TryInvokeNode(ScenarioNodeBase node,
        ObservableDictionary<string, CustomScenarioValue> inputs,
        HashSet<ScenarioNodeBase> executed, IReadOnlySet<ScenarioNodeBase>? available,
        CancellationToken token) {
        if (executed.Contains(node)) return true;
        if (!node.InputDataIsEnough(Connections)) {
            node.Status = NodeStatus.Error;
            return false;
        }

        foreach (var source in ScenarioGraph.GetDataDependencies(node, Connections)) {
            if (executed.Contains(source) || available?.Contains(source) == true) continue;
            if (ScenarioGraph.HasFlowInput(source, Connections)) return null;
            var result = TryInvokeNode(source, inputs, executed, available, token);
            if (result != true) return result;
        }

        token.ThrowIfCancellationRequested();
        try {
            if (!node.Invoke(token, Connections, Values, TempValue, inputs)) {
                node.Status = NodeStatus.Error;
                return false;
            }
        }
        catch {
            node.Status = NodeStatus.Error;
            throw;
        }

        node.Status = NodeStatus.Verified;
        executed.Add(node);
        return true;
    }

    public bool IsUseThePlugin(string plugStr) {
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
