#nullable enable
using System.Diagnostics;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Editor.Gui.Interaction.TransformGizmos;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Selection;

/// <summary>
/// Some notes on selection handling:
/// 
/// - We can't store references to instances because this leads to null-references and reference leaks. For this
///   reason we save id-paths: Basically the nesting path of instances from the dashboard operator down.
///
/// - Frequently we want to select the parent operator when clicking on the background of a composition (e.g. to
///   show its parameters.  
/// </summary>
internal sealed class NodeSelection : ISelection
{
    public NodeSelection(NavigationHistory navigationHistory, Structure structure)
    {
        NavigationHistory = navigationHistory;
        _structure = structure;
    }

    //public readonly HashSet<Guid> HoveredIds = [];
    public readonly HashSet<Guid> PinnedIds = [];

    public void Clear()
    {
        TransformGizmoHandling.ClearSelectedTransformables();
        Selection.Clear();
    }

    /// <summary>
    /// This called when clicking on background
    /// </summary>
    public void SetSelectionToComposition(Instance instance)
    {
        NavigationHistory.UpdateSelectedInstance(instance);
        Clear();
        _childUiInstanceIdPaths.Clear();
        _selectedCompositionPath = instance.InstancePath;
    }

    /// <summary>
    /// Replaces the current selection with the given node
    /// </summary>
    /// <param name="node"></param>
    /// <param name="instance">required if the object is a <see cref="SymbolUi.Child"/></param>
    public void SetSelection(ISelectableCanvasObject node, Instance? instance = null)
    {
        Clear();
        AddSelection(node, instance);

        if (node is SymbolUi.Child)
        {
            Debug.Assert(instance != null);
            NavigationHistory.UpdateSelectedInstance(instance);
        }
    }

    public void AddSelection(ISelectableCanvasObject node, Instance? instance = null)
    {
        if (Selection.Contains(node))
            return;

        if (node is SymbolUi.Child childUi)
        {
            ArgumentNullException.ThrowIfNull(instance);
            _childUiInstanceIdPaths[childUi] = instance.InstancePath;
            if (instance is ITransformable transformable)
            {
                TransformGizmoHandling.RegisterSelectedTransformable(childUi, transformable);
            }
        }

        Selection.Add(node);
    }

    public void SelectCompositionChild(Instance compositionOp, Guid id)
    {
        if (!Structure.TryGetUiAndInstanceInComposition(id, compositionOp, out var childUi, out var instance))
            return;

        AddSelection(childUi, instance);
    }

    public IEnumerable<T> GetSelectedNodes<T>() where T : ISelectableCanvasObject
    {
        foreach (var item in Selection)
        {
            if (item is T typedItem)
                yield return typedItem;
        }
    }

    public bool IsNodeSelected(ISelectableCanvasObject node) => Selection.Contains(node);

    public bool IsAnythingSelected() => Selection.Count > 0;

    /// <summary>
    /// Returns null if more than one
    /// </summary>
    public Instance? GetSelectedInstanceWithoutComposition()
    {
        if (Selection.Count != 1)
            return null;

        var selection = GetFirstSelectedInstance();
        
        // Clear invalid or obsolete selection
        //if(selection==null)
        //    Clear();
        
        return selection == _structure.GetInstanceFromIdPath(_selectedCompositionPath) ? null : selection;
    }

    public Instance? GetFirstSelectedInstance()
    {
        if (Selection.Count == 0)
            return _structure.GetInstanceFromIdPath(_selectedCompositionPath);

        if (Selection[0] is SymbolUi.Child firstNode)
        {
            if (!_childUiInstanceIdPaths.TryGetValue(firstNode, out var idPath))
            {
                Log.Error("Failed to access id-path of selected childUi " + firstNode.SymbolChild.Name);
                //Clear();
                return null;
            }

            return _structure.GetInstanceFromIdPath(idPath);
        }

        return null;
    }

    public IEnumerable<SymbolUi.Child> GetSelectedChildUis() => GetSelectedNodes<SymbolUi.Child>();

    public IEnumerable<Instance> GetSelectedInstances()
    {
        return GetSelectedNodes<SymbolUi.Child>()
              .Where(x => _childUiInstanceIdPaths.ContainsKey(x))
              .Select(symbolChildUi =>
                      {
                          var idPath = _childUiInstanceIdPaths[symbolChildUi];
                          return _structure.GetInstanceFromIdPath(idPath)!;
                      });
    }

    /// <summary>
    /// Returns null if there are other selections
    /// </summary>
    public Instance? GetSelectedComposition() => Selection.Count > 0 ? null : _structure.GetInstanceFromIdPath(_selectedCompositionPath);

    public Guid? GetSelectionSymbolChildId()
    {
        if (_selectedCompositionPath == null)
            return null;
        
        if (Selection.Count == 0)
            return _selectedCompositionPath[^1];

        if (Selection[0] is not SymbolUi.Child firstNode)
            return null;

        return firstNode.SymbolChild.Id;
    }

    public void DeselectCompositionChild(Instance compositionOp, Guid symbolChildId)
    {
        if (!Structure.TryGetUiAndInstanceInComposition(symbolChildId, compositionOp, out var childUi, out var instance))
            return;

        DeselectNode(childUi, instance);
    }

    public void DeselectNode(ISelectableCanvasObject node, Instance? instance = null)
    {
        Selection.Remove(node);
        if (instance is ITransformable transformable)
        {
            TransformGizmoHandling.ClearDeselectedTransformableNode(transformable);
        }
    }

    public Instance? GetInstanceForChildUi(SymbolUi.Child symbolChildUi)
    {
        var idPath = _childUiInstanceIdPaths[symbolChildUi];
        return _structure.GetInstanceFromIdPath(idPath);
    }



    public bool IsSelected(ISelectableCanvasObject item)
    {
        // Avoid linq
        foreach (var i in Selection)
        {
            if (i.Id == item.Id)
                return true;
        }

        return false;
    }

    public static IEnumerable<ISelectableCanvasObject?> GetSelectableChildren(Instance compositionOp)
    {
        List<ISelectableCanvasObject?> selectableItems = [];
        selectableItems.Clear();
        try
        {
            var symbolUi = compositionOp.GetSymbolUi();
            var selectableCanvasObjects = compositionOp.Children.Values.Select(x => x.GetChildUi());
            selectableItems.AddRange(selectableCanvasObjects);

            selectableItems.AddRange(symbolUi.InputUis.Values);
            selectableItems.AddRange(symbolUi.OutputUis.Values);
            selectableItems.AddRange(symbolUi.Annotations.Values);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to get selected children. This might be caused be recompilation. {e.Message} Please report");
        }

        return selectableItems;
    }

    internal static ImRect GetSelectionBounds(NodeSelection nodeSelection, Instance compositionOp, float padding = 50)
    {
        var selectedOrAll = nodeSelection.IsAnythingSelected()
                                ? nodeSelection.GetSelectedNodes<ISelectableCanvasObject>().ToArray()
                                : NodeSelection.GetSelectableChildren(compositionOp).ToArray();

        if (selectedOrAll.Length == 0)
            return new ImRect();

        var firstElement = selectedOrAll[0];
        if(firstElement == null)
            return new ImRect();
            
        var bounds = new ImRect(firstElement.PosOnCanvas, firstElement.PosOnCanvas + firstElement.Size);
        foreach (var element in selectedOrAll)
        {
            if (element == null)
                continue;
            
            if (float.IsInfinity(element.PosOnCanvas.X) || float.IsInfinity(element.PosOnCanvas.Y))
                element.PosOnCanvas = Vector2.Zero;

            bounds.Add(element.PosOnCanvas);
            bounds.Add(element.PosOnCanvas + element.Size);
        }

        bounds.Expand(padding);
        return bounds;
    }
    
    
    public readonly NavigationHistory NavigationHistory;
    private readonly Structure _structure;

    //TODO: This should be a dict because selecting many (> 500) ops will lead to framedrops
    public readonly List<ISelectableCanvasObject> Selection = new();
    private IReadOnlyList<Guid>? _selectedCompositionPath;
    private readonly Dictionary<SymbolUi.Child, IReadOnlyList<Guid>> _childUiInstanceIdPaths = new();
}