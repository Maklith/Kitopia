#region

using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.SDKs;
using Core.SDKs.CustomScenario;
using Core.SDKs.Services;
using Core.SDKs.Services.Plugin;
using Core.SDKs.Tools;
using Core.SDKs.Tools.Ex;
using log4net;
using PluginCore;

#endregion

namespace Core.ViewModel.TaskEditor;

public partial class TaskEditorViewModel : ObservableRecipient
{
    private static readonly ILog Log = LogManager.GetLogger(nameof(TaskEditorViewModel));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCustomScenarioCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndQuitCustomScenarioCommand))]
    public bool _isModified = false;


    public ScenarioMethodCategoryGroup ScenarioMethodCategoryGroup
    {
        get
        {
            var rootScenarioMethodCategoryGroup = ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup;
            rootScenarioMethodCategoryGroup.PropertyChanged += (sender, args) =>
            {
                OnPropertyChanged(nameof(ScenarioMethodCategoryGroup));
            };
            return rootScenarioMethodCategoryGroup;
        }
    }

    [ObservableProperty] private CustomScenario _scenario = new() { IsActive = true };

    private Window _window;

    public TaskEditorViewModel()
    {
        PendingConnection = new PendingConnectionViewModel(this);
        var nodify2 = new ScenarioMethodNode
        {
            Title = "任务1",
            ScenarioMethod = new ScenarioMethod(ScenarioMethodType.默认)
        };
        nodify2.Output = new ObservableCollection<ConnectorItem>
        {
            new()
            {
                IsOut = true,
                Source = nodify2,
                InputObject = new CustomScenarioValue()
                {
                    Type = typeof(NodeConnectorClass)
                },
                
                Title = "开始"
            }
        };
        Scenario.nodes.Add(nodify2);
        var nodify3 = new ScenarioMethodNode
        {
            Title = "Tick",
            ScenarioMethod = new ScenarioMethod(ScenarioMethodType.默认),
            Location = new Point(0, 100)
        };
        nodify3.Output = new ObservableCollection<ConnectorItem>
        {
            new()
            {
                IsOut = true,
                Source = nodify3,
                InputObject = new CustomScenarioValue()
                {
                    Type = typeof(NodeConnectorClass)
                },
                Title = "开始"
            }
        };
        Scenario.nodes.Add(nodify3);

        WeakReferenceMessenger.Default.Register<string, string>(this, "hotkey", (HotKey, o) =>
        {
            if (o == Scenario.runHotKey.SignName)
                Dispatcher.UIThread.InvokeAsync(() => { IsModified = true; });
            else if (o == Scenario.stopHotKey.SignName) Dispatcher.UIThread.InvokeAsync(() => { IsModified = true; });
        });
        WeakReferenceMessenger.Default.Register<CustomScenarioChangeMsg>(this,
            (a, e) =>
            {
                Dispatcher.UIThread.Post(() => { IsModified = true; });

                //Console.WriteLine(1);
                if (e.Type == 1)
                {
                    if (e.Name == "Name") e.CustomScenario.nodes[0].Title = e.CustomScenario.Name;

                    return;
                }

                if (!Scenario.nodes.Contains(e.ScenarioMethodNode)) return;

                if (e.ConnectorItem is not { InputObject: not null }) return;

                if (e.ScenarioMethodNode.ScenarioMethod.Type == ScenarioMethodType.一对多 &&
                    e.ConnectorItem.Title == "输出数量")
                {
                    int? value = null;
                    if (e.ConnectorItem.InputObject.Value is int inputObject)
                        value = inputObject;
                    else
                        value = Convert.ToInt32((double)e.ConnectorItem.InputObject.Value);

                    if (value > 10)
                    {
                        value = 10;
                        e.ConnectorItem.InputObject.Value = (double)10;
                    }

                    if (e.ScenarioMethodNode.Output.Count == value) return;

                    while (e.ScenarioMethodNode.Output.Count != value)
                        if (e.ScenarioMethodNode.Output.Count > value)
                        {
                            var connectorItem = e.ScenarioMethodNode.Output[^1];
                            var connectionItems = Scenario.connections
                                .Where(connectionItem =>
                                    connectionItem.Source == connectorItem)
                                .ToList();
                            foreach (var connectionItem in connectionItems)
                            {
                                Scenario.connections.Remove(connectionItem);
                                if (Scenario.connections.All(item => item.Source != connectionItem.Source))
                                    connectionItem.Source.IsConnected = false;

                                if (Scenario.connections.All(item => item.Target != connectionItem.Target))
                                    connectionItem.Target.IsConnected = false;
                            }

                            e.ScenarioMethodNode.Output.Remove(connectorItem);
                        }
                        else
                        {
                            e.ScenarioMethodNode.Output.Add(new ConnectorItem
                            {
                                Source = e.ScenarioMethodNode,
                                InputObject = new CustomScenarioValue()
                                {
                                    Type = typeof(NodeConnectorClass)
                                },
                                Title = "流输出",
                                IsOut = true,
                            });
                        }
                }

                if (e.ScenarioMethodNode.ScenarioMethod.Type == ScenarioMethodType.打开运行本地项目)
                {
                    var o = e.ScenarioMethodNode.Input[1].InputObject.Value;
                    if (o is string inputObject)
                    {
                        if (inputObject.StartsWith("CustomScenario:"))
                        {
                            var replace = inputObject.Replace("CustomScenario:", "");
                            var customScenario = CustomScenarioManger.CustomScenarios.First(e => e.UUID == replace);
                            if (customScenario.IsHaveInputValue)
                            {
                                for (var index = 0; index < customScenario.InputValue.Count; index++)
                                {
                                    var (key, value) = customScenario.InputValue[index];
                                    if (e.ScenarioMethodNode.Input.Count() < index + 2)
                                        e.ScenarioMethodNode.Input.Add(new ConnectorItem
                                        {
                                            IsOut = false,
                                            Source = e.ScenarioMethodNode,
                                            InputObject = new CustomScenarioValue()
                                            {
                                                Type = value.GetType()
                                            },
                                            Title = key
                                        });

                                    if (e.ScenarioMethodNode.Input[index + 2].Title != key)
                                    {
                                        e.ScenarioMethodNode.Input[index + 2].Title = key;
                                        e.ScenarioMethodNode.Input[index + 2].InputObject.Value = value.GetType();
                                        
                                    }
                                }

                                for (var i = e.ScenarioMethodNode.Input.Count - 1;
                                     i >= customScenario.InputValue.Count + 2;
                                     i--)
                                    e.ScenarioMethodNode.Input.RemoveAt(i);
                            }
                        }
                        else
                        {
                            for (var i = e.ScenarioMethodNode.Input.Count - 1; i >= 2; i--)
                                e.ScenarioMethodNode.Input.RemoveAt(i);
                        }
                    }
                }
            });

        //nodeMethods.Add("new PointItem(){Title = \"Test\"}");
    }

    public bool IsSaveInLocal => CustomScenarioManger.CustomScenarios.Contains(Scenario);

    public object ContentPresenter { get; set; }

    public PendingConnectionViewModel PendingConnection { get; }

    [RelayCommand]
    private void VerifyNode()
    {
        Scenario.Run();
    }

    [RelayCommand]
    private void StopCustomScenario()
    {
        Scenario.Stop();
    }

    private void ToFirstVerify(bool notRealTime = false)
    {
        Scenario.Run(true);
    }

    [RelayCommand]
    private void SwitchConnector(ContentPresenter c)
    {
        if (c.DataContext is not ConnectorItem connector) return;

        if (!connector.SelfInputAble) return;

        IsModified = true;

        connector.InputObject.IsSelf = !connector.InputObject.IsSelf;
        if (connector.InputObject.IsSelf)
        {
            var connectionItems = Scenario.connections
                .Where(e => e.Source == connector || e.Target == connector)
                .ToList();
            foreach (var connectionItem in connectionItems)
            {
                Scenario.connections.Remove(connectionItem);
                if (Scenario.connections.All(e => e.Source != connectionItem.Source))
                    connectionItem.Source.IsConnected = false;

                if (Scenario.connections.All(e => e.Target != connectionItem.Target))
                    connectionItem.Target.IsConnected = false;
            }
        }

        c.DataContext = null;
        c.DataContext = connector;
    }

    [RelayCommand]
    private void AddNodes(ScenarioMethodNode scenarioMethodNode)
    {
        IsModified = true;
        var methodNode = scenarioMethodNode.Copy(Scenario.PluginUsedCount);

        Scenario.nodes.Add(methodNode);
    }

    [RelayCommand]
    private void CopyNode(ScenarioMethodNode scenarioMethodNode)
    {
        IsModified = true;
        if (Scenario.nodes.IndexOf(scenarioMethodNode) is 0 or 1) return;

        var methodNode = scenarioMethodNode.Copy(Scenario.PluginUsedCount);
        methodNode.Location = new Point(scenarioMethodNode.Location.X + 50, scenarioMethodNode.Location.Y + 50);
        Scenario.nodes.Add(methodNode);
    }

    [RelayCommand]
    private void DelNode(ScenarioMethodNode scenarioMethodNode)
    {
        IsModified = true;
        var indexOf = Scenario.nodes.IndexOf(scenarioMethodNode);
        if (indexOf is 0 or 1) return;

        var connectionItems = Scenario.connections
            .Where(e => e.Source.Source == scenarioMethodNode || e.Target.Source == scenarioMethodNode)
            .ToList();
        foreach (var connectionItem in connectionItems)
        {
            Scenario.connections.Remove(connectionItem);
            if (Scenario.connections.All(e => e.Source != connectionItem.Source))
                connectionItem.Source.IsConnected = false;

            if (Scenario.connections.All(e => e.Target != connectionItem.Target))
                connectionItem.Target.IsConnected = false;
        }

        if (scenarioMethodNode.ScenarioMethod.IsFromPlugin)
            Scenario.PluginUsedCount.DelOrDecrease(scenarioMethodNode.ScenarioMethod.PluginInfo!.ToPlgString());

        foreach (var connectorItem in scenarioMethodNode.Input)
        {
            var plugin = PluginManager.EnablePlugin
                .FirstOrDefault((e) => e.Value._dll == connectorItem.InputObject.Type.Assembly)
                .Value;
            if (plugin is not null)
                Scenario.PluginUsedCount.DelOrDecrease(scenarioMethodNode.ScenarioMethod.PluginInfo!.ToPlgString());

            var plugin2 = PluginManager.EnablePlugin
                .FirstOrDefault((e) => e.Value._dll == connectorItem.InputObject.RealType.Assembly)
                .Value;
            if (plugin2 is not null)
                Scenario.PluginUsedCount.DelOrDecrease(scenarioMethodNode.ScenarioMethod.PluginInfo!.ToPlgString());
        }

        foreach (var connectorItem in scenarioMethodNode.Output)
        {
            var plugin = PluginManager.EnablePlugin
                .FirstOrDefault((e) => e.Value._dll == connectorItem.InputObject.Type.Assembly)
                .Value;
            if (plugin is not null)
                Scenario.PluginUsedCount.DelOrDecrease(scenarioMethodNode.ScenarioMethod.PluginInfo!.ToPlgString());

            var plugin2 = PluginManager.EnablePlugin
                .FirstOrDefault((e) => e.Value._dll == connectorItem.InputObject.RealType.Assembly)
                .Value;
            if (plugin2 is not null)
                Scenario.PluginUsedCount.DelOrDecrease(scenarioMethodNode.ScenarioMethod.PluginInfo!.ToPlgString());
        }

        Scenario.nodes.Remove(scenarioMethodNode);
    }

    [RelayCommand]
    private void DelConnection(ConnectionItem connection)
    {
        IsModified = true;
        Scenario.connections.Remove(connection);
        if (Scenario.connections.All(e => e.Source != connection.Source)) connection.Source.IsConnected = false;

        if (Scenario.connections.All(e => e.Target != connection.Target)) connection.Target.IsConnected = false;

        IsModified = true;
        ToFirstVerify();
    }


    [RelayCommand(CanExecute = nameof(IsModified))]
    private void SaveCustomScenario()
    {
        CleanUnusedNode();

        IsModified = false;
        CustomScenarioManger.Save(Scenario);
        OnPropertyChanged(nameof(IsSaveInLocal));
    }

    [RelayCommand(CanExecute = nameof(IsModified))]
    private void SaveAndQuitCustomScenario(Window window)
    {
        CleanUnusedNode();
        var dialog = new DialogContent()
        {
            Content = "是否确定保存并退出",
            Title = "保存并退出?",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            PrimaryAction = () =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CustomScenarioManger.Save(Scenario);
                    OnPropertyChanged(nameof(IsSaveInLocal));
                    IsModified = false;
                    window.Close();
                });
            }
        };
        ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(ContentPresenter,
            dialog);
    }

    [RelayCommand]
    private void CleanUnusedNode()
    {
        for (var i = Scenario.nodes.Count - 1; i >= 2; i--)
        {
            var toRemove = true;

            foreach (var connectorItem in Scenario.nodes[i].Input)
                if (connectorItem.IsConnected)
                {
                    toRemove = false;
                    break;
                }

            if (!toRemove) continue;

            foreach (var connectorItem in Scenario.nodes[i].Output)
                if (connectorItem.IsConnected)
                {
                    toRemove = false;
                    break;
                }

            if (toRemove)
            {
                IsModified = true;
                DelNode(Scenario.nodes[i]);
            }
        }
    }

    [RelayCommand]
    private void DisconnectConnector(ConnectorItem connector)
    {
        var connections = Scenario.connections.Where(e => e.Source == connector || e.Target == connector)
            .ToList();
        for (var i = connections.Count - 1; i >= 0; i--)
        {
            var connection = connections[i];
            Scenario.connections.Remove(connection);
            if (Scenario.connections.All(e => e.Source != connection.Source)) connection.Source.IsConnected = false;

            if (Scenario.connections.All(e => e.Target != connection.Target)) connection.Target.IsConnected = false;

            IsModified = true;
        }

        ToFirstVerify();
    }


    public void Load(CustomScenario customScenario)
    {
        Scenario = customScenario;
    }

    [RelayCommand]
    private void Load(object window)
    {
        _window = (Window)window;
    }

    [RelayCommand]
    public void ReloadScenario(CancelEventArgs e)
    {
        if (IsModified)
        {
            e.Cancel = true;
            Scenario.Stop();
            var dialog = new DialogContent()
            {
                Content = "是否确定不保存退出",
                Title = "不保存退出?",
                PrimaryButtonText = "保存并退出",
                SecondaryButtonText = "不保存",
                CloseButtonText = "取消",
                PrimaryAction = () =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        CustomScenarioManger.Save(Scenario);
                        IsModified = false;


                        _window.Close();
                    });
                },
                SecondaryAction = () =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        CustomScenarioManger.Reload(Scenario);
                        IsModified = false;


                        _window.Close();
                    });
                },
                CloseAction = () => { e.Cancel = true; }
            };
            ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!)
                .ShowDialogAsync(null, dialog);
        }
    }

    public void Connect(ConnectorItem source, ConnectorItem target)
    {
        if (source.IsConnected)
            if (Scenario.connections
                .Any(e => e.Source == source && e.Target == target))
                return;

        if (source.InputObject.Type.FullName == "PluginCore.NodeConnectorClass")
            if (source.IsConnected)
            {
                var connectionsToRemove = Scenario.connections
                    .Where(e => e.Source == source)
                    .ToList();

                foreach (var connection in connectionsToRemove)
                {
                    connection.Source.IsConnected = false;


                    Scenario.connections.Remove(connection);
                    if (Scenario.connections.All(e => e.Target != connection.Target))
                        connection.Target.IsConnected = false;
                }
            }

        IsModified = true;
        Scenario.connections.Add(new ConnectionItem(source, target));
        ToFirstVerify();
        //OnPropertyChanged(nameof(Connections));
    }

    [RelayCommand]
    public async Task ChangeIcon(Control control)
    {
        var openFilePickerAsync = await TopLevel.GetTopLevel(control).StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            FileTypeFilter = new[] { new FilePickerFileType("图像") { Patterns = new[] { "*.png","*.jpg","*.ico" } } },
        });

        if (openFilePickerAsync.Count > 0)
        {
            var path = $"{AppDomain.CurrentDomain.BaseDirectory}customScenarios{Path.DirectorySeparatorChar}{Scenario.UUID}.png";
            var fileStream = File.OpenWrite(path);
            var openReadAsync = await openFilePickerAsync[0].OpenReadAsync();
            await openReadAsync.CopyToAsync(fileStream);
            await fileStream.FlushAsync();
            fileStream.Close();
            openReadAsync.Close();
            Scenario.Icon = null;
        }
    }

    #region 自定义关键词

    [RelayCommand()]
    private void DelKey(string key)
    {
        Scenario.Keys.Remove(key);
        IsModified = true;
    }

    [RelayCommand()]
    private void AddKey(string key)
    {
        if (Scenario.Keys.Contains(key)) return;

        Scenario.Keys.Add(key);

        IsModified = true;
    }

    #endregion

    #region 变量

    [NotifyPropertyChangedFor(nameof(valueCanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddValueCommand))]
    [ObservableProperty]
    private string? _valueValue = string.Empty;

    [NotifyPropertyChangedFor(nameof(inputValueCanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddValueCommand))]
    [ObservableProperty]
    private CustomScenarioValueTuple _valueType;

    private bool valueCanAdd => !string.IsNullOrEmpty(_valueValue) && _valueType != null;

    [RelayCommand(CanExecute = nameof(valueCanAdd))]
    private void AddValue()
    {
        if (Scenario.Values.ContainsKey(ValueValue)) return;


        Scenario.Values.Add(ValueValue, new CustomScenarioValue(ValueType.Type, null));
        OnPropertyChanged(CommunityToolkit.Mvvm.ComponentModel.__Internals.__KnownINotifyPropertyChangedArgs
            .InputValue);
        ValueValue = null;
        IsModified = true;
    }

    [RelayCommand]
    private void DelValue(string key)
    {
        if (Scenario.Values.ContainsKey(key)) Scenario.Values.Remove(key);

        IsModified = true;
    }

    #endregion

    #region 入参

    [NotifyPropertyChangedFor(nameof(inputValueCanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddInputValueCommand))]
    [ObservableProperty]
    private string? _inputValueValue = string.Empty;

    [NotifyPropertyChangedFor(nameof(inputValueCanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddInputValueCommand))]
    [ObservableProperty]
    private CustomScenarioValueTuple _inputValueType;

    private bool inputValueCanAdd => !string.IsNullOrEmpty(_inputValueValue) && _inputValueType != null;

    [RelayCommand(CanExecute = nameof(inputValueCanAdd))]
    private void AddInputValue()
    {
        if (Scenario.InputValue.ContainsKey(InputValueValue)) return;


        Scenario.InputValue.Add(InputValueValue, new CustomScenarioValue(InputValueType.Type, null));
        OnPropertyChanged(CommunityToolkit.Mvvm.ComponentModel.__Internals.__KnownINotifyPropertyChangedArgs
            .InputValue);
        InputValueValue = null;
        IsModified = true;
    }

    [RelayCommand]
    private void DelInputValue(string key)
    {
        if (Scenario.InputValue.ContainsKey(key)) Scenario.InputValue.Remove(key);

        IsModified = true;
    }

    #endregion
}

public class CustomScenarioValueTuple
{
    public Type Type { get; set; }
    public object Value { get; set; }
}