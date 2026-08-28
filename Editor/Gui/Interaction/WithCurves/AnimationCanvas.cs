using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.TimeLine;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Interaction.WithCurves;

internal abstract class AnimationCanvas : ScalableCanvas, ITimeObjectManipulation
{
    protected AnimationCanvas()
    {
        // Start with time 0 well right of the left edge — the dope sheet's parameter headers
        // overlay that edge and would otherwise cover keyframes at the content start.
        ScrollTarget = new Vector2(-3f, 0.0f);
        ScaleTarget = new Vector2(80, -1);
    }

    public string ImGuiTitle = "timeline";

        
    protected void DrawAnimationCanvas(Action<InteractionState> drawAdditionalCanvasContent, SelectionFence? selectionFence, float height = 0, T3Ui.EditingFlags flags = T3Ui.EditingFlags.None, bool drawVSnapIndicator = true)
    {

        ImGui.BeginChild(ImGuiTitle, new Vector2(0, height), ImGuiChildFlags.Borders,
                         ImGuiWindowFlags.NoScrollbar |
                         ImGuiWindowFlags.NoMove |
                         ImGuiWindowFlags.NoScrollWithMouse |
                         ImGuiWindowFlags.NoBackground);
        {
            Drawlist = ImGui.GetWindowDrawList();
            UpdateCanvas(out var interactionState, flags);

            // Outer fence is optional — subclasses that handle fence selection per-pane
            // (e.g. timeline in inline-curve-edit mode) pass null to suppress it here.
            if (selectionFence != null && !T3Ui.IsAnyPopupOpen)
            {
                HandleFenceUpdate(selectionFence, out _);
            }
            drawAdditionalCanvasContent(interactionState);

            SnapHandlerForU.DrawSnapIndicator(this);
            // V snap is opt-out: in layouts where V is owned by a sub-canvas with different
            // Y state (e.g. timeline's inline curve pane) the sub-canvas renders it itself,
            // and drawing here would use the outer canvas's Y → wrong screen position.
            if (drawVSnapIndicator)
                SnapHandlerForV.DrawSnapIndicator(this);
        }
        ImGui.EndChild();
    }

    private void HandleFenceUpdate(SelectionFence selectionFence, out SelectionFence.SelectModes selectMode)
    {
        switch (selectionFence.UpdateAndDraw(out selectMode))
        {
            case SelectionFence.States.Updated:
            case SelectionFence.States.CompletedAsClick:
                UpdateSelectionForArea(selectionFence.BoundsInScreen, selectMode);
                break;
        }
    }


    protected void HandleCreateNewKeyframes(Curve curve)
    {
        var hoverNewKeyframe = !ImGui.IsAnyItemActive()
                               && ImGui.IsWindowHovered()
                               && ImGui.GetIO().KeyAlt
                               && ImGui.IsWindowHovered();
        if (!hoverNewKeyframe)
            return;
            
        var hoverTime = InverseTransformX(ImGui.GetIO().MousePos.X);
        if (SnapHandlerForU.TryCheckForSnapping(hoverTime, out var snappedValue, Scale.X))
        {
            hoverTime = (float)snappedValue;
        }

        if (ImGui.IsMouseReleased(0))
        {
            var dragDistance = ImGui.GetIO().MouseDragMaxDistanceAbs[0].Length();
            if (dragDistance < 2)
            {
                ClearSelection();

                InsertNewKeyframe(curve, hoverTime);
            }
        }
        else
        {
            var sampledValue = (float)curve.GetSampledValue(hoverTime);
            var posOnCanvas = new Vector2(hoverTime, sampledValue);
            var iconSize = Icons.FontSize;
            var posOnScreen = TransformPosition(posOnCanvas)
                              - new Vector2(iconSize/2  );
            
            Icons.DrawIconAtScreenPosition(Icon.CurveKeyframe, posOnScreen);
            var drawlist = ImGui.GetWindowDrawList();
            drawlist.AddText(posOnScreen + Vector2.One*20, UiColors.Gray, $"Insert at\n{hoverTime:0.00}  {sampledValue:0.00}");
        }

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
    }
    
    private static void InsertNewKeyframe(Curve curve, float u)
    {
        var value = curve.GetSampledValue(u);

        var newKey = curve.TryGetPreviousKey(u, out var previousKey) 
                         ? previousKey.Clone() 
                         : new VDefinition();

        newKey.Value = value;
        newKey.U = u;
        curve.AddOrUpdateV(u, newKey);
    }

    #region implement ITimeObjectManipulation to forward interaction to children
    public void ClearSelection()
    {
        foreach (var sh in TimeObjectManipulators)
        {
            sh.ClearSelection();
        }
    }

    public void UpdateSelectionForArea(ImRect screenArea, SelectionFence.SelectModes selectMode)
    {
        // When several manipulators share the same keyframe selection set (e.g. DopeSheetArea
        // and TimelineCurveEditor in the inline-pane layout), each one's own
        // "clear on Replace" wipes out the previous manipulator's additions, so only the last
        // dispatched editor's hits survive. Clear once up front, then dispatch as Add.
        // Only manipulators the fence actually concerns are cleared/updated — the clip lanes
        // opt out when the fence is entirely over the keyframe rows (see ParticipatesInFence).
        if (selectMode == SelectionFence.SelectModes.Replace)
        {
            foreach (var sh in TimeObjectManipulators)
            {
                if (sh.ParticipatesInFence(screenArea))
                    sh.ClearSelection();
            }

            selectMode = SelectionFence.SelectModes.Add;
        }

        foreach (var sh in TimeObjectManipulators)
        {
            if (sh.ParticipatesInFence(screenArea))
                sh.UpdateSelectionForArea(screenArea, selectMode);
        }
    }

    private MacroCommand _macro;
    private readonly List<ICommand> _commands = new();
        
    public ICommand StartDragCommand(in Guid compositionSymbolId)
    {
        _commands.Clear();
        foreach (var manipulators in TimeObjectManipulators)
        {
            _commands.Add(manipulators.StartDragCommand(compositionSymbolId));
        }
        _macro = new MacroCommand("Move Timeline Items", _commands);
        return null;
    }

    public void UpdateDragCommand(double dt, double dv)
    {
        foreach (var manipulators in TimeObjectManipulators)
        {
            manipulators.UpdateDragCommand(dt, dv);
        }
    }

    public void UpdateDragAtStartPointCommand(double dt, double dv)
    {
        foreach (var manipulators in TimeObjectManipulators)
        {
            manipulators.UpdateDragAtStartPointCommand(dt, dv);
        }
    }

    public void UpdateDragAtEndPointCommand(double dt, double dv)
    {
        foreach (var manipulators in TimeObjectManipulators)
        {
            manipulators.UpdateDragAtEndPointCommand(dt, dv);
        }
    }

    public void UpdateDragStretchCommand(double scaleU, double scaleV, double originU, double originV)
    {
        foreach (var manipulators in TimeObjectManipulators)
        {
            manipulators.UpdateDragStretchCommand(scaleU, scaleV, originU, originV);
        }
    }

    public TimeRange GetSelectionTimeRange()
    {
        var timeRange = new TimeRange(float.PositiveInfinity, float.NegativeInfinity);

        foreach (var sh in TimeObjectManipulators)
        {
            timeRange.Unite(sh.GetSelectionTimeRange());
        }

        return timeRange;
    }

    public void CompleteDragCommand()
    {
        foreach (var manipulators in TimeObjectManipulators)
        {
            manipulators.CompleteDragCommand();
        }

        if (_macro == null)
        {
            Log.Warning("Can't complete no valid valid drag command?");
            return;
        }
        UndoRedoStack.AddAndExecute(_macro);
    }

    public void DeleteSelectedElements(Instance composition)
    {
        foreach (var s in TimeObjectManipulators)
        {
            s.DeleteSelectedElements(composition);
        }
    }

    protected readonly List<ITimeObjectManipulation> TimeObjectManipulators = new();
    #endregion
        
    public readonly ValueSnapHandler SnapHandlerForU = new(SnapResult.Orientations.Horizontal);
    public readonly ValueSnapHandler SnapHandlerForV = new(SnapResult.Orientations.Vertical);
    protected ImDrawListPtr Drawlist;
    protected override ScalableCanvas Parent => null;
}