#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class SetIntVarUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("bfd87742-aaf5-4fa8-b714-fd275de1c60d")]
        internal readonly InputSlot<string> VariableName = null!;

        [BindInput("72DD0C80-8E95-474B-9AA5-D8292D0FF0DD")]
        internal readonly InputSlot<int> Value = null!;
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
                                        Guid.Parse("470db771-c7f2-4c52-8897-d3a9b9fc6a4e"), 
                                        Guid.Parse("d7662b65-f249-4887-a319-dc2cf7d192f2"));
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
            WidgetElements.DrawPrimaryTitle(drawList, area, "Set int: " + data.VariableName.TypedInputValue.Value, canvas.Scale);
        }

        WidgetElements.DrawSmallValue(drawList, area, $"{value:0}", canvas.Scale);

        drawList.PopClipRect();
        return OpUi.CustomUiResult.Rendered | OpUi.CustomUiResult.PreventInputLabels | OpUi.CustomUiResult.PreventOpenSubGraph;
    }
}