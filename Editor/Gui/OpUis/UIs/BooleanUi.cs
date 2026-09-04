#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

// ReSharper disable once UnusedType.Global
internal static class BooleanUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("E7C1F0AF-DA6D-4E33-AC86-7DC96BFE7EB3")]
        internal readonly InputSlot<bool> BoolValue = null!;

        [BindInput("49b3fa80-4027-4297-82cd-10a36882ed9d")]
        internal readonly InputSlot<Vector4> ColorInGraph = null!;
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
        var label = string.IsNullOrEmpty(symbolChild.Name)
                        ? (refValue ? "True" : "False")
                        : symbolChild.ReadableName;

        drawList.AddRectFilled(activeRect.Min, activeRect.Max, color.Fade(refValue ? 0.5f : 0.1f));

        var canvasScaleY = canvas.Scale.Y;
        var font = WidgetElements.GetPrimaryLabelFont(canvasScaleY);
        var labelColor = WidgetElements.GetPrimaryLabelColor(canvasScaleY);

        ImGui.PushFont(font);
        var labelSize = ImGui.CalcTextSize(label);

        var labelPos = new Vector2(activeRect.Min.X + 18 * canvasScaleY,
                                   (activeRect.Min.Y + activeRect.Max.Y) / 2 - labelSize.Y / 2);
        drawList.AddText(font, font.FontSize, labelPos, labelColor, label);
        ImGui.PopFont();

        var checkCenter = new Vector2(labelPos.X - 10f * canvasScaleY,
                                      (activeRect.Min.Y + activeRect.Max.Y) / 2 + 1.5f * canvasScaleY
                                     );
        var checkSize = MathF.Min(100, 2.5f * canvasScaleY);
        var points = new[]
                         {
                             checkCenter + new Vector2(-2, -1) * checkSize,
                             checkCenter + new Vector2(0, 1) * checkSize,
                             checkCenter + new Vector2(3, -2) * checkSize,
                         };
        drawList.AddPolyline(ref points[0], 3,
                             refValue ? UiColors.WidgetTitle : UiColors.BackgroundFull.Fade(0.2f),
                             ImDrawFlags.None,
                             MathF.Max(1.4f, 0.5f * canvasScaleY));

        if (!data.BoolValue.HasInputConnections)
        {
            var isHoveredOrActive = instance.SymbolChildId == _activeInputId ||
                                    ImGui.IsWindowHovered() && activeRect.Contains(ImGui.GetMousePos());
            if (isHoveredOrActive)
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _activeInputId = instance.SymbolChildId;
                }
                else if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && ImGui.GetMouseDragDelta().LengthSquared() < UserSettings.Config.ClickThreshold)
                {
                    _activeInputId = Guid.Empty;
                    var newValue = !data.BoolValue.TypedInputValue.Value;
                    data.BoolValue.SetTypedInputValue(newValue);
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