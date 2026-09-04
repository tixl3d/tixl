using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class GradientSliderUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("EFF10FAD-CF95-4133-91DB-EFC41258CD1B")]
        internal readonly InputSlot<Gradient> Gradient = null!;

        [BindInput("a4527e01-f19a-4200-85e5-00144f3ce061")]
        internal readonly InputSlot<float> SamplePos = null!;

        [BindOutput("963611E7-F55E-4C94-96E6-34E195558A2B")]
        internal readonly Slot<Vector4> OutColor = null!;

        [BindOutput("9F3D0701-86E8-436E-8652-918BA23B2CEF")]
        internal readonly Slot<Gradient> OutGradient = null!;
    }

    private static ChangeInputValueCommand _inputValueCommandInFlight;
    private static object _inputSlotForActiveCommand;

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect screenRect,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid)
            return OpUi.CustomUiResult.None;

        var gradient = data.Gradient.HasInputConnections
                           ? data.Gradient.Value
                           : data.Gradient.TypedInputValue.Value;

        if (gradient == null || instance.Parent == null)
            return OpUi.CustomUiResult.None;

        var innerRect = screenRect;

        var dragHandleWidth = WidgetElements.DrawOperatorDragHandle(screenRect, drawList, canvas.Scale);
        innerRect.Min.X += dragHandleWidth;

        var cloneIfModified = data.Gradient.Input.IsDefault;
        ImGui.PushID(instance.SymbolChildId.GetHashCode());
        var editState = GradientEditor.Draw(ref gradient, drawList, innerRect, cloneIfModified);
        var inputSlot = data.Gradient;

        if (editState.HasFlag(InputEditStateFlags.Started))
        {
            _inputSlotForActiveCommand = inputSlot;
            _inputValueCommandInFlight =
                new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, inputSlot.Input, inputSlot.Input.Value, instance);
        }

        if (editState.HasFlag(InputEditStateFlags.Modified))
        {
            if (cloneIfModified)
            {
                data.Gradient.SetTypedInputValue(gradient);
            }

            data.OutColor.DirtyFlag.Invalidate();
            data.OutGradient.DirtyFlag.Invalidate();

            if (_inputValueCommandInFlight == null || _inputSlotForActiveCommand != inputSlot)
            {
                _inputValueCommandInFlight =
                    new ChangeInputValueCommand(instance.Parent.Symbol, instance.SymbolChildId, inputSlot.Input, inputSlot.Input.Value, instance);
                _inputSlotForActiveCommand = inputSlot;
            }

            _inputValueCommandInFlight.AssignNewValue(inputSlot.Input.Value);
            inputSlot.DirtyFlag.Invalidate();
        }

        if (editState.HasFlag(InputEditStateFlags.Finished))
        {
            if (_inputValueCommandInFlight != null && _inputSlotForActiveCommand == inputSlot)
            {
                UndoRedoStack.Add(_inputValueCommandInFlight);
            }

            _inputValueCommandInFlight = null;
        }

        var x = data.SamplePos.Value.Clamp(0, 1) * innerRect.GetWidth();
        var pMin = new Vector2(innerRect.Min.X + x, innerRect.Min.Y);
        var pMax = new Vector2(innerRect.Min.X + x + 2, innerRect.Max.Y);
        drawList.AddRectFilled(pMin, pMax, UiColors.StatusAnimated);
        ImGui.PopID();

        const OpUi.CustomUiResult defaultHandlingForInteractiveOps = OpUi.CustomUiResult.Rendered
                                                                     | OpUi.CustomUiResult.PreventInputLabels
                                                                     | OpUi.CustomUiResult.PreventOpenSubGraph
                                                                     | OpUi.CustomUiResult.PreventTooltip
                                                                     | OpUi.CustomUiResult.PreventOpenParameterPopUp;

        if (editState == InputEditStateFlags.Nothing)
            return defaultHandlingForInteractiveOps;

        return defaultHandlingForInteractiveOps | OpUi.CustomUiResult.IsActive;
    }
}