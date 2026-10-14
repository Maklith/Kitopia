using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.CustomScenario.ViewModels.TaskEditor;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaTest.CustomScenario;

[TestClass]
[DoNotParallelize]
public sealed class ScenarioExecutionTests
{
    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ExecutePhase_Condition_RunsOnlySelectedBranch(bool condition)
    {
        var scenario = new Kitopia.Desktop.Features.CustomScenario.CustomScenario();
        var start = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        var branch = new ScenarioMethod(ScenarioMethodType.Condition).GenerateNode();
        var trueNode = new CountingNode();
        var falseNode = new CountingNode();
        branch.Input[1].InputObject.IsSelf = true;
        branch.Input[1].InputObject.Value = condition;

        scenario.Connections.Add(Connect(start.Output[0], branch.Input[0]));
        scenario.Connections.Add(Connect(branch.Output[0], trueNode.Input));
        scenario.Connections.Add(Connect(branch.Output[1], falseNode.Input));

        scenario.ExecutePhase(start, new ObservableDictionary<string, CustomScenarioValue>(),
            CancellationToken.None);

        Assert.AreEqual(condition ? 1 : 0, trueNode.Count);
        Assert.AreEqual(condition ? 0 : 1, falseNode.Count);
    }

    [TestMethod]
    public void ExecutePhase_InactiveBranchDataDependency_DoesNotRunItsNode()
    {
        var scenario = new Kitopia.Desktop.Features.CustomScenario.CustomScenario();
        var start = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        var branch = new ScenarioMethod(ScenarioMethodType.Condition).GenerateNode();
        var selected = new ScenarioMethod(ScenarioMethodType.VariableSet)
            { ValueName = "result", ValueDataType = typeof(object) }.GenerateNode();
        var inactive = new ScenarioMethod(ScenarioMethodType.VariableGet)
            { ValueName = "source", ValueDataType = typeof(object) }.GenerateNode();
        branch.Input[1].InputObject.IsSelf = true;
        branch.Input[1].InputObject.Value = true;
        scenario.Connections.Add(Connect(start.Output[0], branch.Input[0]));
        scenario.Connections.Add(Connect(branch.Output[0], selected.Input[0]));
        scenario.Connections.Add(Connect(branch.Output[1], inactive.Input[0]));
        scenario.Connections.Add(Connect(inactive.Output[1], selected.Input[1]));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            scenario.ExecutePhase(start, new ObservableDictionary<string, CustomScenarioValue>(),
                CancellationToken.None));
        Assert.AreNotEqual(NodeStatus.Verified, inactive.Status);
        Assert.AreEqual(NodeStatus.Error, selected.Status);
    }

    [TestMethod]
    public void ExecutePhase_DataKnot_PropagatesProducerValue()
    {
        var scenario = new Kitopia.Desktop.Features.CustomScenario.CustomScenario();
        var start = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        var producer = new ScenarioMethod(ScenarioMethodType.VariableGet)
            { ValueName = "source", ValueDataType = typeof(int) }.GenerateNode();
        var consumer = new ScenarioMethod(ScenarioMethodType.VariableSet)
            { ValueName = "result", ValueDataType = typeof(int) }.GenerateNode();
        var knot = new KnotNodeViewModel { Title = "data knot" };
        knot.Connector = new ConnectorItem
        {
            Source = knot,
            ConnectorType = ConnectorType.Both,
            InputObject = new CustomScenarioValue(typeof(int), null)
        };
        scenario.Values.Add("source", new CustomScenarioValue(typeof(int), 5));
        scenario.Values.Add("result", new CustomScenarioValue(typeof(int), 0));
        scenario.Connections.Add(Connect(start.Output[0], producer.Input[0]));
        scenario.Connections.Add(Connect(start.Output[1], consumer.Input[0]));
        scenario.Connections.Add(Connect(producer.Output[1], knot.Connector));
        scenario.Connections.Add(Connect(knot.Connector, consumer.Input[1]));

        scenario.ExecutePhase(start, new ObservableDictionary<string, CustomScenarioValue>(),
            CancellationToken.None);

        Assert.AreEqual(5, scenario.Values["result"].Value);
    }

    [TestMethod]
    public void SplitConnection_FlowKnot_ContinuesToTarget()
    {
        var scenario = new Kitopia.Desktop.Features.CustomScenario.CustomScenario();
        var start = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        var tick = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        var target = new CountingNode();
        scenario.Nodes.Add(start);
        scenario.Nodes.Add(tick);
        scenario.Nodes.Add(target);
        var connection = Connect(start.Output[0], target.Input);
        scenario.Connections.Add(connection);
        var editor = new TaskEditorViewModel();
        editor.Load(scenario);

        editor.SplitConnection(connection, new Point(20, 20));

        var knot = (KnotNodeViewModel)scenario.Nodes[^1];
        Assert.AreEqual(typeof(NodeConnectorClass), knot.Connector.InputObject.SerializeType);
        Assert.AreSame(target, ScenarioGraph.GetFlowSuccessors(knot, scenario.Connections).Single());
        scenario.ExecutePhase(start, new ObservableDictionary<string, CustomScenarioValue>(), CancellationToken.None);
        Assert.AreEqual(1, target.Count);
    }

    [TestMethod]
    public void Copy_PluginInputConnector_HasIndependentState()
    {
        using var provider = new ServiceCollection()
            .AddTransient<TestInputConnector>(_ => new TestInputConnector(42)).BuildServiceProvider();
        var method = new ScenarioMethod(ScenarioMethodType.PluginMethod) { ServiceProvider = provider };
        var source = new ScenarioMethodNode { Title = "plugin", ScenarioMethod = method };
        source.Input.Add(new ConnectorItem
        {
            Source = source,
            InputObject = new CustomScenarioValue
            {
                SerializeType = typeof(int), ShowType = typeof(TestInputConnector), IsSelf = true, Value = 5
            },
            IsPluginInputConnector = true,
            PluginInputConnector = provider.GetRequiredService<TestInputConnector>()
        });

        var first = (ScenarioMethodNode)source.Copy();
        var second = (ScenarioMethodNode)source.Copy();
        var firstConnector = first.Input[0].PluginInputConnector!;
        var secondConnector = second.Input[0].PluginInputConnector!;
        firstConnector.Value.SetValue(7);

        Assert.AreNotSame(firstConnector, secondConnector);
        Assert.AreEqual(42, ((TestInputConnector)firstConnector).FactoryToken);
        Assert.AreEqual(42, ((TestInputConnector)secondConnector).FactoryToken);
        Assert.AreEqual(5, secondConnector.Value.Value.Value);

        first.ConnectorInit(first.Input[0]);
        Assert.AreNotSame(firstConnector, first.Input[0].PluginInputConnector);
        Assert.AreEqual(42, ((TestInputConnector)first.Input[0].PluginInputConnector!).FactoryToken);
        Assert.AreEqual(5, first.Input[0].PluginInputConnector!.Value.Value.Value);
    }

    [TestMethod]
    public void GenerateNode_RequiredCustomInput_StartsEmptyAndRejectsInvalidValues()
    {
        using var provider = new ServiceCollection()
            .AddTransient<TestInputConnector>(_ => new TestInputConnector(42)).BuildServiceProvider();
        var method = typeof(ParameterMethods).GetMethod(nameof(ParameterMethods.Required))!;
        var node = new ScenarioMethod(method, new PluginLocalInfo(), new ScenarioMethodAttribute("required"),
            ScenarioMethodType.PluginMethod, provider).GenerateNode();
        var input = node.Input[1].InputObject;
        Assert.IsNull(input.Value);
        var values = new ObservableDictionary<string, CustomScenarioValue>();
        Assert.IsFalse(node.Invoke(CancellationToken.None, [], values, values, values));
        input.Value = DBNull.Value;
        Assert.IsFalse(node.Invoke(CancellationToken.None, [], values, values, values));
        input.Value = 7;
        Assert.IsTrue(node.Invoke(CancellationToken.None, [], values, values, values));
        Assert.AreEqual(7, node.Output[1].InputObject.Value);
    }

    [TestMethod]
    public void GenerateNode_OptionalInput_KeepsDefaultValue()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var method = typeof(ParameterMethods).GetMethod(nameof(ParameterMethods.Optional))!;
        var node = new ScenarioMethod(method, new PluginLocalInfo(), new ScenarioMethodAttribute("optional"),
            ScenarioMethodType.PluginMethod, provider).GenerateNode();
        Assert.AreEqual(9, node.Input[1].InputObject.Value);
        node.Input[1].InputObject.Value = null;
        var values = new ObservableDictionary<string, CustomScenarioValue>();
        Assert.IsTrue(node.Invoke(CancellationToken.None, [], values, values, values));
        Assert.AreEqual(9, node.Output[1].InputObject.Value);
    }

    [TestMethod]
    public async Task TaskEditor_PluginCategoriesChange_NotifiesUntilWindowCloses()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch<bool>(() =>
        {
            var editor = new TaskEditorViewModel();
            var window = new Window();
            var notifications = 0;
            editor.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(editor.ScenarioMethodCategoryGroup)) notifications++;
            };
            window.Show();
            editor.LoadCommand.Execute(window);
            editor.LoadCommand.Execute(window);
            ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup.OnPropertyChanged(
                nameof(ScenarioMethodCategoryGroup));
            Assert.AreEqual(1, notifications);
            window.Close();
            ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup.OnPropertyChanged(
                nameof(ScenarioMethodCategoryGroup));
            Assert.AreEqual(1, notifications);
            return Task.FromResult(true);
        }, CancellationToken.None);
    }

    [TestMethod]
    public void ExecutePhase_Tick_CanReadValueFromInitialization()
    {
        var scenario = new Kitopia.Desktop.Features.CustomScenario.CustomScenario();
        var start = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        var tick = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        var producer = new ScenarioMethod(ScenarioMethodType.VariableGet)
            { ValueName = "source", ValueDataType = typeof(int) }.GenerateNode();
        var consumer = new ScenarioMethod(ScenarioMethodType.VariableSet)
            { ValueName = "result", ValueDataType = typeof(int) }.GenerateNode();
        scenario.Values.Add("source", new CustomScenarioValue(typeof(int), 5));
        scenario.Values.Add("result", new CustomScenarioValue(typeof(int), 0));
        scenario.Connections.Add(Connect(start.Output[0], producer.Input[0]));
        scenario.Connections.Add(Connect(tick.Output[0], consumer.Input[0]));
        scenario.Connections.Add(Connect(producer.Output[1], consumer.Input[1]));

        var inputs = new ObservableDictionary<string, CustomScenarioValue>();
        var initialized = scenario.ExecutePhase(start, inputs, CancellationToken.None);
        scenario.ExecutePhase(tick, inputs, CancellationToken.None, initialized);

        Assert.AreEqual(5, scenario.Values["result"].Value);
    }

    [TestMethod]
    public void RunInputs_MapByDefinitionOrder_WithoutMutatingDefinitions()
    {
        var scenario = new Kitopia.Desktop.Features.CustomScenario.CustomScenario { IsHaveInputValue = true };
        scenario.InputValue.Add("count", new CustomScenarioValue(typeof(int), null));
        scenario.InputValue.Add("label", new CustomScenarioValue(typeof(string), null));

        Assert.IsTrue(scenario.TryCreateRunInputs([3, "item"], out var inputs));
        Assert.AreEqual(3, inputs["count"].Value);
        Assert.AreEqual("item", inputs["label"].Value);
        Assert.IsNull(scenario.InputValue["count"].Value);
        Assert.IsFalse(scenario.TryCreateRunInputs(["wrong", 3], out _));
        Assert.IsFalse(scenario.TryCreateRunInputs([3], out _));
    }

    [TestMethod]
    public void InputVariableGet_OutputsTheValue()
    {
        var node = new ScenarioMethod(ScenarioMethodType.InputVariableGet)
            { ValueName = "name", ValueDataType = typeof(string) }.GenerateNode();
        var inputs = new ObservableDictionary<string, CustomScenarioValue>();
        inputs.Add("name", new CustomScenarioValue(typeof(string), "Kitopia"));

        Assert.IsTrue(node.Invoke(CancellationToken.None, [],
            new ObservableDictionary<string, CustomScenarioValue>(),
            new ObservableDictionary<string, CustomScenarioValue>(), inputs));
        Assert.AreEqual("Kitopia", node.Output[1].InputObject.Value);
    }

    [TestMethod]
    public void OpenRunLocalProject_PassesRawInputValues()
    {
        var priorServices = ServiceManager.Services;
        var search = new RecordingSearchItemTool();
        ServiceManager.Services = new ServiceCollection().AddSingleton<ISearchItemTool>(search)
            .BuildServiceProvider();
        try
        {
            var node = new ScenarioMethod(ScenarioMethodType.OpenRunLocalProject).GenerateNode();
            node.Input[1].InputObject.Value = "CustomScenario:child";
            node.Input.Add(new ConnectorItem
            {
                Source = node,
                InputObject = new CustomScenarioValue(typeof(int), 7)
            });

            Assert.IsTrue(node.Invoke(CancellationToken.None, [],
                new ObservableDictionary<string, CustomScenarioValue>(),
                new ObservableDictionary<string, CustomScenarioValue>(),
                new ObservableDictionary<string, CustomScenarioValue>()));
            Assert.AreEqual("CustomScenario:child", search.OnlyKey);
            CollectionAssert.AreEqual(new object[] { 7 }, search.Inputs);
        }
        finally
        {
            ServiceManager.Services = priorServices;
        }
    }

    [TestMethod]
    public void ScenarioGraph_RejectsIndirectCycles_AndRunVerificationDoesNotExecute()
    {
        var scenario = new Kitopia.Desktop.Features.CustomScenario.CustomScenario();
        var start = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        var tick = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        var first = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        var second = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        scenario.Nodes.Add(start);
        scenario.Nodes.Add(tick);
        scenario.Nodes.Add(first);
        scenario.Nodes.Add(second);
        scenario.Connections.Add(Connect(start.Output[0], first.Input[0]));
        scenario.Connections.Add(Connect(first.Output[0], second.Input[0]));

        Assert.IsTrue(ScenarioGraph.WouldCreateCycle(scenario.Connections, second.Output[0], first.Input[0]));
        scenario.Run(true);
        Assert.IsTrue(scenario.HasInit);
        Assert.IsFalse(scenario.IsRunning);

        scenario.Connections.Add(Connect(second.Output[0], first.Input[0]));
        Assert.IsTrue(ScenarioGraph.HasCycle(scenario.Connections));
        scenario.Run(true);
        Assert.IsFalse(scenario.HasInit);
        Assert.AreEqual(NodeStatus.Error, first.Status);
        Assert.ThrowsExactly<InvalidOperationException>(() => scenario.ExecutePhase(start,
            new ObservableDictionary<string, CustomScenarioValue>(), CancellationToken.None));
    }

    [TestMethod]
    [DataRow(null, null, true)]
    [DataRow(null, "value", false)]
    [DataRow("value", null, false)]
    [DataRow(42, 42, true)]
    [DataRow(42, "42", false)]
    public async Task Equal_NullAndNonNullInputs_UsesObjectEquality(object? left, object? right, bool expected)
    {
        var node = new ScenarioMethod(ScenarioMethodType.Equal).GenerateNode();
        node.Input[1].InputObject.Value = left;
        node.Input[2].InputObject.Value = right;
        var values = new ObservableDictionary<string, CustomScenarioValue>();

        Assert.IsTrue(await node.InvokeAsync(CancellationToken.None, [], values, values, values));
        Assert.AreEqual(expected, node.Output[0].InputObject.Value);
    }

    [TestMethod]
    public async Task IntegerConstant_DefaultValue_OutputsInt32()
    {
        var group = ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup.Childrens["Kitopia"]
            .Childrens.Values.Single(child => child.Methods.Values.Any(node =>
                node.ScenarioMethod.Type == ScenarioMethodType.Default));
        var node = (ScenarioMethodNode)group.Methods.Values.Single(node =>
            node.Output[0].InputObject.SerializeType == typeof(int)).Copy();
        var values = new ObservableDictionary<string, CustomScenarioValue>();

        Assert.IsTrue(await node.InvokeAsync(CancellationToken.None, [], values, values, values));
        Assert.IsInstanceOfType<int>(node.Output[0].InputObject.Value);
        Assert.AreEqual(0, node.Output[0].InputObject.Value);
    }

    [TestMethod]
    public void VariableTypes_StringIsAvailableAndFlowIsExcluded()
    {
        var types = CustomScenarioGlobe.GetAllCouldUseTypeInValue.Select(value => value.Type).ToArray();

        CollectionAssert.Contains(types, typeof(string));
        CollectionAssert.Contains(types, typeof(int));
        CollectionAssert.DoesNotContain(types, typeof(NodeConnectorClass));
    }

    [TestMethod]
    [DataRow("fewer", false)]
    [DataRow("changed", false)]
    [DataRow("missing", false)]
    [DataRow("local", false)]
    [DataRow("fewer", true)]
    [DataRow("changed", true)]
    [DataRow("missing", true)]
    [DataRow("local", true)]
    public async Task OpenRunLocalProject_TargetChanges_RemovesObsoleteConnections(string change, bool plugin)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch(() =>
        {
            using var child = new Kitopia.Desktop.Features.CustomScenario.CustomScenario { IsHaveInputValue = true };
            using var next = new Kitopia.Desktop.Features.CustomScenario.CustomScenario { IsHaveInputValue = true };
            child.InputValue.Add("count", new CustomScenarioValue(typeof(int), null!));
            child.InputValue.Add("label", new CustomScenarioValue(typeof(string), null!));
            next.InputValue.Add("value", new CustomScenarioValue(change == "changed" ? typeof(string) : typeof(int), null!));
            CustomScenarioManger.CustomScenarios.Add(child);
            CustomScenarioManger.CustomScenarios.Add(next);
            var editor = new TaskEditorViewModel();
            var method = typeof(KitopiaEx.CustomScenarioMethods.SearchItemScenarioMethod)
                .GetMethod(nameof(KitopiaEx.CustomScenarioMethods.SearchItemScenarioMethod.OpenSearchViewItem))!;
            using var services = new ServiceCollection().BuildServiceProvider();
            var node = plugin
                ? new ScenarioMethod(method, new PluginLocalInfo(), method.GetCustomAttribute<ScenarioMethodAttribute>()!,
                    ScenarioMethodType.PluginMethod, services).GenerateNode()
                : new ScenarioMethod(ScenarioMethodType.OpenRunLocalProject).GenerateNode();
            try
            {
                editor.Scenario.Nodes.Add(node);
                node.Input[1].InputObject.Value = $"CustomScenario:{child.Uuid}";
                Assert.HasCount(4, node.Input);
                var number = new ScenarioMethod(ScenarioMethodType.VariableGet)
                    { ValueName = "number", ValueDataType = typeof(int) }.GenerateNode();
                var text = new ScenarioMethod(ScenarioMethodType.VariableGet)
                    { ValueName = "text", ValueDataType = typeof(string) }.GenerateNode();
                editor.Connect(number.Output[1], node.Input[2]);
                editor.Connect(text.Output[1], node.Input[3]);

                node.Input[1].InputObject.Value = change switch
                {
                    "missing" => "CustomScenario:missing",
                    "local" => "local-file",
                    _ => $"CustomScenario:{next.Uuid}"
                };

                Assert.HasCount(change is "missing" or "local" ? 2 : 3, node.Input);
                Assert.HasCount(change == "fewer" ? 1 : 0, editor.Scenario.Connections);
                Assert.IsFalse(text.Output[1].IsConnected);
                Assert.AreEqual(change == "fewer", number.Output[1].IsConnected);
                Assert.IsTrue(editor.Scenario.Connections.All(edge => node.Input.Contains(edge.Target)));
                if (change == "changed") Assert.AreEqual(typeof(string), node.Input[2].InputObject.SerializeType);
            }
            finally
            {
                WeakReferenceMessenger.Default.UnregisterAll(editor);
                editor.Scenario.Dispose();
                CustomScenarioManger.CustomScenarios.Remove(child);
                CustomScenarioManger.CustomScenarios.Remove(next);
            }
            return Task.FromResult(true);
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task LocalItem_EditorLoadsPreviouslySelectedPluginTarget_InitializesScenarioInputs()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch(() =>
        {
            using var child = new Kitopia.Desktop.Features.CustomScenario.CustomScenario { IsHaveInputValue = true };
            using var scenario = new Kitopia.Desktop.Features.CustomScenario.CustomScenario();
            using var services = new ServiceCollection().BuildServiceProvider();
            child.InputValue.Add("text", new CustomScenarioValue(typeof(string), null!));
            CustomScenarioManger.CustomScenarios.Add(child);
            var method = typeof(KitopiaEx.CustomScenarioMethods.SearchItemScenarioMethod)
                .GetMethod(nameof(KitopiaEx.CustomScenarioMethods.SearchItemScenarioMethod.OpenSearchViewItem))!;
            var node = new ScenarioMethod(method, new PluginLocalInfo(), method.GetCustomAttribute<ScenarioMethodAttribute>()!,
                ScenarioMethodType.PluginMethod, services).GenerateNode();
            node.Input[1].InputObject.Value = $"CustomScenario:{child.Uuid}";
            scenario.Nodes.Add(node);
            var editor = new TaskEditorViewModel();
            var emptyScenario = editor.Scenario;
            try
            {
                Assert.HasCount(2, node.Input);
                editor.Load(scenario);
                Assert.HasCount(3, node.Input);
                Assert.AreEqual("text", node.Input[2].Title);
                Assert.AreEqual(typeof(string), node.Input[2].InputObject.SerializeType);
                Assert.AreSame(node, node.Input[2].Source);
            }
            finally
            {
                WeakReferenceMessenger.Default.UnregisterAll(editor);
                emptyScenario.Dispose();
                CustomScenarioManger.CustomScenarios.Remove(child);
            }
            return Task.FromResult(true);
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LocalItem_ConnectedTargetReset_PreservesParameterConnectorsAndConnections(bool plugin)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch(() =>
        {
            using var child = new Kitopia.Desktop.Features.CustomScenario.CustomScenario { IsHaveInputValue = true };
            child.InputValue.Add("count", new CustomScenarioValue(typeof(int), null!));
            CustomScenarioManger.CustomScenarios.Add(child);
            using var services = new ServiceCollection().BuildServiceProvider();
            var editor = new TaskEditorViewModel();
            var method = typeof(KitopiaEx.CustomScenarioMethods.SearchItemScenarioMethod)
                .GetMethod(nameof(KitopiaEx.CustomScenarioMethods.SearchItemScenarioMethod.OpenSearchViewItem))!;
            var node = plugin
                ? new ScenarioMethod(method, new PluginLocalInfo(), method.GetCustomAttribute<ScenarioMethodAttribute>()!,
                    ScenarioMethodType.PluginMethod, services).GenerateNode()
                : new ScenarioMethod(ScenarioMethodType.OpenRunLocalProject).GenerateNode();
            try
            {
                editor.Scenario.Nodes.Add(node);
                node.Input[1].InputObject.Value = $"CustomScenario:{child.Uuid}";
                var parameter = node.Input[2];
                var itemSource = new ScenarioMethod(ScenarioMethodType.VariableGet)
                    { ValueName = "item", ValueDataType = typeof(string) }.GenerateNode();
                var numberSource = new ScenarioMethod(ScenarioMethodType.VariableGet)
                    { ValueName = "count", ValueDataType = typeof(int) }.GenerateNode();
                node.Input[1].InputObject.IsSelf = false;
                editor.Connect(itemSource.Output[1], node.Input[1]);
                editor.Connect(numberSource.Output[1], parameter);

                node.ResetData();
                editor.Load(editor.Scenario);

                Assert.IsNull(node.Input[1].InputObject.Value);
                Assert.HasCount(3, node.Input);
                Assert.HasCount(2, editor.Scenario.Connections);
                Assert.AreSame(parameter, node.Input[2]);
                Assert.IsTrue(parameter.IsConnected);
                Assert.IsTrue(numberSource.Output[1].IsConnected);
            }
            finally
            {
                WeakReferenceMessenger.Default.UnregisterAll(editor);
                editor.Scenario.Dispose();
                CustomScenarioManger.CustomScenarios.Remove(child);
            }
            return Task.FromResult(true);
        }, CancellationToken.None);
    }

    private static ConnectionItem Connect(ConnectorItem source, ConnectorItem target) =>
        new() { Source = source, Target = target };

    private sealed class CountingNode : ScenarioNodeBase
    {
        public CountingNode()
        {
            Title = "counter";
            Input = new ConnectorItem
            {
                Source = this,
                InputObject = new CustomScenarioValue(typeof(NodeConnectorClass), null)
            };
        }

        public ConnectorItem Input { get; }
        public int Count { get; private set; }

        public override bool InputDataIsEnough(ObservableCollection<ConnectionItem> connections) => true;

        public override bool Invoke(CancellationToken cancellationToken,
            ObservableCollection<ConnectionItem> connections,
            ObservableDictionary<string, CustomScenarioValue> values,
            ObservableDictionary<string, CustomScenarioValue> tempValues,
            ObservableDictionary<string, CustomScenarioValue> inputValues)
        {
            Count++;
            return true;
        }
    }

    private sealed class TestInputConnector(int factoryToken) : INodeInputConnector
    {
        public int FactoryToken { get; } = factoryToken;
        public StyleInclude Style => null!;
        public IDataTemplate IDataTemplate => null!;
        public ObservableValue Value { get; set; } = new()
        {
            Value = new CustomScenarioValue { SerializeType = typeof(int) }
        };
    }

    private static class ParameterMethods
    {
        public static int Required([SelfInput] [CustomNodeInputType(typeof(TestInputConnector))] int value,
            CancellationToken token) => value;

        public static int Optional([SelfInput] int value = 9, CancellationToken token = default) => value;
    }

    private sealed class RecordingSearchItemTool : ISearchItemTool
    {
        public string? OnlyKey { get; private set; }
        public object[] Inputs { get; private set; } = [];
        public void OpenSearchItemByOnlyKey(string onlyKey, params object[] inputValues)
        {
            OnlyKey = onlyKey;
            Inputs = inputValues;
        }

        public void OpenFile(SearchViewItem? item, params object[] inputValues) { }
        public void IgnoreItem(SearchViewItem? item) { }
        public void OpenFolder(SearchViewItem? item) { }
        public void RunAsAdmin(SearchViewItem? item) { }
        public void Star(SearchViewItem item) { }
        public void Pin(SearchViewItem? item) { }
        public void OpenFolderInTerminal(SearchViewItem? item) { }
    }
}
