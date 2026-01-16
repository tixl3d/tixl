#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class SetFloatVarUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("6EE64D39-855A-4B20-A8F5-39B4F98E8036")]
        internal readonly InputSlot<string> VariableName = null!;

        [BindInput("68E31EAA-1481-48F4-B742-5177A241FE6D")]
        internal readonly InputSlot<float> Value = null!;
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
                                        Guid.Parse("e6072ecf-30d2-4c52-afa1-3b195d61617b"), 
                                        Guid.Parse("015d1ea0-ea51-4038-893a-4af2f8584631"));
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
            WidgetElements.DrawPrimaryTitle(drawList, area, "Set float: " + data.VariableName.TypedInputValue.Value, canvas.Scale);
        }

        WidgetElements.DrawSmallValue(drawList, area, $"{value:0.000}", canvas.Scale);

        drawList.PopClipRect();
        return OpUi.CustomUiResult.Rendered | OpUi.CustomUiResult.PreventInputLabels | OpUi.CustomUiResult.PreventOpenSubGraph;
    }
}