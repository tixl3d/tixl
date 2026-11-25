using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.OutputUi;

public interface IOutputUi : ISelectableCanvasObject
{
    Symbol.OutputDefinition OutputDefinition { get; set; }
    Type Type { get; }

    /// <summary>
    /// What the output window treats the drawn value as for camera and view handling. Differs from
    /// <see cref="Type"/> when a value is shown as a rendering, like a point buffer drawn as points.
    /// </summary>
    Type PresentedType { get; }
    IOutputUi Clone();
    void DrawValue(ISlot slot, EvaluationContext context, string viewId, bool recompute = true);
}