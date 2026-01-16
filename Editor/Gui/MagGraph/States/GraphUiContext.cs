#nullable enable
using System.Diagnostics;
using T3.Core.Operator;
using T3.Editor.Gui.Dialogs;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using MagItemMovement = T3.Editor.Gui.MagGraph.Interaction.MagItemMovement;

namespace T3.Editor.Gui.MagGraph.States;

/// <summary>
/// Holds the current interaction state of the graph. It is passed as a parameter
/// during most processing and makes "graph-global" components and states accessible to all related components.
/// New instances of the context are created when the composition object or window changes.
///</summary>
/// <remarks>
/// Overall concept of the graph UI system.
///
/// Preface: The node graph is a central piece of Tooll's user interface and probably
/// one of the most complex: For the implementation, we tried to balance the following:
/// - Code should be well-structured and readable.
/// - There should be no mud-ball side effects that lead to inconsistent states and hard-to-reproduce issues.
/// - All interactions should support combined undo/redo out of the box.
/// - The system should be open to implement new features, tweak the design, or adjust behaviors.
/// - Ideally, rendering should be fast.
/// - Cycle checks should be consistent and bulletproof.
/// - Allocations should be avoided if possible.
///
/// The basic components are:
/// - <see cref="Layout"/> holds an intermediate view model that is updated if required. This view model
/// builds referenceable items, view elements, and connections. This makes it much easier to traverse the
/// graph without dictionary lookups. The layout also precomputes the visibility of input links, which simplifies
/// the layout and rendering of connection lines (one of the most complicated parts of the legacy layout).
/// - <see cref="GraphUiContext"/> holds the current interaction state of the graph. It is passed as a parameter
/// during most processing and makes "graph-global" components and states accessible to all related components.
/// New instances of the context are created when the composition object or window changes.
/// - <see cref="StateMachine"/> the state machine is a very bare-bones (no hierarchy or events) implementation
/// of a state machine that handles activation of <see cref="State"/>s. There can only be one state active.
/// Most of the update interaction is done in State.Update() overrides.
/// - <see cref="MagGraphView"/> is a scalable canvas that handles drawing. The Layout sometimes resets
/// the current state.
/// - <see cref="PlaceholderCreation"/> combines drawing and logic that handles creating new operators (it's the
/// "new" SymbolBrowser).
/// - <see cref="ItemMovement"/> handles dragging, snapping, inserting, and unsnapping operators on the canvas.
/// Grouping is handled on the fly by flood-filling "Snapped" Layout-Connections into HashSets.
///
/// Notes:
/// -------
/// Currently, a Context only holds information on a single composition and is discorded as soon as
/// the user navigations to another composition-op (e.g. jumping up, down the composition stack.
///
/// Architectural questions:
/// ------------------------
/// We need to clarify how to deal with modal dialogs like "Edit Comment"
///
/// 
///</remarks>
internal sealed class GraphUiContext
{
    internal GraphUiContext(ProjectView projectView,  MagGraphView view)
    {
        ProjectView = projectView;
        View = view;
        ItemMovement = new MagItemMovement(this, view, Layout, projectView.NodeSelection);
        Placeholder = new PlaceholderCreation();
        //EditCommentDialog = new EditCommentDialog();
        StateMachine = new StateMachine<GraphUiContext>(typeof(GraphStates), GraphStates.Default);// needs to be initialized last
    }

    internal readonly ProjectView ProjectView;
    internal Instance CompositionInstance => ProjectView.CompositionInstance!;
    internal NodeSelection Selector => ProjectView.NodeSelection;

    /** Used for fading out graph on left edge */
    internal float GraphOpacity = 1;

    internal bool PreventInteraction => ProjectView.GraphImageBackground.HasInteractionFocus;
    
    internal readonly MagGraphView View;
    internal readonly MagItemMovement ItemMovement;
    internal readonly PlaceholderCreation Placeholder;
    internal readonly ConnectionHovering ConnectionHovering = new();
    internal readonly MagGraphLayout Layout = new();
    
    internal readonly StateMachine<GraphUiContext> StateMachine;
    internal  MacroCommand? MacroCommand { get; private set; }
    
    /** Keep for continuous update of dragged items */
    internal  ModifyCanvasElementsCommand? MoveElementsCommand;

    // Input picking... (should probably be moved into Placeholder)
    internal Type? DraggedPrimaryOutputType;
    
    internal MagGraphItem? ItemWithActiveCustomUi { get; set; }
    
    /** Set when clicking on a node.
     * Is cleared on the beginning of each frame when in default state.
     */
    internal MagGraphItem? ActiveItem { get; set; }
    
    internal MagGraphItem? ActiveSourceItem;
    internal Guid ActiveSourceOutputId { get; set; }
    internal MagGraphItem.Directions ActiveOutputDirection { get; set; }
    
    internal MagGraphItem? ActiveTargetItem;
    internal Guid ActiveTargetInputId { get; set; }
    internal MagGraphItem.Directions ActiveInputDirection { get; set; }

    /** The item that is currently hovered by the mouse */
    internal MagGraphItem? HoveredItem { get; set; }

    /** Only relevant while picking inputs or hovering an op while dragging connecting end. */
    internal MagGraphItem? ItemForInputSelection;
    
    internal Guid ActiveAnnotationId { get; set; }
    
    /** Used to prevent disconnected inputLines from collapsing... */
    internal readonly HashSet<int> DisconnectedInputHashes = []; 
    
    internal bool TryGetActiveOutputLine(out MagGraphItem.OutputLine outputLine)
    {
        if (ActiveSourceItem == null || ActiveSourceItem.OutputLines.Length == 0)
        {
            outputLine = default;
            return false;
        }

        foreach (var l in ActiveSourceItem
                    .OutputLines)
        {
            if (l.Id != ActiveSourceOutputId) 
                continue;
            
            outputLine = l;
            return true;
        }
        outputLine = default;
        return false;
    }
    
    internal bool TryGetActiveInputLine(out MagGraphItem.InputLine inputLine)
    {
        if (ActiveTargetItem == null || ActiveTargetItem.InputLines.Length == 0)
        {
            inputLine = default;
            return false;
        }
        
        foreach (var l in ActiveTargetItem
                    .InputLines)
        {
            if (l.Id != ActiveTargetInputId) 
                continue;
            
            inputLine = l;
            return true;
        }
        inputLine = default;
        return false;
    }
    
    internal Vector2 PeekAnchorInCanvas;
    internal bool ShouldAttemptToSnapToInput;
    
    internal MacroCommand StartMacroCommand(string title)
    {
        Debug.Assert(MacroCommand == null);
        MacroCommand = new MacroCommand(title);
        return MacroCommand;
    }
    
    internal MacroCommand StartOrContinueMacroCommand(string title)
    {
        MacroCommand ??= new MacroCommand(title);
        return MacroCommand;
    }
    
    internal void CompleteMacroCommand()
    {
        Debug.Assert(MacroCommand != null);
        UndoRedoStack.Add(MacroCommand);
        MacroCommand = null;
    }
    
    internal void CancelMacroCommand()
    {
        Debug.Assert(MacroCommand != null);
        MacroCommand.Undo();
        MacroCommand = null;
    }
    
    // Dialogs
    internal readonly EditCommentDialog EditCommentDialog = new();
    internal readonly AddInputDialog AddInputDialog = new();
    internal readonly AddOutputDialog AddOutputDialog = new();
    internal readonly CombineToSymbolDialog CombineToSymbolDialog = new();
    internal readonly DuplicateSymbolDialog DuplicateSymbolDialog = new();
    public readonly RenameSymbolDialog RenameSymbolDialog = new();

    // Variables for dialogs
    internal string SymbolNameForDialogEdits = "";
    internal string NameSpaceForDialogEdits = "";
    internal string SymbolDescriptionForDialog ="";


    // internal Vector2 PeekAnchorInCanvas => PrimaryOutputItem == null
    //                                            ? Vector2.Zero
    //                                            : new Vector2(PrimaryOutputItem.Area.Max.X - MagGraphItem.GridSize.Y * 0.25f,
    //                                                          PrimaryOutputItem.Area.Min.Y + MagGraphItem.GridSize.Y * 0.5f);

    
    internal readonly List<MagGraphConnection> TempConnections = [];

    public ChangeSymbol.SymbolModificationResults DrawDialogs(ProjectView projectView)
    {
        EditCommentDialog.Draw(Selector);
        var results = ChangeSymbol.SymbolModificationResults.Nothing;
        
        if (projectView.CompositionInstance != null)
        {
            var compositionSymbol = projectView.InstView!.Symbol;
            if (projectView.CompositionInstance != projectView.RootInstance 
                && !compositionSymbol.SymbolPackage.IsReadOnly)
            {
                results |= AddInputDialog.Draw(compositionSymbol);
                results |= AddOutputDialog.Draw(compositionSymbol);
            }
            
            results |= DuplicateSymbolDialog.Draw(compositionSymbol.Id, 
                                                  projectView.NodeSelection.GetSelectedChildUis(), 
                                                  ref NameSpaceForDialogEdits,
                                                  ref SymbolNameForDialogEdits,
                                                  ref SymbolDescriptionForDialog);
            
            
            results |= CombineToSymbolDialog.Draw(compositionSymbol.Id, projectView,
                                                  ref NameSpaceForDialogEdits,
                                                  ref SymbolNameForDialogEdits,
                                                  ref SymbolDescriptionForDialog);
            
            results |= EditTourPointsPopup.Draw(compositionSymbol, projectView);
            
            RenameSymbolDialog.Draw(projectView.NodeSelection.GetSelectedChildUis(), 
                                             ref SymbolNameForDialogEdits);
            
            if(results != ChangeSymbol.SymbolModificationResults.Nothing)
                Layout.FlagStructureAsChanged();
        }

        return results;
    }
}