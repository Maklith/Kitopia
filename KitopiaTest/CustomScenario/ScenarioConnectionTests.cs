using System.Reflection;
using System.Reflection.Emit;
using Avalonia;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.CustomScenario.ViewModels.TaskEditor;
using PluginCore.CustomScenario;

namespace KitopiaTest.CustomScenario;

[TestClass]
[DoNotParallelize]
public sealed class ScenarioConnectionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PendingConnection_CustomEditor_UsesDataTypeInBothDragDirections(bool fromInput)
    {
        var editor = new TaskEditorViewModel();
        var output = CreateConnector(typeof(string), ConnectorType.Output);
        var input = CreateConnector(typeof(string), ConnectorType.Input, typeof(CustomEditor));
        var origin = fromInput ? input : output;
        var target = fromInput ? output : input;
        editor.PendingConnection.Start(origin);
        editor.PendingConnection.PreviewTarget = target;
        Assert.AreEqual("连接", editor.PendingConnection.PreviewText);

        editor.PendingConnection.Finish(target);

        var connection = editor.Scenario.Connections.Single();
        Assert.AreSame(output, connection.Source);
        Assert.AreSame(input, connection.Target);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PendingConnection_Inheritance_OnlyAllowsOutputAssignableToInput(bool fromInput)
    {
        var editor = new TaskEditorViewModel();
        var output = CreateConnector(typeof(DerivedValue), ConnectorType.Output);
        var input = CreateConnector(typeof(BaseValue), ConnectorType.Input);
        editor.PendingConnection.Start(fromInput ? input : output);
        editor.PendingConnection.Finish(fromInput ? output : input);
        Assert.HasCount(1, editor.Scenario.Connections);

        editor.Scenario.Connections.Clear();
        output.InputObject.SerializeType = typeof(BaseValue);
        input.InputObject.SerializeType = typeof(DerivedValue);
        editor.PendingConnection.PreviewTarget = fromInput ? output : input;
        Assert.AreEqual("类型错误", editor.PendingConnection.PreviewText);
        editor.PendingConnection.Finish(fromInput ? output : input);
        Assert.IsEmpty(editor.Scenario.Connections);
    }

    [TestMethod]
    public void CanConnect_SameFullNameInDifferentAssemblies_RejectsDifferentTypes()
    {
        var first = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ScenarioValueA"),
            AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("main")
            .DefineType("Plugin.Value", TypeAttributes.Public).CreateType()!;
        var second = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ScenarioValueB"),
            AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("main")
            .DefineType("Plugin.Value", TypeAttributes.Public).CreateType()!;
        Assert.AreEqual(first.FullName, second.FullName);
        var output = CreateConnector(first, ConnectorType.Output);
        var input = CreateConnector(second, ConnectorType.Input);

        Assert.IsFalse(ScenarioGraph.CanConnect(output, input));
        Assert.IsFalse(ScenarioGraph.CanConnect(input, output));
    }

    [TestMethod]
    public void CanConnect_DynamicValues_DoesNotMixDataAndFlow()
    {
        var output = CreateConnector(typeof(object), ConnectorType.Output);
        var input = CreateConnector(typeof(int), ConnectorType.Input);
        Assert.IsTrue(ScenarioGraph.CanConnect(output, input));
        input.InputObject.SerializeType = typeof(NodeConnectorClass);
        Assert.IsFalse(ScenarioGraph.CanConnect(output, input));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NodeSearch_CustomEditorAndInheritance_UsesDataTypeAndDirection(bool fromInput)
    {
        var previousRoot = ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup;
        var root = new ScenarioMethodCategoryGroup();
        var output = CreateConnector(typeof(DerivedValue), ConnectorType.Output);
        var input = CreateConnector(typeof(BaseValue), ConnectorType.Input, typeof(CustomEditor));
        var candidate = (ScenarioMethodNode)(fromInput ? output.Source : input.Source);
        root.Methods.Add("candidate", candidate);
        ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup = root;
        try
        {
            var search = new TaskNodeSearchViewModel(fromInput ? input : output, new TaskEditorViewModel(), new Point());
            Assert.AreSame(candidate, search.FilteredNodes.Single().Node);
        }
        finally
        {
            ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup = previousRoot;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Connect_DataInputAlreadyConnected_ReplacesSourceAndPreservesFanOut(bool fromInput)
    {
        var editor = new TaskEditorViewModel();
        var first = CreateConnector(typeof(string), ConnectorType.Output);
        var second = CreateConnector(typeof(string), ConnectorType.Output);
        var input = CreateConnector(typeof(string), ConnectorType.Input);
        var otherInput = CreateConnector(typeof(string), ConnectorType.Input);
        editor.Connect(first, input);
        editor.Connect(first, otherInput);

        editor.Connect(fromInput ? input : second, fromInput ? second : input);

        Assert.HasCount(2, editor.Scenario.Connections);
        Assert.AreSame(second, editor.Scenario.Connections.Single(edge => edge.Target == input).Source);
        Assert.AreSame(first, editor.Scenario.Connections.Single(edge => edge.Target == otherInput).Source);
        Assert.IsTrue(first.IsConnected);
        Assert.IsTrue(second.IsConnected);
        Assert.IsTrue(input.IsConnected);
    }

    [TestMethod]
    public void Connect_FlowInput_AllowsBranchesToMerge()
    {
        var editor = new TaskEditorViewModel();
        var first = CreateConnector(typeof(NodeConnectorClass), ConnectorType.Output);
        var second = CreateConnector(typeof(NodeConnectorClass), ConnectorType.Output);
        var input = CreateConnector(typeof(NodeConnectorClass), ConnectorType.Input);
        editor.Connect(first, input);
        editor.Connect(second, input);

        Assert.HasCount(2, editor.Scenario.Connections);
        Assert.IsTrue(first.IsConnected);
        Assert.IsTrue(second.IsConnected);
    }

    [TestMethod]
    public void Connect_DataKnot_ReplacesIncomingSourceAndPreservesOutgoingConnection()
    {
        var editor = new TaskEditorViewModel();
        var first = CreateConnector(typeof(string), ConnectorType.Output);
        var second = CreateConnector(typeof(string), ConnectorType.Output);
        var knot = new KnotNodeViewModel();
        knot.Connector = new ConnectorItem
        {
            Source = knot, ConnectorType = ConnectorType.Both,
            InputObject = new CustomScenarioValue(typeof(string), null!)
        };
        var input = CreateConnector(typeof(string), ConnectorType.Input);
        editor.Connect(first, knot.Connector);
        editor.Connect(knot.Connector, input);

        editor.Connect(second, knot.Connector);

        Assert.HasCount(2, editor.Scenario.Connections);
        Assert.IsFalse(first.IsConnected);
        Assert.IsTrue(knot.Connector.IsConnected);
        Assert.AreSame(second, editor.Scenario.Connections.Single(edge => edge.Target == knot.Connector).Source);
        Assert.AreSame(knot.Connector, editor.Scenario.Connections.Single(edge => edge.Target == input).Source);
    }

    private static ConnectorItem CreateConnector(Type type, ConnectorType direction, Type? showType = null)
    {
        var node = new ScenarioMethodNode
        {
            Title = "connection fixture", ScenarioMethod = new ScenarioMethod(ScenarioMethodType.Default)
        };
        var connector = new ConnectorItem
        {
            Source = node,
            ConnectorType = direction,
            InputObject = new CustomScenarioValue { SerializeType = type, ShowType = showType ?? type }
        };
        if (direction == ConnectorType.Input) node.Input.Add(connector);
        else node.Output.Add(connector);
        return connector;
    }

    private sealed class CustomEditor;
    private class BaseValue;
    private sealed class DerivedValue : BaseValue;
}
