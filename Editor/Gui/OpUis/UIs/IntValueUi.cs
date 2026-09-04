#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class IntValueUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("4515C98E-05BC-4186-8773-4D2B31A8C323")]
        internal readonly InputSlot<int> Int = null!;
    }

    internal static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                    ImDrawListPtr drawList,
                                                    ImRect selectableScreenRect,
                                                    ScalableCanvas canvas,
                                                    ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid)
            return OpUi.CustomUiResult.None;

        var symbolChild = instance.SymbolChild;
        ImGui.PushClipRect(selectableScreenRect.Min, selectableScreenRect.Max, true);

        var h = selectableScreenRect.GetHeight();
        var font = h > 40
                       ? Fonts.FontLarge
                       : (h > 25
                              ? Fonts.FontNormal
                              : Fonts.FontSmall);

        ImGui.PushFont(font);
        ImGui.SetCursorScreenPos(selectableScreenRect.Min + new Vector2(10, 0));
        ImGui.BeginGroup();
        if (!string.IsNullOrWhiteSpace(symbolChild.Name))
        {
            ImGui.TextUnformatted(symbolChild.Name);
        }

        var isAnimated = instance.Parent?.Symbol.Animator.IsInputSlotAnimated(data.Int) ?? false;

        var value = (isAnimated || data.Int.HasInputConnections)
                        ? data.Int.Value
                        : data.Int.TypedInputValue.Value;

        ImGui.TextUnformatted($"{value:0}");
        ImGui.EndGroup();
        ImGui.PopFont();

        ImGui.PopClipRect();
        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip;
    }
}