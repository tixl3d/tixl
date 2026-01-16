#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.OpUis.UIs;

// ReSharper disable once UnusedType.Global
internal static class RemapUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("40606d4e-acaf-4f23-a845-16f0eb9b73cf")]
        internal readonly InputSlot<float> Value = null!;

        [BindInput("edb98f34-d019-47f6-b275-e5a80061e1f7")]
        internal readonly InputSlot<float> RangeInMin = null!;

        [BindInput("CD369755-5062-4934-8F37-E3A5CC9963DF")]
        internal readonly InputSlot<float> RangeInMax = null!;

        [BindInput("F2BAF278-ADDE-42DE-AFCE-336B6C8D0387")]
        internal readonly InputSlot<float> RangeOutMin = null!;

        [BindInput("252276FB-8DE1-42CC-BA41-07D6862015BD")]
        internal readonly InputSlot<float> RangeOutMax = null!;

        [BindInput("23548048-E373-4FD6-9C83-1CF7398F952D")]
        internal readonly InputSlot<Vector2> BiasAndGain = null!;

        [BindOutput("de6e6f65-cb51-49f1-bb90-34ed1ec963c1")]
        internal readonly Slot<float> Result = null!;
    }

    private const float GraphRangePadding = 0.06f;

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect screenRect,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid)
            return OpUi.CustomUiResult.None;

        screenRect.Expand(-2);
        var biasGraphRect = screenRect;

        var isActive = false;

        // Draw interaction
        ImGui.SetCursorScreenPos(biasGraphRect.Min);

        var value = data.Value.GetCurrentValue();
        var inMin = data.RangeInMin.GetCurrentValue();
        var inMax = data.RangeInMax.GetCurrentValue();
        var outMin = data.RangeOutMin.GetCurrentValue();
        var outMax = data.RangeOutMax.GetCurrentValue();

        var inFragment = Math.Abs(inMin - inMax) < 0.001f ? 0 : (value - inMin) / (inMax - inMin);
        var outFragment = Math.Abs(outMin - outMax) < 0.001f ? 0 : (data.Result.Value - outMin) / (outMax - outMin);

        drawList.PushClipRect(biasGraphRect.Min, biasGraphRect.Max, true);

        var canvasFade = canvas.Scale.X.RemapAndClamp(0.7f, 1.5f, 0, 1);

        ImGui.PushID(instance.SymbolChildId.GetHashCode());
        var isGraphActive = false;
        if (ImGui.GetIO().KeyCtrl)
        {
            ImGui.InvisibleButton("dragMicroGraph", biasGraphRect.GetSize());

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup)
                && ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsItemActive())
            {
                isGraphActive = true;
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            }
        }

        if (isGraphActive)
        {
            isActive = true;
            var dragDelta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 1);

            if (ImGui.IsItemActivated())
            {
                _biasAndGainStart = data.BiasAndGain.TypedInputValue.Value;
            }

            if (dragDelta.Length() > 0.001)
            {
                data.BiasAndGain.SetTypedInputValue(
                                                    new Vector2((_biasAndGainStart.X + dragDelta.X / 400f).Clamp(0.001f, 0.99f),
                                                                (_biasAndGainStart.Y - dragDelta.Y / 400f).Clamp(0.001f, 0.99f)));
            }
        }

        // Draw mapping graph...
        {
            const int steps = 35;
            var points = new Vector2[steps];
            var biasAndGain = data.BiasAndGain.GetCurrentValue();
            var p = new Vector2(MathUtils.Lerp(biasGraphRect.Min.X, biasGraphRect.Max.X, inFragment),
                                MathUtils.Lerp(biasGraphRect.Max.Y, biasGraphRect.Min.Y, outFragment));
            drawList.AddCircleFilled(p,
                                     3,
                                     UiColors.StatusAnimated);

            // Distribution...
            for (var i = 0; i < steps; i++)
            {
                var t = (float)i / (steps - 1);
                var f = t.ApplyGainAndBias(biasAndGain.X, biasAndGain.Y);
                var x = MathUtils.Lerp(biasGraphRect.Min.X, biasGraphRect.Max.X, f);
                var y = MathUtils.Lerp(biasGraphRect.Max.Y, biasGraphRect.Min.Y, f);
                drawList.AddLine(new Vector2(biasGraphRect.Min.X, y), new
                                     Vector2(biasGraphRect.Max.X, y),
                                 UiColors.BackgroundFull.Fade(0.2f * canvasFade), 1);

                points[i] = new Vector2(MathUtils.Lerp(biasGraphRect.Min.X, biasGraphRect.Max.X, t),
                                        MathUtils.Lerp(biasGraphRect.Min.Y, biasGraphRect.Max.Y, 1 - f));
            }

            drawList.AddLine(new Vector2(p.X, biasGraphRect.Min.Y),
                             new Vector2(p.X, biasGraphRect.Max.Y), UiColors.StatusAnimated.Fade(0.5f), 0.5f);

            drawList.AddLine(p, new Vector2(biasGraphRect.Max.X - 5, p.Y), UiColors.StatusAnimated.Fade(0.5f), 1);
            drawList.AddRectFilled(new Vector2(biasGraphRect.Max.X - 3, p.Y),
                                   biasGraphRect.Max, UiColors.StatusAnimated);

            drawList.AddPolyline(ref points[0], steps, UiColors.TextMuted, ImDrawFlags.None, 1);
            drawList.PopClipRect();
        }

        if (inFragment > 1)
        {
            drawList.AddCircleFilled(new Vector2(biasGraphRect.Max.X - 4, biasGraphRect.Max.Y - 4), 4, UiColors.StatusAnimated, 3);
        }

        if (inFragment < 0)
        {
            drawList.AddTriangleFilled(new Vector2(biasGraphRect.Min.X + 7, biasGraphRect.Max.Y - 9.5f),
                                       new Vector2(biasGraphRect.Min.X + 2, biasGraphRect.Max.Y - 6.5f),
                                       new Vector2(biasGraphRect.Min.X + 7, biasGraphRect.Max.Y - 3.5f),
                                       UiColors.StatusAnimated
                                      );
        }

        isActive |= ValueLabel.Draw(drawList, screenRect, new Vector2(GraphRangePadding / 2, 0), data.RangeInMax);
        isActive |= ValueLabel.Draw(drawList, screenRect, new Vector2(GraphRangePadding / 2, 1), data.RangeInMin);

        isActive |= ValueLabel.Draw(drawList, screenRect, new Vector2(1 - GraphRangePadding / 2, 0), data.RangeOutMax);
        isActive |= ValueLabel.Draw(drawList, screenRect, new Vector2(1 - GraphRangePadding / 2, 1), data.RangeOutMin);

        ImGui.PopID();
        
        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventTooltip
               | (isActive ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }

    private static Vector2 _biasAndGainStart = Vector2.One;
}