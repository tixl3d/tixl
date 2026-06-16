#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class GetVec3VarUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("d8a9d923-232f-4cd4-9e24-fadbf40fe1d1")]
        internal readonly InputSlot<string> VariableName = null!;

        [BindOutput("F26C6DFE-AFC5-4824-9580-92FF5CD8F086")]
        internal readonly Slot<Vector3> Result = null!;
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
                                        Guid.Parse("fdad077d-e919-4f40-a154-36e86245a585"),
                                        Guid.Parse("0edf7837-4555-4e62-902f-930abf72e8b8"));
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
            WidgetElements.DrawPrimaryTitle(drawList, area, "Get vec3: " + data.VariableName.TypedInputValue.Value, canvas.Scale);
        }

        WidgetElements.DrawSmallValue(drawList, area, $"{value}", canvas.Scale);

        drawList.PopClipRect();
        return OpUi.CustomUiResult.Rendered | OpUi.CustomUiResult.PreventInputLabels | OpUi.CustomUiResult.PreventOpenSubGraph;
    }
}