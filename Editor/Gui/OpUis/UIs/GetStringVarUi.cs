#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class GetStringVarUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("d19b0483-cdd4-4684-852d-ef5ee8393dfc")]
        internal readonly InputSlot<string> VariableName = null!;

        [BindOutput("1a3ed40a-655b-4604-868d-1f037f7306c7")]
        internal readonly Slot<string> Result = null!;
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
                                        Guid.Parse("9c9274c1-3f85-4f57-9eb5-1e0a0b1cce00"),
                                        Guid.Parse("5be2939b-27f5-4af8-b78e-3ae220d6b1a4"));
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
            WidgetElements.DrawPrimaryTitle(drawList, area, "Get string: " + data.VariableName.TypedInputValue.Value, canvas.Scale);
        }

        WidgetElements.DrawSmallValue(drawList, area, value, canvas.Scale);

        drawList.PopClipRect();
        return OpUi.CustomUiResult.Rendered | OpUi.CustomUiResult.PreventInputLabels | OpUi.CustomUiResult.PreventOpenSubGraph;
    }
}