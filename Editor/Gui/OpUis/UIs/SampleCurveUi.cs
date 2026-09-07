#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.InputUi.CombinedInputs;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class SampleCurveUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }
        
        [BindInput("108CB829-5F9E-4A45-BC6B-7CF40A0A0F89")]
        internal readonly InputSlot<Curve> Curve = null!;

        [BindInput("2c24d4fe-6c96-4502-bf76-dac756a16215")]
        internal readonly InputSlot<float> U = null!;

        [BindOutput("1A20C791-A1FE-4A14-A9CF-615691948F2D")]
        internal readonly Slot<Curve> OutCurve = null!;

        [BindOutput("fc51bee8-091c-4c66-a7df-12f6f69e3783")]
        internal readonly Slot<float> Result = null!;
    }

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect selectableScreenRect,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid || instance.Parent == null)
            return OpUi.CustomUiResult.None;

        var dragWidth = WidgetElements.DrawOperatorDragHandle(selectableScreenRect, drawList, canvas.Scale);
        var innerRect = selectableScreenRect;
        innerRect.Min.X += dragWidth;
        innerRect.Min.Y += 1;

        if (innerRect.GetHeight() < 0)
            return OpUi.CustomUiResult.PreventTooltip
                   | OpUi.CustomUiResult.PreventOpenSubGraph
                   | OpUi.CustomUiResult.PreventInputLabels
                   | OpUi.CustomUiResult.PreventOpenParameterPopUp;

        var curve = (data.Curve.HasInputConnections)
                        ? data.Curve.Value
                        : data.Curve.TypedInputValue.Value;

        //var curve = data.Curve.Value;
        if (curve == null)
        {
            //Log.Warning("Can't draw undefined gradient");
            return OpUi.CustomUiResult.PreventTooltip
                   | OpUi.CustomUiResult.PreventOpenSubGraph
                   | OpUi.CustomUiResult.PreventInputLabels
                   | OpUi.CustomUiResult.PreventOpenParameterPopUp;
        }

        ImGui.PushClipRect(innerRect.Min, innerRect.Max, true);
        ImGui.SetCursorScreenPos(innerRect.Min);
        ImGui.BeginChild("curve" + instance.SymbolChildId.GetHashCode(), innerRect.GetSize(), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
        {
            var cloneIfModified = data.Curve.Input.IsDefault;

            var preventEditingUnlessCtrlPressed = ImGui.GetIO().KeyCtrl
                                                      ? T3Ui.EditingFlags.None
                                                      : T3Ui.EditingFlags.PreventMouseInteractions;

            var keepPositionForIcon = ImGui.GetCursorPos() + Vector2.One;
            var modified2 = CurveInputEditing.DrawCanvasForCurve(ref curve,
                                                                 data.Curve.Input,
                                                                 cloneIfModified,
                                                                 instance.Parent, T3Ui.EditingFlags.ExpandVertically
                                                                                  | preventEditingUnlessCtrlPressed
                                                                                  | T3Ui.EditingFlags.PreventZoomWithMouseWheel);

            var showPopupIcon = innerRect.GetHeight() > ImGui.GetFrameHeight() * T3Ui.UiScaleFactor * 2;
            if (showPopupIcon &&
                CurveEditPopup.DrawPopupIndicator(instance.Parent, data.Curve.Input, ref curve, keepPositionForIcon, cloneIfModified, out var popupResult))
            {
                modified2 = popupResult;
            }

            if ((modified2 & InputEditStateFlags.Modified) != InputEditStateFlags.Nothing)
            {
                if (cloneIfModified)
                {
                    data.Curve.SetTypedInputValue(curve);
                }

                data.Result.DirtyFlag.Invalidate();
                data.OutCurve.DirtyFlag.ForceInvalidate();
            }

            DrawSamplePointIndicator();
            ImGui.Dummy(Vector2.One); // ImGui 1.91 sentinel: clears any dangling SetCursorPos from helpers
        }
        ImGui.EndChild();
        ImGui.PopClipRect();

        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventTooltip
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventOpenParameterPopUp;

        void DrawSamplePointIndicator()
        {
            ScalableCanvas? scalableCanvas = null; //CurveInputEditing.GetCanvasForCurve(curve);
            if (scalableCanvas == null)
                return;

            var x = scalableCanvas.TransformPosition(new Vector2(data.U.Value, 0)).X;
            if (!(x >= innerRect.Min.X) || !(x < innerRect.Max.X))
                return;

            var pMin = new Vector2(x, innerRect.Min.Y);
            var pMax = new Vector2(x + 1, innerRect.Max.Y);
            drawList.AddRectFilled(pMin, pMax, UiColors.StatusAnimated);
        }
    }
}