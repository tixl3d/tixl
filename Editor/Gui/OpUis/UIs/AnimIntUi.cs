#nullable enable
using System.Reflection;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

// ReSharper disable once UnusedType.Global
internal static class AnimIntUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
            _instance = instance;
        }

        private readonly Instance _instance;

        [BindField("_normalizedTime")]
        private readonly FieldInfo? _normalizedTimeField = null!;

        internal double NormalizedTime => (double)(_normalizedTimeField?.GetValue(_instance) ?? 0);

        [BindInput("1bc7d002-1483-48bd-b419-cccfcb38aa2f")]
        internal readonly InputSlot<float> Rate = null!;

        [BindInput("0D0A2C33-F3BB-4035-8F8D-D01524952B2E")]
        internal readonly InputSlot<int> Modulo = null!;
    }

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

        var isNodeActivated = false;
        ImGui.PushID(instance.SymbolChildId.GetHashCode());
        if (WidgetElements.DrawRateLabelWithTitle(data.Rate,
                                                  screenRect,
                                                  drawList,
                                                  "Anim Int", canvas.Scale))
        {
            isNodeActivated = true;
        }

        // Graph dragging to edit Bias and Ratio
        var h = screenRect.GetHeight();
        var graphRect = screenRect;

        var modulo = data.Modulo.GetCurrentValue();
        var usingModulo = modulo != 0;

        const float relativeGraphWidth = 0.6f;

        graphRect.Expand(-3);
        var min = graphRect.Min;
        var max = graphRect.Max;

        min.X = max.X - graphRect.GetWidth() * relativeGraphWidth;

        var fMod = (float)MathUtils.Fmod(data.NormalizedTime, 1);
        var x = MathUtils.Lerp(min.X, max.X, fMod);

        var moduloLineY = (int)MathUtils.Lerp(min.Y, max.Y, 0.85f);
        drawList.AddRectFilled(new Vector2(x, min.Y),
                               new Vector2(x + 1, usingModulo ? moduloLineY : max.Y),
                               UiColors.StatusAnimated);

        if (usingModulo)
        {
            drawList.AddRectFilled(new Vector2(min.X, moduloLineY),
                                   new Vector2(max.X, moduloLineY + 1),
                                   UiColors.WidgetAxis);

            var f = ((int)data.NormalizedTime).Mod(modulo) / (float)modulo;
            var x2 = MathUtils.Lerp(min.X, max.X, f);
            var rectWidth = Math.Max(1, (max.X - min.X) / Math.Abs(modulo));

            var color = Color.Mix(UiColors.StatusAnimated,
                                  UiColors.WidgetAxis.Fade(0.3f),
                                  fMod);

            drawList.AddRectFilled(new Vector2(x2, moduloLineY + 1),
                                   new Vector2(x2 + rectWidth, max.Y),
                                   color);
        }

        var fade = canvas.Scale.X.RemapAndClamp(1f, 2f, 0, 1);
        if (fade > 0)
        {
            var label = $"{(int)data.NormalizedTime}";
            if (usingModulo)
            {
                label = $"{((int)data.NormalizedTime).Mod(modulo)}";
            }
            ImGui.PushFont(Fonts.FontLarge);
            var size = ImGui.CalcTextSize(label);

            drawList.AddText(new Vector2(max.X - size.X, graphRect.GetCenter().Y - Fonts.FontLarge.FontSize * 0.5f),
                             UiColors.ForegroundFull.Fade(fade),
                             label);

            ImGui.PopFont();
        }

        ImGui.PopID();

        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip
               | (isNodeActivated ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }
}