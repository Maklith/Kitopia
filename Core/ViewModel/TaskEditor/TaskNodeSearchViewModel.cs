using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.CustomScenario;
using Pinyin.NET;

namespace Core.ViewModel.TaskEditor;

public class NodeSearchItemViewModel
{
    public ScenarioMethodNode Node { get; }
    public string Title => Node.Title;
    public string? Detail { get; set; }

    public NodeSearchItemViewModel(ScenarioMethodNode node)
    {
        Node = node;
    }
}

public partial class TaskNodeSearchViewModel : ObservableObject
{
    private readonly ConnectorItem _sourceConnector;
    private readonly TaskEditorViewModel _editorViewModel;
    private readonly Point _location;
    private readonly List<ScenarioMethodNode> _allCompatibleNodes = new();
    private PinyinSearcher<ScenarioMethodNode>? _pinyinSearcher;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<NodeSearchItemViewModel> _filteredNodes = new();

    [ObservableProperty]
    private NodeSearchItemViewModel? _selectedNode;
    
    // Action to close the window
    public Action? CloseAction { get; set; }

    public TaskNodeSearchViewModel(ConnectorItem sourceConnector, TaskEditorViewModel editorViewModel, Point location)
    {
        _sourceConnector = sourceConnector;
        _editorViewModel = editorViewModel;
        _location = location;

        LoadCompatibleNodes();
        
        // Setup search throttling
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SearchText))
            {
                FilterNodes();
            }
        };
    }

    private void LoadCompatibleNodes()
    {
        var rootGroup = ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup;
        TraverseAndCollect(rootGroup);

        _pinyinSearcher = new PinyinSearcher<ScenarioMethodNode>(_allCompatibleNodes, node => node.Title);
        FilterNodes();
    }

    private void TraverseAndCollect(ScenarioMethodCategoryGroup group)
    {
        foreach (var methodNode in group.Methods.Values)
        {
            if (IsCompatible(methodNode))
            {
                _allCompatibleNodes.Add(methodNode);
            }
        }

        foreach (var childGroup in group.Childrens.Values)
        {
            TraverseAndCollect(childGroup);
        }
    }

    private bool IsCompatible(ScenarioMethodNode node)
    {
        IEnumerable<ConnectorItem> targetConnectors = _sourceConnector.ConnectorType == ConnectorType.Output 
            ? node.Input 
            : node.Output;

        if (_sourceConnector.ConnectorType == ConnectorType.Both)
        {
             targetConnectors = node.Input.Concat(node.Output);
        }

        foreach (var target in targetConnectors)
        {
             if (CheckCompatibility(_sourceConnector, target))
             {
                 return true;
             }
        }

        return false;
    }

    private bool CheckCompatibility(ConnectorItem source, ConnectorItem target)
    {
        if (target == null) return false;
        
        if (source.ConnectorType != ConnectorType.Both && source.ConnectorType == target.ConnectorType)
        {
            return false; 
        }

        if (source.InputObject.ShowType.FullName != target.InputObject.ShowType.FullName)
        {
            if (target.InputObject.ShowType.FullName == "System.Object" || 
                source.InputObject.ShowType.FullName == "System.Object")
            {
                return true;
            }

            if (target.InputObject.ShowType.IsAssignableFrom(source.InputObject.ShowType))
            {
                return true;
            }
            
            return false;
        }

        return true;
    }

    private void FilterNodes()
    {
        List<ScenarioMethodNode> results;
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            results = _allCompatibleNodes;
        }
        else
        {
            results = _pinyinSearcher?.Search(SearchText).Select(x => x.Source).ToList() ?? new List<ScenarioMethodNode>();
        }

        var wrapperList = new List<NodeSearchItemViewModel>();
        var groups = results.GroupBy(x => x.Title);

        foreach (var group in groups)
        {
            var items = group.ToList();
            if (items.Count > 1)
            {
                // Disambiguate
                foreach (var item in items)
                {
                    var wrapper = new NodeSearchItemViewModel(item);
                    var inputs = string.Join(", ", item.Input.Skip(1).Select(i => i.InputObject.ShowType.Name));
                    var outputs = string.Join(", ", item.Output.Select(o => o.InputObject.ShowType.Name));
                    
                    if (string.IsNullOrEmpty(inputs)) inputs = "None";
                    if (string.IsNullOrEmpty(outputs)) outputs = "None";

                    wrapper.Detail = $"In: {inputs} | Out: {outputs}";
                    wrapperList.Add(wrapper);
                }
            }
            else
            {
                wrapperList.Add(new NodeSearchItemViewModel(items[0]));
            }
        }

        FilteredNodes = new ObservableCollection<NodeSearchItemViewModel>(wrapperList);
        
        if (FilteredNodes.Count > 0)
        {
            SelectedNode = FilteredNodes[0];
        }
    }

    [RelayCommand]
    public void ConfirmSelect(NodeSearchItemViewModel? item)
    {
        var nodeToUse = item ?? SelectedNode;
        if (nodeToUse == null) return;

        var newNode = (ScenarioMethodNode)nodeToUse.Node.Copy();
        newNode.Location = _location;
        
        ConnectorItem? bestTarget = null;
        
        IEnumerable<ConnectorItem> candidates = _sourceConnector.ConnectorType == ConnectorType.Output 
            ? newNode.Input 
            : newNode.Output;
            
        if (_sourceConnector.ConnectorType == ConnectorType.Both)
             candidates = newNode.Input.Concat(newNode.Output);

        foreach (var target in candidates)
        {
            if (CheckCompatibility(_sourceConnector, target))
            {
                bestTarget = target;
                break;
            }
        }

        _editorViewModel.Scenario.Nodes.Add(newNode);
        
        if (bestTarget != null)
        {
             if (_sourceConnector.ConnectorType == ConnectorType.Input)
                 _editorViewModel.Connect(bestTarget, _sourceConnector);
             else
                 _editorViewModel.Connect(_sourceConnector, bestTarget);
        }
        
        CloseAction?.Invoke();
    }
}