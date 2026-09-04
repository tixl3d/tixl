#nullable enable
using System.Reflection;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class SequenceAnimUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
            _instance = instance;

            var methodInfo = instance.GetType().GetMethod("SetStepValue",
                                                          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            SetStep = (Action<int, float>)Delegate.CreateDelegate(typeof(Action<int, float>), instance, methodInfo!);
        }

        private readonly Instance _instance;

        internal readonly Action<int, float> SetStep;

        [BindField("CurrentSequence")]
        private readonly FieldInfo? _currentSequenceField = null!;

        internal List<float>? CurrentSequence => (List<float>?)_currentSequenceField?.GetValue(_instance);

        [BindProperty("NormalizedBarTime")]
        private readonly PropertyInfo? _normalizedBarTimeProp = null!;

        internal float NormalizedBarTime => (float)(_normalizedBarTimeProp?.GetValue(_instance) ?? 0);

        [BindProperty("IsRecording")]
        private readonly PropertyInfo? _isRecordingProp = null!;

        internal bool IsRecording => (bool)(_isRecordingProp?.GetValue(_instance) ?? false);

        [BindInput("F0AE47AE-5849-4D81-BAE0-9B6EC44949EF")]
        internal readonly InputSlot<float> Rate = null!;
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

        ImGui.PushID(instance.SymbolChildId.GetHashCode());

        // if (RateEditLabel.Draw(ref data.Rate.TypedInputValue.Value,
        //                        screenRect, drawList, nameof(sequenceAnim) + " "))
        // {
        //     data.Rate.Input.IsDefault = false;
        //     data.Rate.DirtyFlag.Invalidate();
        // }

        var isEditActive = false;
        var mousePos = ImGui.GetMousePos();
        var editUnlocked = ImGui.GetIO().KeyCtrl;
        //var highlight = editUnlocked;

        // Speed Interaction
        //var speedRect = selectableScreenRect;
        //speedRect.Max.X = speedRect.Min.X +  speedRect.GetWidth() * 0.2f;
        //ImGui.SetCursorScreenPos(speedRect.Min);

        var h = screenRect.GetHeight();
        var w = screenRect.GetWidth();
        if (h < 10 || data.CurrentSequence == null || data.CurrentSequence.Count == 0)
        {
            ImGui.PopID(); // matches PushID above
            return OpUi.CustomUiResult.None;
        }

        if (editUnlocked)
        {
            ImGui.SetCursorScreenPos(screenRect.Min);
            ImGui.InvisibleButton("rateButton", screenRect.GetSize());
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
            }

            isEditActive = ImGui.IsItemActive();
        }

        drawList.PushClipRect(screenRect.Min, screenRect.Max, true);

        // Draw bins and window

        var x = screenRect.Min.X;
        var bottom = screenRect.Max.Y;

        var barCount = data.CurrentSequence.Count;
        var barWidth = w / barCount;
        var xPeaks = screenRect.Min.X;

        var currentIndex = (int)(data.NormalizedBarTime * barCount);

        ImGui.PushFont(Fonts.FontSmall);
        for (int barIndex = 0; barIndex < barCount; barIndex++)
        {
            var pMin = new Vector2(x, screenRect.Min.Y);
            var pMax = new Vector2(x + barWidth, bottom - 1);

            if (isEditActive && mousePos.X > pMin.X && mousePos.X < pMax.X)
            {
                var value = 1 - ((mousePos.Y + 3 - screenRect.Min.Y) / (h - 6)).Clamp(0, 1);
                data.SetStep(barIndex, value);
            }

            var highlightFactor = barIndex == currentIndex
                                      ? 1 - (data.NormalizedBarTime * barCount - barIndex).Clamp(0, 1)
                                      : 0;

            var barIntensity = barIndex % 4 == 0 ? 0.4f : 0.1f;

            drawList.AddRectFilled(pMin,
                                   new Vector2(x + 1, bottom - 1),
                                   UiColors.WidgetBackgroundStrong.Fade(barIntensity)
                                  );

            var peak = data.CurrentSequence[barIndex];
            drawList.AddRectFilled(new Vector2(x + 1, bottom - peak * h - 2),
                                   new Vector2(x + barWidth, bottom - 1),
                                   Color.Mix(_inactiveColor, _highlightColor, highlightFactor));

            drawList.AddText(pMin + new Vector2(2, 0), UiColors.WidgetBackgroundStrong.Fade(barIntensity), "" + (barIndex + 1));
            x += barWidth;
            xPeaks += barWidth;
        }

        ImGui.PopFont();

        var min = screenRect.Min + new Vector2(data.NormalizedBarTime * w, 0);
        drawList.AddRectFilled(min,
                               min + new Vector2(1, h),
                               data.IsRecording ? UiColors.StatusAttention : UiColors.WidgetActiveLine);

        drawList.PopClipRect();
        ImGui.PopID();
        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip
               | (isEditActive ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }

    // private static float _dragStartBias;
    // private static float _dragStartRatio;

    private static readonly Color _highlightColor = UiColors.StatusAnimated;
    private static readonly Color _inactiveColor = UiColors.WidgetBackgroundStrong.Fade(0.3f);

    //private static readonly Vector2[] GraphLinePoints = new Vector2[GraphListSteps];
    private const int GraphListSteps = 80;
}