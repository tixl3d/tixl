#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class SetStringVarUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("5be2939b-27f5-4af8-b78e-3ae220d6b1a4")]
        internal readonly InputSlot<string> VariableName = null!;

        [BindInput("64453e52-84eb-487a-b69c-6c5c793dc057")]
        internal readonly InputSlot<string> Value = null!;
    }

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect area,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid || instance.Parent == null)
            return OpUi.CustomUiResult.PreventOpenSubGraph;

        // Draw reference lines on hover
        if (area.Contains(ImGui.GetMousePos()))
        {
            OpUi.DrawVariableReferences(drawList, canvas, area.GetCenter(), instance, data.VariableName.Value, 
                                        Guid.Parse("15012739-97b0-49e6-8bdc-cd45cd262560"), 
                                        Guid.Parse("d19b0483-cdd4-4684-852d-ef5ee8393dfc"));
        }
        
        drawList.PushClipRect(area.Min, area.Max, true);
        
        var value = data.Value.TypedInputValue.Value;

        var symbolChild = instance.SymbolChild;
        if (!string.IsNullOrWhiteSpace(symbolChild.Name))
        {
            WidgetElements.DrawPrimaryTitle(drawList, area, symbolChild.Name, canvas.Scale);
        }
        else
        {
            WidgetElements.DrawPrimaryTitle(drawList, area, "Set string: " + data.VariableName.TypedInputValue.Value, canvas.Scale);
        }

        WidgetElements.DrawSmallValue(drawList, area, value, canvas.Scale);

        drawList.PopClipRect();
        return OpUi.CustomUiResult.Rendered | OpUi.CustomUiResult.PreventInputLabels | OpUi.CustomUiResult.PreventOpenSubGraph;
    }
}