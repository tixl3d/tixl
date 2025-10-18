#nullable enable
using ImGuiNET;
using System.Reflection;
using T3.Core.Audio;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

//using Lib.io.audio;

namespace T3.Editor.Gui.OpUis.UIs;

// ReSharper disable once UnusedType.Global
internal static class AudioReactionUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
            _instance = instance;
        }

        private readonly Instance _instance;

        [BindInput("44409811-1a0f-4be6-83ea-b2f040ebf08b")]
        internal readonly InputSlot<int> InputBand = null!;

        [BindInput("1D3A1132-E8B9-4C04-99BF-80F6F1530309")]
        internal readonly InputSlot<float> WindowWidth = null!;

        [BindInput("F3D7C7FD-4280-4FB4-9F9A-C39B28D1A72B")]
        internal readonly InputSlot<float> WindowCenter = null!;

        [BindInput("F920A79D-C946-4A78-9E68-F64FC3C4A696")]
        internal readonly InputSlot<float> WindowEdge = null!;

        [BindInput("02F71A92-D5C8-4DD7-AF5F-DA12330F60EB")]
        internal readonly InputSlot<float> Threshold = null!;

        [BindProperty("Sum")]
        private readonly PropertyInfo? _sumProp = null!;

        internal float Sum => (float)(_sumProp?.GetValue(_instance) ?? 0);


        [BindProperty("ActiveBins")]
        private readonly PropertyInfo? _activeBindsProp = null!;

        internal List<float>? ActiveBins => (List<float>?)_activeBindsProp?.GetValue(_instance);

        [BindProperty("TimeSinceLastHit")]
        private readonly PropertyInfo? _timeSinceHitField = null!;

        internal double TimeSinceLastHit => (double)(_timeSinceHitField?.GetValue(_instance) ?? 0);

        [BindProperty("AccumulatedLevel")]
        private readonly PropertyInfo? _accumulationLevelProp = null!;

        internal double AccumulatedLevel => (double)(_accumulationLevelProp?.GetValue(_instance) ?? 0);

        [BindField("AccumulationActive")]
        private readonly FieldInfo? _accumulationActiveField = null!;

        internal bool AccumulationActive => (bool)(_accumulationActiveField?.GetValue(_instance) ?? false);
    }

    public enum InputModes
    {
        RawFft,
        NormalizedFft,
        FrequencyBands,
        FrequencyBandsPeaks,
        FrequencyBandsAttacks,
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
        var dragWidth = WidgetElements.DrawOperatorDragHandle(screenRect, drawList, canvas.Scale);
        if (!ImGui.IsRectVisible(screenRect.Min, screenRect.Max))
            return OpUi.CustomUiResult.None;

        var h = screenRect.GetHeight();
        var w = screenRect.GetWidth();
        if (h < 10 || data.ActiveBins == null)
        {
            return OpUi.CustomUiResult.None;
        }
        screenRect.Min.X += dragWidth;
        ImGui.PushID(instance.SymbolChildId.GetHashCode());
        drawList.PushClipRect(screenRect.Min, screenRect.Max, true);

        // Draw bins and window
        var windowCenter = data.WindowCenter.GetCurrentValue();
        var windowEdge = data.WindowEdge.GetCurrentValue();
        var windowWidth = data.WindowWidth.GetCurrentValue();

        var freqGraphWidth = w * 0.6f;
        var maxBars = 128;
        var x = screenRect.Min.X;
        var bottom = screenRect.Max.Y;

        var fftBuffer = data.ActiveBins;
        var binCount = fftBuffer.Count;
        var barsCount = Math.Min(binCount, maxBars);
        var barWidth = freqGraphWidth / barsCount;
        var binsPerBar = (float)binCount / barsCount;
        const float valueScale = 0.5f;

        var inputMode = (InputModes)data.InputBand.Value.Clamp(0, Enum.GetNames(typeof(InputModes)).Length);
        if (inputMode == InputModes.FrequencyBandsAttacks
            || inputMode == InputModes.FrequencyBands)
        {
            var xPeaks = screenRect.Min.X;
            float[]? peakBands;
            switch (inputMode)
            {
                case InputModes.FrequencyBands:
                    peakBands = AudioAnalysis.FrequencyBandPeaks;
                    break;
                case InputModes.FrequencyBandsAttacks:
                    peakBands = AudioAnalysis.FrequencyBandAttackPeaks;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            for (var barIndex = 0; barIndex < peakBands.Length; barIndex++)
            {
                var peak = peakBands[barIndex];

                drawList.AddRectFilled(new Vector2(xPeaks, bottom - peak * h * valueScale - 2),
                                       new Vector2(xPeaks + barWidth, bottom - 1),
                                       UiColors.BackgroundFull.Fade(0.06f));
                xPeaks += barWidth;
            }
        }

        var binIndex = 0;
        var inactiveColor = UiColors.BackgroundFull.Fade(0.2f);
        for (var barIndex = 0; barIndex < barsCount; barIndex++)
        {
            var sum = 0f;
            var count = 0;
            var maxBinForBar = barIndex * binsPerBar;
            while (binIndex <= maxBinForBar)
            {
                sum += fftBuffer[binIndex];
                binIndex++;
                count++;
            }

            sum /= count;

            var f = (float)barIndex / (barsCount - 1);
            var factor = (MathF.Abs((f - windowCenter) / windowEdge) - windowWidth / windowEdge).Clamp(0.0f, 1);

            drawList.AddRectFilled(new Vector2(x, bottom - sum * h * valueScale - 2),
                                   new Vector2(x + barWidth, bottom - 1),
                                   Color.Mix(UiColors.StatusAnimated, inactiveColor, factor));
            x += barWidth;
        }

        x += barWidth;

        // Draw sum and threshold
        x += 2;
        var flashFactor = MathF.Pow((1 - (float)data.TimeSinceLastHit * 2).Clamp(0, 1), 4);
        drawList.AddRectFilled(new Vector2(x, bottom - data.Sum * h * valueScale),
                               new Vector2(x + w / 20, bottom),
                               Color.Mix(inactiveColor, UiColors.StatusAnimated, flashFactor));

        var thresholdY = data.Threshold.GetCurrentValue() * h * valueScale;
        drawList.AddRectFilled(new Vector2(x, bottom - thresholdY),
                               new Vector2(x + w / 20, bottom - thresholdY + 2),
                               UiColors.StatusAnimated);

        var w2 = windowWidth * freqGraphWidth;
        var x1 = screenRect.Min.X + windowCenter * freqGraphWidth - w2 / 2;
        var x2 = screenRect.Min.X + windowCenter * freqGraphWidth + w2 / 2;

        drawList.AddRectFilled(new Vector2(x1, bottom - thresholdY),
                               new Vector2(x2 + w / 20, bottom - thresholdY + 1),
                               UiColors.ForegroundFull.Fade(0.5f));

        // Draw Spinner
        if (data.AccumulationActive)
        {
            var center = new Vector2(screenRect.Max.X - h / 2, screenRect.Min.Y + h / 2);

            var a = (data.AccumulatedLevel) % (Math.PI * 2);
            drawList.PathClear();

            drawList.PathArcTo(center, h * 0.3f, (float)a, (float)a + 2.6f);
            drawList.PathStroke(UiColors.StatusAnimated, ImDrawFlags.None, 3);
        }

        var graphRect = screenRect;
        graphRect.Expand(-3);
        //graphRect.Min.X = graphRect.Max.X - graphRect.GetWidth() * RelativeGraphWidth;

        // Graph dragging to edit Bias and Ratio
        var isActive = false;

        ImGui.SetCursorScreenPos(graphRect.Min);
        if (ImGui.GetIO().KeyCtrl)
        {
            ImGui.InvisibleButton("dragMicroGraph", graphRect.GetSize());
            isActive = ImGui.IsItemActive();
        }

        if (isActive)
        {
            var dragDelta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 1);

            if (ImGui.IsItemActivated())
            {
                //_dragStartPosition = ImGui.GetMousePos();
                _dragStartThreshold = data.Threshold.TypedInputValue.Value;
                _dragStartWindow = data.WindowCenter.TypedInputValue.Value;
            }

            if (Math.Abs(dragDelta.X) > 0.5f)
            {
                data.WindowCenter.SetTypedInputValue((_dragStartWindow + dragDelta.X / 200f).Clamp(0.001f, 1f));
                // data.WindowCenter.DirtyFlag.Invalidate();
                // data.WindowCenter.Input.IsDefault = false;
            }

            if (Math.Abs(dragDelta.Y) > 0.5f)
            {
                data.Threshold.SetTypedInputValue((_dragStartThreshold - dragDelta.Y / 100f).Clamp(0.01f, 3f));
                //data.Threshold.TypedInputValue.Value = ;
                //data.Threshold.DirtyFlag.Invalidate();
                //data.Threshold.Input.IsDefault = false;
            }
        }

        drawList.PopClipRect();
        ImGui.PopID();
        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip
               | (isActive ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }

    private static float _dragStartThreshold;
    private static float _dragStartWindow;

    //private static readonly Color _highlightColor = UiColors.Animation;
    //private static readonly Color _inactiveColor = Color.Black.Fade(0.2f);
}