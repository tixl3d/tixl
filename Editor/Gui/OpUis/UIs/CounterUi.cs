using System.Reflection;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class CounterUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
            _instance = instance;
        }
        
        private readonly Instance _instance;
        
        [BindProperty("Fragment")]
        private readonly PropertyInfo _fragmentField = null!;
        internal float Fragment => (float)(_fragmentField?.GetValue(_instance) ?? 0);

        [BindInput("286CBBFB-796D-499F-93D3-D467512110BE")]
        internal readonly InputSlot<float> Rate = null!;
        
        [BindInput("BCA3F7B2-A093-4CB3-89A5-0E2681760607")]
        internal readonly InputSlot<float> Increment = null!;
        
        [BindInput("B04D475B-A898-421B-BF26-AE5CF982A351")]
        internal readonly InputSlot<float> Blending = null!;
        
        [BindInput("73B493CB-91D1-4D4F-B9A8-005017ECAC8F")]
        internal readonly InputSlot<float> Modulo = null!;
    }

    internal static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                    ImDrawListPtr drawList,
                                                    ImRect screenRect,
                                                    ScalableCanvas canvas,
                                                    ref OpUiBinding data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid)
            return OpUi.CustomUiResult.None;

        ImGui.PushID(instance.SymbolChildId.GetHashCode());
        var isEditActive = false;

        if (WidgetElements.DrawRateLabelWithTitle(data.Rate, screenRect, drawList, "Counter", canvas.Scale))
        {
            isEditActive = true;
            data.Rate.Input.IsDefault = false;
            data.Rate.DirtyFlag.Invalidate();
        }

        var inc = data.Increment.Value;
        var label = (inc < 0 ? "-" : "+") + $"{inc:0.0}";
        if (data.Modulo.Value > 0)
        {
            label += $" % {data.Modulo.Value:0}";
        }

        if (MicroGraph.Draw(ref data.Increment.TypedInputValue.Value,
                            ref data.Blending.TypedInputValue.Value,
                            data.Fragment,
                            screenRect, drawList, label))
        {
            isEditActive = true;
            data.Blending.Input.IsDefault = false;
            data.Blending.DirtyFlag.Invalidate();

            data.Increment.Input.IsDefault = false;
            data.Increment.DirtyFlag.Invalidate();
        }

        ImGui.PopID();
        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip
               | (isEditActive ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }
}