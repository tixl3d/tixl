using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.Window;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>Verifies cleanup state restoration, view refresh, cancellation, and shake undo grouping.</summary>
public sealed partial class ConnectionStrokeTests
{
    /// <summary>Cleanup is selective, refreshes each view once, and restores metadata before ordered wires are replayed.</summary>
    [Fact]
    public void CleanupRestoresMetadataAndOrderedConnectionsAcrossViews()
    {
        var anchorDefinition = _package.Add(typeof(Types.Routing.TestReroute<float>));
        var inputId = anchorDefinition.Symbol.InputDefinitions[0].Id;
        var outputId = anchorDefinition.Symbol.OutputDefinitions[0].Id;
        var anchor = AddAnchor(new Vector2(170, 220));
        var blank = AddAnchor(new Vector2(190, 250));
        var incomingOnly = AddAnchor(new Vector2(220, 260));
        var outgoingOnly = AddAnchor(new Vector2(220, 300));
        var multiDefinition = _package.Add(typeof(Types.Routing.MultiReroute));
        var target = _home.AddChild(multiDefinition.Symbol, Guid.NewGuid(), new Vector2(400, 220), new Vector2(110, 25));
        var sourceId = _connection.SourceItem.Id;
        var sourceOutput = _connection.SourceOutput.Id;
        var incoming = new Symbol.Connection(sourceId, sourceOutput, anchor.Id, inputId);
        var routed = new Symbol.Connection(anchor.Id, outputId, target.Id, multiDefinition.Symbol.InputDefinitions[0].Id);
        _home.Symbol.AddConnection(incoming, 0);
        _home.Symbol.AddConnection(new Symbol.Connection(sourceId, sourceOutput, incomingOnly.Id, inputId), 0);
        _home.Symbol.AddConnection(routed, 0);
        _home.Symbol.AddConnection(new Symbol.Connection(outgoingOnly.Id, outputId, target.Id, routed.TargetSlotId), 1);
        _home.Symbol.AddConnection(routed.Clone(), 2);
        _context.Layout.ComputeLayout(_context, true);

        var opened = _context.ProjectView.OpenedProject;
        var history = new NavigationHistory(opened.Structure);
        var secondView = new ProjectView(opened, history, new NodeSelection(history, opened.Structure), null!);
        var secondCanvas = (MagGraphView)Activator.CreateInstance(typeof(MagGraphView), BindingFlags.Instance | BindingFlags.NonPublic,
                                                                 null, [secondView], null)!;
        secondView.GraphView = secondCanvas;
        var secondContext = (GraphUiContext)typeof(MagGraphView).GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(secondCanvas)!;
        secondContext.Layout.ComputeLayout(secondContext, true);
        var firstWindow = RegisterWindow(_context.ProjectView);
        var secondWindow = RegisterWindow(secondView);
        var previousFocus = ProjectView.Focused;
        typeof(ProjectView).GetProperty("Focused")!.SetValue(null, _context.ProjectView);
        var firstRefreshes = 0;
        var secondRefreshes = 0;
        _context.ProjectView.OnCompositionContentChanged += (_, _) => firstRefreshes++;
        secondView.OnCompositionContentChanged += (_, _) => secondRefreshes++;
        // Attach the same layout notification callbacks as CreateWithComponents.
        var notify = typeof(MagGraphView).GetMethod("CompositionContentChangedHandler", BindingFlags.NonPublic | BindingFlags.Instance)!;
        _context.ProjectView.OnCompositionContentChanged += notify.CreateDelegate<Action<ProjectView, ProjectView.ChangeTypes>>(_canvas);
        secondView.OnCompositionContentChanged += notify.CreateDelegate<Action<ProjectView, ProjectView.ChangeTypes>>(secondCanvas);
        try
        {
            foreach (var context in new[] { _context, secondContext })
            {
                context.Selector.SetSelection(anchor, context.Layout.Items[anchor.Id].Instance);
                context.Selector.AddSelection(blank, context.Layout.Items[blank.Id].Instance);
            }
            anchor.SymbolChild.Name = "saved anchor";
            anchor.Comment = "keep this note";
            anchor.SectionId = Guid.NewGuid();
            anchor.Style = SymbolUi.Child.Styles.Expanded;
            anchor.SnapshotGroupIndex = 2;
            anchor.SnapshotEnabledInputIds = [inputId];
            anchor.ConnectionStyleOverrides[outputId] = SymbolUi.Child.ConnectionStyles.FadedOut;
            var childInput = anchor.SymbolChild.Inputs[inputId];
            ((InputValue<float>)childInput.Value).Value = 17.5f;
            childInput.IsDefault = false;
            anchor.SymbolChild.Outputs[outputId].IsDisabled = true;
            anchor.SymbolChild.Outputs[outputId].DirtyFlagTrigger = DirtyFlagTrigger.Animated;
            var savedSectionId = anchor.SectionId;
            var macro = _context.StartMacroCommand("Disconnect anchor");
            var addedBlank = AddAnchor(new Vector2(100, 400));
            macro.AddAndExecCommand(new DeleteConnectionCommand(_home.Symbol, routed, 2));
            macro.AddAndExecCommand(new DeleteConnectionCommand(_home.Symbol, routed, 0));
            macro.AddAndExecCommand(new VerifyBeforeUndo(new DeleteConnectionCommand(_home.Symbol, incoming, 0), VerifyRestoredMetadata));
            _context.CompleteMacroCommand();
            Assert.Equal(4, macro.Count);
            Assert.False(_home.ChildUis.ContainsKey(anchor.Id));
            Assert.True(_home.ChildUis.ContainsKey(blank.Id));
            Assert.True(_home.ChildUis.ContainsKey(addedBlank.Id));
            Assert.True(_home.ChildUis.ContainsKey(incomingOnly.Id));
            Assert.True(_home.ChildUis.ContainsKey(outgoingOnly.Id));
            Assert.Equal(1, firstRefreshes);
            Assert.Equal(1, secondRefreshes);
            var layoutDirty = typeof(MagGraphLayout).GetProperty("StructureFlaggedAsChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True((bool)layoutDirty.GetValue(_context.Layout)!);
            Assert.True((bool)layoutDirty.GetValue(secondContext.Layout)!);
            foreach (var context in new[] { _context, secondContext })
            {
                Assert.DoesNotContain(context.Selector.Selection, node => node.Id == anchor.Id);
                Assert.Contains(blank, context.Selector.Selection);
            }
            UndoRedoStack.Undo();
            VerifyRestoredMetadata();
            Assert.Equal(new[] { anchor.Id, outgoingOnly.Id, anchor.Id }, TargetSources());
            Assert.Equal(2, firstRefreshes);
            Assert.Equal(2, secondRefreshes);
            UndoRedoStack.Redo();
            Assert.False(_home.ChildUis.ContainsKey(anchor.Id));
            Assert.Equal(new[] { outgoingOnly.Id }, TargetSources());
            Assert.Equal(3, firstRefreshes);
            Assert.Equal(3, secondRefreshes);
            UndoRedoStack.Undo();
            Assert.Equal(new[] { anchor.Id, outgoingOnly.Id, anchor.Id }, TargetSources());

            void VerifyRestoredMetadata()
            {
                var restored = _home.ChildUis[anchor.Id];
                Assert.Equal("saved anchor", restored.SymbolChild.Name);
                Assert.Equal("keep this note", restored.Comment);
                Assert.Equal(new Vector2(170, 220), restored.PosOnCanvas);
                Assert.Equal(MagGraphItem.RerouteSize, restored.Size);
                Assert.Equal(savedSectionId, restored.SectionId);
                Assert.Equal(SymbolUi.Child.Styles.Expanded, restored.Style);
                Assert.Equal(2, restored.SnapshotGroupIndex);
                Assert.Equal(new[] { inputId }, restored.SnapshotEnabledInputIds);
                Assert.Equal(SymbolUi.Child.ConnectionStyles.FadedOut, restored.ConnectionStyleOverrides[outputId]);
                Assert.Equal(17.5f, ((InputValue<float>)restored.SymbolChild.Inputs[inputId].Value).Value);
                Assert.False(restored.SymbolChild.Inputs[inputId].IsDefault);
                Assert.True(restored.SymbolChild.Outputs[outputId].IsDisabled);
                Assert.Equal(DirtyFlagTrigger.Animated, restored.SymbolChild.Outputs[outputId].DirtyFlagTrigger);
            }
            Guid[] TargetSources() => _home.Symbol.Connections.Where(c => c.TargetParentOrChildId == target.Id).Select(c => c.SourceParentOrChildId).ToArray();
        }
        finally
        {
            GraphWindow.GraphWindowInstances.Remove(firstWindow);
            GraphWindow.GraphWindowInstances.Remove(secondWindow);
            typeof(ProjectView).GetProperty("Focused")!.SetValue(null, previousFocus);
        }
        SymbolUi.Child AddAnchor(Vector2 position) => _home.AddChild(anchorDefinition.Symbol, Guid.NewGuid(), position, MagGraphItem.RerouteSize);
    }

    /// <summary>Cancelling restores edited wires and discards pending cleanup without replacing the anchor UI.</summary>
    [Fact]
    public void CancelledCleanupLeavesAnchorAndHistoryIntact()
    {
        var definition = _package.Add(typeof(Types.Routing.TestReroute<float>));
        var anchor = _home.AddChild(definition.Symbol, Guid.NewGuid(), Vector2.Zero, MagGraphItem.RerouteSize);
        var wire = new Symbol.Connection(_connection.SourceItem.Id, _connection.SourceOutput.Id, anchor.Id, definition.Symbol.InputDefinitions[0].Id);
        _home.Symbol.AddConnection(wire, 0);
        var macro = _context.StartMacroCommand("Cancelled cut");
        macro.AddAndExecCommand(new DeleteConnectionCommand(_home.Symbol, wire, 0));
        _context.CancelMacroCommand();
        Assert.Same(anchor, _home.ChildUis[anchor.Id]);
        Assert.Contains(wire, _home.Symbol.Connections);
        Assert.Null(_context.MacroCommand);
        Assert.False(UndoRedoStack.CanUndo);
        var empty = _context.StartMacroCommand("No isolated anchors");
        _context.CompleteMacroCommand();
        Assert.Equal(0, empty.Count);
    }

    /// <summary>Shake keeps its existing move/disconnect undo groups and preserves other selected operators.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShakeCleanupPreservesUndoGroups(bool mixedSelection)
    {
        _home.Symbol.RemoveConnection(_connection.AsSymbolConnection(), 0);
        var definition = _package.Add(typeof(Types.Routing.TestReroute<float>));
        var anchor = _home.AddChild(definition.Symbol, Guid.NewGuid(), new Vector2(200, 100), MagGraphItem.RerouteSize);
        _home.Symbol.AddConnection(new Symbol.Connection(_connection.SourceItem.Id, _connection.SourceOutput.Id,
                                                        anchor.Id, definition.Symbol.InputDefinitions[0].Id), 0);
        _home.Symbol.AddConnection(new Symbol.Connection(anchor.Id, definition.Symbol.OutputDefinitions[0].Id,
                                                        _connection.TargetItem.Id, _connection.TargetInput.Id), 0);
        var selected = new List<ISelectableCanvasObject> { anchor };
        SymbolUi.Child? ordinary = null;
        if (mixedSelection)
        {
            ordinary = _home.AddChild(_connection.TargetItem.SymbolChild!.Symbol, Guid.NewGuid(), new Vector2(200, 200), new Vector2(110, 25));
            selected.Add(ordinary);
        }
        _context.Layout.ComputeLayout(_context, true);
        var window = RegisterWindow(_context.ProjectView);
        try
        {
            foreach (var child in selected)
                _context.Selector.AddSelection(child, _context.Layout.Items[child.Id].Instance);
            _context.ItemMovement.SetDraggedItems(selected);
            _context.ItemMovement.StartDragOperation(_context);
            anchor.PosOnCanvas += new Vector2(32, 0);
            var shook = (bool)typeof(MagItemMovement).GetMethod("HandleShakeDisconnect", BindingFlags.Instance | BindingFlags.NonPublic)!
                                                     .Invoke(_context.ItemMovement, [_context])!;
            Assert.True(shook);
            Assert.Null(_context.MacroCommand);
            Assert.Equal(GraphStates.WaitForMouseRelease, _context.StateMachine.CurrentState);
            Assert.False(_home.ChildUis.ContainsKey(anchor.Id));
            Assert.Single(_home.Symbol.Connections);
            if (ordinary != null)
            {
                Assert.True(_home.ChildUis.ContainsKey(ordinary.Id));
                Assert.Contains(ordinary, _context.Selector.Selection);
            }
            Assert.Equal("Disconnect nodes", UndoRedoStack.GetNextUndoTitle());
            UndoRedoStack.Undo();
            Assert.Equal(2, _home.Symbol.Connections.Count);
            Assert.Equal(new Vector2(232, 100), _home.ChildUis[anchor.Id].PosOnCanvas);
            Assert.Equal("Move nodes", UndoRedoStack.GetNextUndoTitle());
            UndoRedoStack.Undo();
            Assert.Equal(new Vector2(200, 100), _home.ChildUis[anchor.Id].PosOnCanvas);
            Assert.False(UndoRedoStack.CanUndo);
            UndoRedoStack.Redo();
            UndoRedoStack.Redo();
            Assert.False(_home.ChildUis.ContainsKey(anchor.Id));
            Assert.Single(_home.Symbol.Connections);
        }
        finally
        {
            GraphWindow.GraphWindowInstances.Remove(window);
        }
    }

    /// <summary>Models registered graph windows without constructing unrelated window chrome.</summary>
    private static GraphWindow RegisterWindow(ProjectView view)
    {
        var window = (GraphWindow)RuntimeHelpers.GetUninitializedObject(typeof(GraphWindow));
        typeof(GraphWindow).GetProperty("ProjectView")!.SetValue(window, view);
        GraphWindow.GraphWindowInstances.Add(window);
        return window;
    }

    /// <summary>Checks restored state before the real connection command runs its undo.</summary>
    private sealed class VerifyBeforeUndo(ICommand command, Action verify) : ICommand
    {
        /// <summary>Preserves the wrapped command's history label.</summary>
        public string Name => command.Name;
        /// <summary>Preserves the wrapped command's undo contract.</summary>
        public bool IsUndoable => command.IsUndoable;
        /// <summary>Executes the real graph edit.</summary>
        public void Do() => command.Do();
        /// <summary>Verifies cleanup restored the anchor before reconnecting its wire.</summary>
        public void Undo() { verify(); command.Undo(); }
    }
}
