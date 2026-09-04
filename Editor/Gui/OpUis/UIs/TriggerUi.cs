#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

// ReSharper disable once UnusedType.Global
internal static class TriggerUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("E7C1F0AF-DA6D-4E33-AC86-7DC96BFE7EB3")]
        internal readonly InputSlot<bool> BoolValue = null!;

        [BindInput("FA14AC1D-3247-4D36-BC96-14FF7356720A")]
        internal readonly InputSlot<Vector4> ColorInGraph = null!;
        
        [BindOutput("2451ea62-9915-4ec1-a65e-4d44a3758fa8")]
        internal readonly Slot<bool> Result = null!;

    }

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect screenRect,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid)
            return OpUi.CustomUiResult.None;

        var dragWidth = WidgetElements.DrawOperatorDragHandle(screenRect, drawList, canvas.Scale);
        var colorAsVec4 = data.ColorInGraph.TypedInputValue.Value;
        var color = new Color(colorAsVec4);

        var activeRect = screenRect;
        activeRect.Min.X += dragWidth;

        ImGui.PushID(instance.SymbolChildId.GetHashCode());
        screenRect.Expand(-4);
        ImGui.SetCursorScreenPos(screenRect.Min);
        var symbolChild = instance.SymbolChild;
        ImGui.PushClipRect(screenRect.Min, screenRect.Max, true);

        var refValue = data.BoolValue.Value;
        var label = string.IsNullOrWhiteSpace(symbolChild.Name)
                        ? "Trigger"
                        : symbolChild.ReadableName;

        drawList.AddRectFilled(activeRect.Min, activeRect.Max, color.Fade(refValue ? 0.5f : 0.1f));
        var canvasScaleY = canvas.Scale.Y;

        var font = WidgetElements.GetPrimaryLabelFont(canvasScaleY);
        var labelColor = WidgetElements.GetPrimaryLabelColor(canvasScaleY);

        ImGui.PushFont(font);
        var labelSize = ImGui.CalcTextSize(label);

        var labelPos = activeRect.GetCenter() - labelSize / 2 - new Vector2(3 * canvasScaleY, 0);
        drawList.AddText(font, font.FontSize, labelPos, labelColor, label);
        ImGui.PopFont();

        if (!data.BoolValue.HasInputConnections)
        {
            var isHoveredOrActive = instance.SymbolChildId == _activeInputId ||
                                    ImGui.IsWindowHovered() && activeRect.Contains(ImGui.GetMousePos());
            if (isHoveredOrActive)
            {
                var wasChanged = false;
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    data.BoolValue.SetTypedInputValue(true);
                    _activeInputId = instance.SymbolChildId;
                    data.Result.DirtyFlag.Invalidate();
                    wasChanged = true;
                }
                else if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    _activeInputId = Guid.Empty;

                    data.BoolValue.SetTypedInputValue(false);
                    wasChanged = true;
                }

                if (wasChanged)
                {
                    data.Result.DirtyFlag.ForceInvalidate();
                }
            }
        }

        ImGui.PopClipRect();
        ImGui.PopID();
        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventTooltip
               | OpUi.CustomUiResult.PreventOpenParameterPopUp
               | OpUi.CustomUiResult.PreventInputLabels;
    }

    private static Guid _activeInputId;
        
}