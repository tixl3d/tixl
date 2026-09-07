using System.Reflection;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class Jitter2dUi
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

        [BindInput("1DF95BEB-DA6D-4263-8273-7A180FD190F5")]
        internal readonly InputSlot<float> Rate = null!;

        [BindInput("F101AF0C-DE31-4AFB-ACB4-8166C62C2EC8")]
        internal readonly InputSlot<float> JumpDistance = null!;

        [BindInput("38086D8A-15E0-4F3E-B161-A46A79FC5CC3")]
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

        if (WidgetElements.DrawRateLabelWithTitle(data.Rate, screenRect, drawList, "Jitter2D", canvas.Scale))
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