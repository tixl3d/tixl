using System.Reflection;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class _JitterUi
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
        private readonly PropertyInfo _fragmentProp = null!;

        internal float Fragment => (float)(_fragmentProp?.GetValue(_instance) ?? 0);

        [BindInput("C7BBB096-DD90-4B06-9C16-36521347147C")]
        internal readonly InputSlot<float> Rate = null!;

        [BindInput("74D3963F-3563-4CC7-86B9-DCE93B5FE5AB")]
        internal readonly InputSlot<float> JumpDistance = null!;

        [BindInput("57B0BB27-19FD-4D0C-96DE-9D5B320FDE98")]
        internal readonly InputSlot<float> Blending = null!;
    }

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect screenRect,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid)
            return OpUi.CustomUiResult.None;

        if (WidgetElements.DrawRateLabelWithTitle(data.Rate, screenRect, drawList, "Jitter", canvas.Scale))
        {
            data.Rate.Input.IsDefault = false;
            data.Rate.DirtyFlag.Invalidate();
        }

        var label = $"±{data.JumpDistance.TypedInputValue.Value:0.0}";

        if (MicroGraph.Draw(ref data.JumpDistance.TypedInputValue.Value,
                            ref data.Blending.TypedInputValue.Value,
                            data.Fragment,
                            screenRect, drawList, label))
        {
            data.Blending.Input.IsDefault = false;
            data.Blending.DirtyFlag.Invalidate();
            data.JumpDistance.Input.IsDefault = false;
            data.JumpDistance.DirtyFlag.Invalidate();
        }

        return OpUi.CustomUiResult.Rendered | OpUi.CustomUiResult.PreventInputLabels;
    }
}