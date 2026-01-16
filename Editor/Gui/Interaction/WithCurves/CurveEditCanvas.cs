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

internal abstract class CurveEditCanvas : ScalableCanvas, ITimeObjectManipulation
{
    protected CurveEditCanvas()
    {
        ScrollTarget = new Vector2(-2.5f, 0.0f);
        ScaleTarget = new Vector2(80, -1);
    }

    public string ImGuiTitle = "timeline";

        
    protected void DrawCurveCanvas(Action<InteractionState> drawAdditionalCanvasContent, SelectionFence selectionFence, float height = 0, T3Ui.EditingFlags flags = T3Ui.EditingFlags.None)
    {

        ImGui.BeginChild(ImGuiTitle, new Vector2(0, height), true,
                         ImGuiWindowFlags.NoScrollbar | 
                         ImGuiWindowFlags.NoMove | 
                         ImGuiWindowFlags.NoScrollWithMouse |
                         ImGuiWindowFlags.NoBackground);
        {
            Drawlist = ImGui.GetWindowDrawList();
            UpdateCanvas(out var interactionState, flags);
            Drawlist = ImGui.GetWindowDrawList();

            if (!T3Ui.IsAnyPopupOpen)
            {
                HandleFenceUpdate(selectionFence, out _);
            }
            drawAdditionalCanvasContent(interactionState);

            SnapHandlerForU.DrawSnapIndicator(this);
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
            //Icons.DrawIconOnLastItem(Icon.Cu);
            var drawlist = ImGui.GetWindowDrawList();
            drawlist.AddText(posOnScreen + Vector2.One*20, UiColors.Gray, $"Insert at\n{hoverTime:0.00}  {sampledValue:0.00}");
        }

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
    }

    private const float KeyframeIconWidth = 16;

    private void InsertNewKeyframe(Curve curve, float u)
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
        foreach (var sh in TimeObjectManipulators)
        {
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