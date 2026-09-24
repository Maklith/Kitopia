using System.Collections.ObjectModel;
using PluginCore.CustomScenario;

namespace Kitopia.Desktop.Features.CustomScenario;

internal static class ScenarioGraph
{
    public static bool HasCycle(IEnumerable<ConnectionItem> connections)
    {
        var edges = connections.ToArray();
        var visiting = new HashSet<ScenarioNodeBase>();
        var visited = new HashSet<ScenarioNodeBase>();

        bool Visit(ScenarioNodeBase node)
        {
            if (visiting.Contains(node)) return true;
            if (!visited.Add(node)) return false;

            visiting.Add(node);
            foreach (var edge in edges.Where(edge => edge.Source.Source == node))
                if (Visit(edge.Target.Source)) return true;
            visiting.Remove(node);
            return false;
        }

        return edges.Any(edge => Visit(edge.Source.Source));
    }

    public static bool WouldCreateCycle(IEnumerable<ConnectionItem> connections, ConnectorItem source,
        ConnectorItem target)
    {
        if (source.Source == target.Source) return true;

        var replacedFlowOutput = source.InputObject.SerializeType == typeof(NodeConnectorClass);
        var pending = new Stack<ScenarioNodeBase>();
        var visited = new HashSet<ScenarioNodeBase>();
        pending.Push(target.Source);
        while (pending.TryPop(out var node))
        {
            if (node == source.Source) return true;
            if (!visited.Add(node)) continue;
            foreach (var edge in connections)
                if (edge.Source.Source == node && (!replacedFlowOutput || edge.Source != source))
                    pending.Push(edge.Target.Source);
        }

        return false;
    }

    public static IEnumerable<ScenarioNodeBase> GetFlowSuccessors(ScenarioNodeBase node,
        ObservableCollection<ConnectionItem> connections)
    {
        IEnumerable<ConnectorItem> outputs = node switch
        {
            ScenarioMethodNode methodNode => methodNode.Output.Where(output =>
                output.InputObject.SerializeType == typeof(NodeConnectorClass) &&
                (methodNode.ScenarioMethod.Type != ScenarioMethodType.Condition || !output.IsNotUsed)),
            KnotNodeViewModel knot => [knot.Connector],
            _ => []
        };

        foreach (var output in outputs)
            foreach (var edge in connections.Where(edge => edge.Source == output))
                yield return edge.Target.Source;
    }

    public static IEnumerable<ScenarioNodeBase> GetDataDependencies(ScenarioNodeBase node,
        ObservableCollection<ConnectionItem> connections)
    {
        IEnumerable<ConnectorItem> inputs = node switch
        {
            ScenarioMethodNode methodNode => methodNode.Input.Where(input =>
                input.InputObject.SerializeType != typeof(NodeConnectorClass)),
            KnotNodeViewModel knot => [knot.Connector],
            _ => []
        };

        foreach (var input in inputs)
            foreach (var edge in connections.Where(edge => edge.Target == input))
                yield return edge.Source.Source;
    }

    public static bool HasFlowInput(ScenarioNodeBase node, ObservableCollection<ConnectionItem> connections)
    {
        if (node is KnotNodeViewModel knot)
            return connections.Any(edge => edge.Target == knot.Connector &&
                (edge.Source.InputObject.SerializeType == typeof(NodeConnectorClass) ||
                 edge.Source.Source is KnotNodeViewModel previous && HasFlowInput(previous, connections)));

        return node is ScenarioMethodNode methodNode && methodNode.Input.Any(input =>
            input.InputObject.SerializeType == typeof(NodeConnectorClass) &&
            connections.Any(edge => edge.Target == input));
    }
}
