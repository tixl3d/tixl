#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

// ReSharper disable ForCanBeConvertedToForeach

namespace T3.Editor.UiModel.ProjectHandling;

/// <summary>
/// each GraphWindow class should have its own Structure object, and some of this logic should
/// be moved out where applicable (i.e. dealing with packages, selecting root instances, etc.) 
/// </summary>
internal sealed class Structure
{
    private readonly Func<Symbol.Child> _getRootAction;
    
    public Structure(Func<Symbol.Child> getRootAction)
    {
        _getRootAction = getRootAction;
    }

    public Instance? GetRootInstance()
    {
        return GetInstanceFromIdPath([_getRootAction().Id]);
    }
    
    public Instance? GetInstanceFromIdPath(IReadOnlyList<Guid>? childPath)
    {
        if (childPath == null || childPath.Count == 0)
        {
            return null;
        }

        Symbol.Child? rootSymbolChild = _getRootAction();
        if (rootSymbolChild == null)
        {
            Log.Error("Root does not exist? \n" + Environment.StackTrace);
            return null;
        }

        if (childPath[0] != rootSymbolChild.Id)
        {
            //Log.Warning("Can't access instance after root changed.\n" + Environment.StackTrace);
            return null;
        }
        
        var current = rootSymbolChild;
        for (int i = 1; i < childPath.Count; i++)
        {
            if (!current.Symbol.Children.TryGetValue(childPath[i], out current))
            {
                // Commented out, because this frequently happens after deleting pinned Instance
                //Log.Error("Did not find child in path provided.\n" + Environment.StackTrace);
                return null;
            }
        }
        
        _ = current.TryGetOrCreateInstance(childPath, out var instance, out _);
        return instance;
    }

    public List<string> GetReadableInstancePath(IReadOnlyList<Guid>? path, bool includeLeave = true)
    {
        if (path == null || (includeLeave && path.Count == 0) || (!includeLeave && path.Count == 1))
            return ["Path empty"];

        var instance = GetInstanceFromIdPath(path);

        if (instance == null)
            return ["Path invalid"];

        var newList = new List<string>();

        var isFirst = true;

        while (true)
        {
            var parent = instance.Parent;
            if (parent == null)
            {
                break;
            }

            if (!includeLeave && isFirst)
            {
                isFirst = false;
                instance = parent;
                continue;
            }

            isFirst = false;

            var parentSymbolUi = parent.GetSymbolUi();
            var childUisWithThatType = parentSymbolUi.ChildUis.Values
                                                     .Where(c => c.SymbolChild.Symbol == instance.Symbol)
                                                     .ToList();
            var indexLabel = "";

            var symbolUiChild = childUisWithThatType.Single(c => c.Id == instance.SymbolChildId);

            if (childUisWithThatType.Count > 1)
            {
                var index = childUisWithThatType.IndexOf(symbolUiChild);
                indexLabel = $"#{index}";
            }

            var readableNameSuffice = !string.IsNullOrEmpty(symbolUiChild.SymbolChild.Name)
                                          ? $" ({symbolUiChild.SymbolChild.Name})"
                                          : "";

            newList.Insert(0, instance.Symbol.Name + indexLabel + readableNameSuffice);

            instance = parent;
        }

        return newList;
    }

    public static TimeClip? GetCompositionTimeClip(Instance? compositionOp)
    {
        if (compositionOp == null)
        {
            Log.Error("Can't get time clip from null composition op");
            return null;
        }

        foreach (var clipProvider in compositionOp.Outputs.OfType<ITimeClipProvider>())
        {
            return clipProvider.TimeClip;
        }

        return null;
    }

    /// <summary>
    /// This is slow and should be refactored into something else
    /// </summary>
    public static IEnumerable<TimeClip> GetAllTimeClips(Instance compositionOp)
    {
        foreach (var child in compositionOp.Children.Values)
        {
            var outputs = child.Outputs;
            for (var i = 0; i < outputs.Count; i++)
            {
                if (outputs[i] is ITimeClipProvider clipProvider)
                {
                    yield return clipProvider.TimeClip; 
                }
            }
        }
    }

    public static bool TryGetUiAndInstanceInComposition(Guid id,
                                                        Instance compositionOp,
                                                        [NotNullWhen(true)] out SymbolUi.Child? childUi,
                                                        [NotNullWhen(true)] out Instance? instance)
    {
        if (!compositionOp.Children.TryGetChildInstance(id, out instance))
        {
            Log.Warning($"Failed to get instance for {id} in {compositionOp.Symbol.Name}");
            childUi = null;
            return false;
        }
        
        childUi = instance.SymbolChild.GetChildUi();
        return true;
    }

    public static IEnumerable<Symbol> CollectDependingSymbols(Symbol symbol)
    {
        var symbolId = symbol.Id;
        foreach (var s in EditorSymbolPackage.AllSymbols)
        {
            foreach (var child in s.Children.Values)
            {
                if (child.Symbol.Id != symbolId)
                    continue;

                yield return s;
                break;
            }
        }
    }

    internal static HashSet<Guid> CollectConnectedChildren(Symbol.Child child, Symbol compositionSymbol, HashSet<Guid>? set = null)
    {
        set ??= [];

        set.Add(child.Id);
        //var compositionSymbol = composition.Symbol;
        var connectedChildren = (from con in compositionSymbol.Connections
                                 where !con.IsConnectedToSymbolInput && !con.IsConnectedToSymbolOutput
                                 from sourceChild in compositionSymbol.Children.Values
                                 where con.SourceParentOrChildId == sourceChild.Id
                                       && con.TargetParentOrChildId == child.Id
                                 select sourceChild).Distinct().ToArray();

        foreach (var connectedChild in connectedChildren)
        {
            set.Add(connectedChild.Id);
            CollectConnectedChildren(connectedChild, compositionSymbol, set);
        }

        return set;
    }
    
    internal static HashSet<Guid> CollectConnectedChildrenOut(Symbol.Child child, Symbol compositionSymbol, HashSet<Guid>? set = null)
    {
        set ??= [];
        
        set.Add(child.Id);
        var connectedChildren = (from con in compositionSymbol.Connections
                                 where !con.IsConnectedToSymbolInput && !con.IsConnectedToSymbolOutput
                                 from targetChild in compositionSymbol.Children.Values
                                 where (con.SourceParentOrChildId == child.Id
                                       && con.TargetParentOrChildId ==targetChild.Id ) 
                                 select targetChild).Distinct();

        foreach (var connectedChild in connectedChildren)
        {
            set.Add(connectedChild.Id);
            CollectConnectedChildrenOut(connectedChild, compositionSymbol, set);
        }

        return set;
    }
    
    internal static void CollectConnectedChildIds(Symbol compositionSymbol, List<Symbol.Child> selectedChildren, HashSet<Guid>? connectedIds)
    {
        connectedIds ??= [];

        var selection = new HashSet<Symbol.Child>(selectedChildren);
        
        foreach (var selected in selectedChildren)
        {
            connectedIds.Add(selected.Id);
        }
        
        var lastCount = connectedIds.Count;
        
        while(true)
        {
            foreach (var item in selection)
            {
                CollectConnectedChildren(item, compositionSymbol,  connectedIds);
                CollectConnectedChildrenOut(item, compositionSymbol,  connectedIds);
            }

            if (connectedIds.Count == lastCount)
                break;

            foreach (var newId in connectedIds)
            {
                if(compositionSymbol.Children.TryGetValue(newId, out var item))
                {
                    selection.Add(item);
                }
            }
            
            lastCount = connectedIds.Count;
        } 
    }
    
    
    /// <summary>
    /// Scan all slots required for updating a Slot.
    /// This can be used for invalidation and cycle checking. 
    /// </summary>
    private static HashSet<ISlot> CollectSlotDependencies(ISlot slot, HashSet<ISlot>? all = null)
    {
        all ??= [];

        var stack = new Stack<ISlot>();
        stack.Push(slot);

        while (stack.Count > 0)
        {
            var currentSlot = stack.Pop();

            if (!all.Add(currentSlot))
                continue;

            if (currentSlot.TryGetFirstConnection(out var firstConnection))
            {
                stack.Push(firstConnection);
            }
            else
            {
                var op = currentSlot.Parent;
                var opInputs = op.Inputs;

                for (int i = 0; i < opInputs.Count; i++)
                {
                    var input = opInputs[i];

                    // Skip if not connected
                    if (!input.TryGetFirstConnection(out var connectedCompInputSlot))
                        continue;

                    if (input.TryGetAsMultiInput(out var multiInput))
                    {
                        var collectedInputs = multiInput.GetCollectedInputs();
                        for (var j = 0; j < collectedInputs.Count; j++)
                        {
                            stack.Push(collectedInputs[j]);
                        }
                    }
                    else
                    {
                        stack.Push(connectedCompInputSlot);
                    }
                }
            }
        }

        return all;
    }

    /** Returns true if connecting the outputSlot to an input of the op with a symbolChildId would result in a cycle */
    internal static bool CheckForCycle(ISlot outputSlot, Guid targetOpId)
    {
        var linkedSlots = CollectSlotDependencies(outputSlot);
        foreach (var linkedSlot in linkedSlots)
        {
            if (linkedSlot.Parent.SymbolChildId != targetOpId)
                continue;

            return true;
        }

        return false;
    }

    /** Returns true if connecting the outputSlot to an input of the op with a symbolChildId would result in a cycle */
    internal static bool CheckForCycle(Instance sourceInstance, Guid targetOpId)
    {
        var linkedSlots = new HashSet<ISlot>();
        foreach (var inputSlot in sourceInstance.Inputs)
        {
            CollectSlotDependencies(inputSlot, linkedSlots);
        }

        //var linkedSlots = CollectSlotDependencies(outputSlot);
        foreach (var linkedSlot in linkedSlots)
        {
            if (linkedSlot.Parent.SymbolChildId != targetOpId)
                continue;

            return true;
        }

        return false;
    }

    internal static bool CheckForCycle(Symbol compositionSymbol, Symbol.Connection connection)
    {
        var dependingSourceItemIds = new HashSet<Guid>();

        CollectDependentChildren(connection.SourceParentOrChildId);

        return dependingSourceItemIds.Contains(connection.TargetParentOrChildId);

        void CollectDependentChildren(Guid sourceChildId)
        {
            if (sourceChildId == Guid.Empty || !dependingSourceItemIds.Add(sourceChildId))
                return;

            // find all connections into child...
            foreach (var c in compositionSymbol.Connections)
            {
                if (sourceChildId == Guid.Empty || c.TargetParentOrChildId != sourceChildId)
                    continue;

                CollectDependentChildren(c.SourceParentOrChildId);
            }
        }
    }

    public static IEnumerable<Instance> CollectParentInstances(Instance compositionOp)
    {
        var parents = new List<Instance>();
        var op = compositionOp;

        while (op.Parent != null)
        {
            op = op.Parent;
            parents.Insert(0, op);
        }

        return parents;
    }

    public static void PopulateInstancePath(Instance instance, IList<Guid> path)
    {
        path.Insert(0, instance.SymbolChildId);
        var parent = instance.Parent;
        while (parent != null)
        {
            path.Insert(0, parent.SymbolChildId);
            parent = parent.Parent;
        }
    }

    public static bool TryGetInstanceFromPath(IReadOnlyList<Guid> entrySourceIdPath, out Instance? hoveredSourceInstance,
                                              out List<string> readableInstancePath)
    {
        var hasInstancePath = entrySourceIdPath.Count > 0;
        var focusedView = ProjectView.Focused;

        var childIdPath = entrySourceIdPath.ToList();
        hoveredSourceInstance = hasInstancePath && focusedView != null
                                    ? focusedView.Structure.GetInstanceFromIdPath(childIdPath)
                                    : null;

        readableInstancePath = hoveredSourceInstance != null ? focusedView!.Structure.GetReadableInstancePath(childIdPath) : [];
        return hasInstancePath;
    }
}