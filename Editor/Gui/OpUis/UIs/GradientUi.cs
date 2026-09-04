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

internal static class GradientUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("b129b4d8-7c1b-47e4-b156-b24d6632b13a")]
        internal readonly InputSlot<Gradient> Gradient = null!;
        

        [BindOutput("631db643-e8af-44c5-be36-2e7c4c818015")]
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

        if (!string.IsNullOrEmpty(instance.SymbolChild.Name))
        {
            WidgetElements.DrawPrimaryTitle(drawList, innerRect, instance.SymbolChild.Name, canvas.Scale, true);
        }
        
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