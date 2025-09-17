#nullable enable
using System.Diagnostics;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Editor.Gui.Graph;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Graph.Window;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.TimeLine;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.UiModel.ProjectHandling;

internal sealed partial class ProjectView
{
    public readonly NavigationHistory NavigationHistory;
    public readonly NodeSelection NodeSelection;
    public readonly GraphImageBackground GraphImageBackground;
    public readonly NodeNavigation NodeNavigation;
    public Structure Structure => OpenedProject.Structure;

    public IGraphView GraphView { get; set; } = null!; // TODO: remove set accessibility
    public OpenedProject OpenedProject { get; }
    private readonly List<Guid> _compositionPath = [];
    
    private InstanceView? _instView;
    public InstanceView? InstView => _instView;
    public Instance? CompositionInstance => InstView?.Instance;

    public Instance RootInstance => GetRoot();
    private bool _subscribedToRootDisposing;

    private Instance GetRoot()
    {
        var package = OpenedProject.Package;
        if (!package.Symbols.TryGetValue(package.HomeSymbolId, out var rootSymbol))
        {
            throw new Exception("Root symbol not found in project.");
        }

        if (!rootSymbol.TryGetParentlessInstance(out var rootInstance))
        {
            throw new Exception("Root instance could not be created?");
        }
                
        if(!_subscribedToRootDisposing)
        {
            _subscribedToRootDisposing = true;
            rootInstance.Disposing += OnRootDisposed;
        }
        return rootInstance;
    }

    private void OnRootDisposed(IResourceConsumer rootAsResourceConsumer)
    {
        _subscribedToRootDisposing = false;
        rootAsResourceConsumer.Disposing -= OnRootDisposed;

        NodeSelection.Clear();
        FlagChanges(ProjectView.ChangeTypes.Children | ProjectView.ChangeTypes.Composition);
    }



    private readonly Stack<InstanceView> _compositionsAwaitingDisposal = [];
    private readonly Stack<InstanceView> _compositionReloadStack = [];
    private bool _waitingOnReload;

    private void SetCompositionOp(Instance? newCompositionOp)
    {
        if (newCompositionOp == null)
        {
            if (_instView != null)
            {
                DisposeComposition(_instView, []);
            }
            
            _instView = null;
            return;
        }
        
        if (_instView != null)
        {
            if (_instView.Is(newCompositionOp))
            {
                return;
            }
            
            var path = new List<Guid>();
            Structure.PopulateInstancePath(newCompositionOp, path);
            DisposeComposition(_instView, path);
        }

        _instView = InstanceView.GetForInstance(newCompositionOp);

        if (_instView != null)
        {
            OnCompositionChanged?.Invoke(this, _instView.SymbolChildId);
        }

        return;

        void DisposeComposition(InstanceView oldComposition, List<Guid> path)
        {
            _compositionsAwaitingDisposal.Push(oldComposition);
            
            while (_compositionsAwaitingDisposal.TryPop(out var compositionToDispose))
            {
                if (path.Contains(compositionToDispose.SymbolChildId))
                {
                    // not ready yet to dispose as it is still part of the current composition stack
                    _compositionsAwaitingDisposal.Push(compositionToDispose);
                    break;
                }

                compositionToDispose.Dispose();
                if (compositionToDispose is { CheckoutCount: 0, IsReadOnly: true, HasBeenModified: true })
                {
                    _compositionReloadStack.Push(compositionToDispose);
                }
            }

        }
    }

    public void CheckDisposal()
    {
        if (_compositionReloadStack.TryPeek(out var nextToReload))
        {
            ShowSymbolReloadDialog(nextToReload);
        }

        return;

        void ShowSymbolReloadDialog(InstanceView composition)
        {
            var symbolChildId = composition.SymbolChildId;
            var symbol = composition.Symbol;
            var symbolChildUi = composition.Instance.Parent?.GetSymbolUi().ChildUis[symbolChildId];
            if (symbolChildUi != null && composition.Instance.Parent != null)
            {
                var parentSymbolId = composition.Instance.Parent.Symbol.Id;
                _duplicateSymbolDialog.ShowNextFrame(); // actually shows this frame
                _duplicateSymbolDialog.Draw(symbolGuid: parentSymbolId,
                                            selectedChildUis: [symbolChildUi],
                                            nameSpace: ref _dupeReadonlyNamespace,
                                            newTypeName: ref _dupeReadonlyName,
                                            description: ref _dupeReadonlyDescription,
                                            isReload: true);

                if (!_waitingOnReload)
                {
                    _duplicateSymbolDialog.Closed += ReloadSymbol;
                    _waitingOnReload = true;
                }
            }

            return;

            void ReloadSymbol()
            {
                _duplicateSymbolDialog.Closed -= ReloadSymbol;
                _compositionReloadStack.Pop();
                _waitingOnReload = false;
                var symbolPackage = (EditorSymbolPackage)symbol.SymbolPackage;
                symbolPackage.Reload(symbol.GetSymbolUi());
            }
        }
    }
    

    public readonly TimeLineCanvas TimeLineCanvas;

    public delegate void CompositionChangedHandler(ProjectView projectView, Guid symbolChildId);
    public event CompositionChangedHandler? OnCompositionChanged;

    public void FlagChanges(ChangeTypes changeTypes)
    {
        OnCompositionContentChanged?.Invoke(this, changeTypes);
    }
    
    public event Action<ProjectView, ChangeTypes>? OnCompositionContentChanged;

    [Flags]
    public enum ChangeTypes
    {
        None =0,
        Connections = 1<< 1,
        Children = 1<<2,
        Layout = 1<<3,
        Composition = 1<<4,
        GraphStyle = 1<<5,
    }

    #region initialization ---------------------------------------------------------
    

    public ProjectView(OpenedProject openedProject, NavigationHistory navigationHistory, NodeSelection nodeSelection, GraphImageBackground graphImageBackground)
    {
        OpenedProject = openedProject;

        NavigationHistory = navigationHistory;
        NodeSelection = nodeSelection;
        GraphImageBackground = graphImageBackground;

        var getCompositionOp = () => CompositionInstance;
        NodeNavigation = new NodeNavigation(openedProject.Structure, NavigationHistory, getCompositionOp);
        TimeLineCanvas = new TimeLineCanvas(NodeSelection, getCompositionOp, TrySetCompositionOpToChild);
        
        SetCompositionOp(RootInstance);
    }

    public static void CreateIndependentComponents(OpenedProject openedProject, out NavigationHistory navigationHistory, out NodeSelection nodeSelection,
                                        out GraphImageBackground graphImageBackground)
    {
        var structure = openedProject.Structure;
        navigationHistory = new NavigationHistory(structure);
        nodeSelection = new NodeSelection(navigationHistory, structure);
        graphImageBackground = new GraphImageBackground(nodeSelection, structure);
    }

    private void SaveUsersViewForCurrentComposition()
    {
        Debug.Assert(CompositionInstance != null);

        if (CompositionInstance == null || GraphView is not ScalableCanvas canvas) 
            return;
    
        var lastSymbolChildId = CompositionInstance.SymbolChildId;

        var visibleCanvasArea = canvas.GetVisibleCanvasArea();
        UserSettings.Config.ViewedCanvasAreaForSymbolChildId[lastSymbolChildId] = visibleCanvasArea;
        UserSettings.Save();
    }

    public bool TrySetCompositionOp(IReadOnlyList<Guid> newIdPath, ScalableCanvas.Transition transition = ScalableCanvas.Transition.JumpIn, Guid? alsoSelectChildId = null)
    {
        var structure = OpenedProject.Structure;
        var newCompositionInstance = structure.GetInstanceFromIdPath(newIdPath);
        var compositionChanged = false;

        if (newCompositionInstance == null)
        {
            var pathString = string.Join('/', structure.GetReadableInstancePath(newIdPath));
            Log.Error("Failed to find instance with path " + pathString);
            return false;
        }

        // Save previous view for user
        if (CompositionInstance != null && CompositionInstance != newCompositionInstance)
        {
            compositionChanged = true;
            SaveUsersViewForCurrentComposition();
        }
        
        // Set new Composition if required
        var targetCompositionAlreadyActive = InstView != null 
                                             && InstView.SymbolChildId == newCompositionInstance.SymbolChildId;
        if (targetCompositionAlreadyActive)
        {
            if (newIdPath[0] != RootInstance.SymbolChildId)
            {
                throw new Exception("Root instance is not the first element in the path");
            }
        }
        else
        {
            SetCompositionOp(newCompositionInstance);
        }
        
        // Although the composition might already be active, the _compositionPath might not have been initialized yet.
        // TODO: This probably is an indication, that this should be refactored into CompositionOp and avoid holding this twice
        _compositionPath.Clear();
        _compositionPath.AddRange(newIdPath);
        
        Debug.Assert(InstView != null);
        
        // Additionally select a child
        NodeSelection.Clear();
        TimeLineCanvas.ClearSelection();

        // This happens when jumping out of an open.
        if (ScalableCanvas != null)
        {
            if (alsoSelectChildId != null && InstView.Instance.Children.TryGetChildInstance(alsoSelectChildId.Value, out var instance))
            {
                //var instance = InstView.Instance.Children[alsoSelectChildId.Value];
                NodeSelection.SetSelection(instance.GetChildUi()!, instance);
                var bounds = NodeSelection.GetSelectionBounds(NodeSelection, instance, 400);
                //var viewScope = ScalableCanvas.GetScopeForCanvasArea(bounds);
                //ScalableCanvas.SetScopeWithTransition(viewScope, compositionChanged ? transition : ScalableCanvas.Transition.Smooth);
                ScalableCanvas.RequestTargetViewAreaWithTransition(bounds, 
                                                               compositionChanged ? transition : ScalableCanvas.Transition.Smooth);
            }
            else
            {
                Debug.Assert(CompositionInstance != null);
            
                var compositionOpSymbolChildId = CompositionInstance.SymbolChildId;
                IEnumerable<ISelectableCanvasObject> childUisValues = InstView.SymbolUi.ChildUis.Values;
                var savedOrValidView = CanvasHelpers.GetSavedOrValidViewForComposition(compositionOpSymbolChildId, childUisValues);
                ScalableCanvas.RequestTargetViewAreaWithTransition(savedOrValidView, transition);
            }
        }
        
        return true;
    }
    
    private ScalableCanvas? ScalableCanvas => GraphView as ScalableCanvas;

    public bool TrySetCompositionOpToChild(Guid symbolChildId)
    {
        List<Guid> path = [.._compositionPath, symbolChildId];
        
        return TrySetCompositionOp(path, ScalableCanvas.Transition.JumpIn);
    }

    public bool TrySetCompositionOpToParent()
    {
        if (_compositionPath.Count == 1)
            return false;

        var previousComposition = InstView;

        // new list as _compositionPath is mutable
        var path = _compositionPath.GetRange(0, _compositionPath.Count - 1);

        // pass the child UI only in case the previous composition was a cloned instance
        return TrySetCompositionOp(path, ScalableCanvas.Transition.JumpOut, previousComposition!.SymbolChildId);
    }
    #endregion
    
    
    
    public void SetBackgroundOutput(Instance instance)
    {
        GraphImageBackground.OutputInstance = instance;
    }
    private readonly DuplicateSymbolDialog _duplicateSymbolDialog = new();
    private string _dupeReadonlyNamespace = "";
    private string _dupeReadonlyName = "";
    private string _dupeReadonlyDescription = "";

    public void Close()
    {
        if(CompositionInstance != null)
            SaveUsersViewForCurrentComposition();
        
        GraphView.Close();
        if (Focused != this)
            return;
        
        foreach (var graphWindow in GraphWindow.GraphWindowInstances)
        {
            if (graphWindow.ProjectView == this)
                graphWindow.CloseView();
            
            if (graphWindow.Config.Visible && graphWindow.ProjectView != null)
                Focused = graphWindow.ProjectView;
        }
    }
    
    public void SetAsFocused()
    {
        Focused = this;
    }
    
    public static ProjectView? Focused { get; private set; }
}