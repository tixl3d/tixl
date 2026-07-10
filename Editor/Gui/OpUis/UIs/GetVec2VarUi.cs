#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class GetVec2VarUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("05d0cc68-4f6b-4e03-871c-4de0e6ea0b24")]
        internal readonly InputSlot<string> VariableName = null!;

        [BindOutput("2231668d-dc24-4966-8582-632bfd771899")]
        internal readonly Slot<Vector2> Result = null!;
    }

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect area,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid)
            return OpUi.CustomUiResult.PreventOpenSubGraph;

        // Draw reference lines on hover — links to the matching Set op (mirror of the Set side).
        if (area.Contains(ImGui.GetMousePos()))
        {
            OpUi.DrawVariableReferences(drawList, canvas, area.GetCenter(), instance, data.VariableName.Value,
                                        Guid.Parse("332d7496-09ec-45b7-85b4-0653bb93dd62"),
                                        Guid.Parse("903c7fc3-dfa4-4a01-8f7d-53a94f8a93ab"));
        }

        drawList.PushClipRect(area.Min, area.Max, true);

        var value = data.Result.Value;

        var name = instance.SymbolChild.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            WidgetElements.DrawPrimaryTitle(drawList, area, name, canvas.Scale);
        }
        else
        {
            WidgetElements.DrawPrimaryTitle(drawList, area, "Get vec2: " + data.VariableName.TypedInputValue.Value, canvas.Scale);
        }

        WidgetElements.DrawSmallValue(drawList, area, $"{value}", canvas.Scale);

        drawList.PopClipRect();
        return OpUi.CustomUiResult.Rendered | OpUi.CustomUiResult.PreventInputLabels | OpUi.CustomUiResult.PreventOpenSubGraph;
    }
}
