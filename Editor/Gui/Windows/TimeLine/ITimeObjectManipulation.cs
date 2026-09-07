using T3.Core.Animation;
using T3.Core.Operator;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Interface common to Timeline components that can hold a selection and manipulate
/// a selection (like <see cref="Animator.Clip"/>, keyframes, etc).
/// </summary>
public interface ITimeObjectManipulation
{
    void ClearSelection();

    /// <summary>
    /// Whether a selection fence over <paramref name="area"/> concerns this manipulator at all.
    /// A Replace-fence clears the selection of every *participating* manipulator before selecting —
    /// opting out lets e.g. the clip lanes keep their selection while keyframe rows are fenced below
    /// them (clearing the clip selection would deselect the op whose rows are being fenced).
    /// </summary>
    bool ParticipatesInFence(ImRect area) => true;

    void UpdateSelectionForArea(ImRect area, SelectionFence.SelectModes selectMode);
    void DeleteSelectedElements(Instance compositionOp);

    ICommand StartDragCommand(in Guid symbolId);
    void UpdateDragCommand(double dt, double dv);
    void UpdateDragStretchCommand(double scaleU, double scaleV, double originU, double originV);
    void CompleteDragCommand();
        
    void UpdateDragAtStartPointCommand(double dt, double dv);
    void UpdateDragAtEndPointCommand(double dt, double dv);

    TimeRange GetSelectionTimeRange();
}