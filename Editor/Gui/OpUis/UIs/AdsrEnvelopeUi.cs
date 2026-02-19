#nullable enable
using System.Reflection;
using ImGuiNET;
using T3.Core.Audio;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class AdsrEnvelopeUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
            _instance = instance;
        }

        private readonly Instance _instance;

        [BindInput("d9e0f1a2-b3c4-4d5e-6f7a-8b9c0d1e2f3a")]
        internal readonly InputSlot<bool> Gate = null!;

        [BindInput("a2b3c4d5-e6f7-4a8b-9c0d-1e2f3a4b5c6d")]
        internal readonly InputSlot<Vector4> Envelope = null!;

        [BindOutput("d5e6f7a8-b9c0-4d1e-2f3a-4b5c6d7e8f9a")]
        internal readonly Slot<float> Result = null!;

        [BindOutput("f7a8b9c0-d1e2-4f3a-4b5c-6d7e8f9a0b1c")]
        internal readonly Slot<bool> IsActive = null!;

        [BindField("_calculator")]
        private readonly FieldInfo? _calculatorField = null!;

        internal AdsrCalculator? Calculator => (AdsrCalculator?)_calculatorField?.GetValue(_instance);
    }

    private enum DragTarget
    {
        None,
        Attack,
        Decay,
        Sustain,
        Release
    }

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect screenRect,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid || instance.Parent == null)
            return OpUi.CustomUiResult.None;

        var dragWidth = WidgetElements.DrawOperatorDragHandle(screenRect, drawList, canvas.Scale);
        var innerRect = screenRect;
        innerRect.Min.X += dragWidth;
        innerRect.Min.Y += 1;

        if (innerRect.GetHeight() < 10)
            return OpUi.CustomUiResult.PreventTooltip
                   | OpUi.CustomUiResult.PreventOpenSubGraph
                   | OpUi.CustomUiResult.PreventInputLabels
                   | OpUi.CustomUiResult.PreventOpenParameterPopUp;

        var envelope = data.Envelope.HasInputConnections
                           ? data.Envelope.Value
                           : data.Envelope.TypedInputValue.Value;

        ImGui.PushClipRect(innerRect.Min, innerRect.Max, true);

        var attack = Math.Max(0.001f, envelope.X);
        var decay = Math.Max(0.001f, envelope.Y);
        var sustain = Math.Clamp(envelope.Z, 0f, 1f);
        var release = Math.Max(0.001f, envelope.W);

        // Draw envelope curve and handles
        var isActive = DrawEnvelopeEditor(data, drawList, innerRect, attack, decay, sustain, release, canvas.Scale);

        // Draw current position indicator line (like SequenceAnimUi)
        DrawPositionIndicator(data, drawList, innerRect, attack, decay, sustain, release);

        // Draw current value indicator
        DrawValueIndicator(data, drawList, innerRect, canvas.Scale);

        ImGui.PopClipRect();

        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventTooltip
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventOpenParameterPopUp
               | (isActive ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }

    private static bool DrawEnvelopeEditor(Binding data, ImDrawListPtr drawList, ImRect area,
                                           float attack, float decay, float sustain, float release,
                                           Vector2 scale)
    {
        // Add padding to prevent handles from being cut off
        const float handlePadding = 4f;
        var paddedArea = new ImRect(
            new Vector2(area.Min.X + handlePadding, area.Min.Y + handlePadding),
            new Vector2(area.Max.X - handlePadding, area.Max.Y - handlePadding)
        );
        var width = paddedArea.GetWidth();
        var height = paddedArea.GetHeight();
        const float sustainDuration = 0.15f;
        var totalTime = attack + decay + sustainDuration + release;

        // Calculate key positions
        var attackX = paddedArea.Min.X + width * (attack / totalTime);
        var decayX = paddedArea.Min.X + width * ((attack + decay) / totalTime);
        var sustainX = paddedArea.Min.X + width * ((attack + decay + sustainDuration) / totalTime);
        var sustainY = paddedArea.Max.Y - (height - 4) * sustain - 2;
        var peakY = paddedArea.Min.Y + 2;

        // Draw the envelope curve
        DrawEnvelopeCurve(drawList, paddedArea, attack, decay, sustain, release, sustainDuration);

        // Draw label "Envelope" at the top left (matching AnimValueUi style)
        WidgetElements.DrawSmallTitle(drawList, paddedArea, "Envelope", scale);

        // Calculate handle positions on the curve
        var handles = new[]
        {
            new HandleInfo(DragTarget.Attack, new Vector2(attackX, peakY), "Attack"),
            new HandleInfo(DragTarget.Decay, new Vector2(decayX, sustainY), "Decay"),
            new HandleInfo(DragTarget.Sustain, new Vector2((decayX + sustainX) / 2, sustainY), "Sustain"),
            new HandleInfo(DragTarget.Release, new Vector2(paddedArea.Max.X - 2, paddedArea.Max.Y - 2), "Release")
        };

        // Draw and handle interaction
        return DrawAndHandleHandles(data, drawList, paddedArea, handles, scale);
    }

    private readonly struct HandleInfo(DragTarget target, Vector2 position, string label)
    {
        public readonly DragTarget Target = target;
        public readonly Vector2 Position = position;
        public readonly string Label = label;
    }

    private static void DrawEnvelopeCurve(ImDrawListPtr drawList, ImRect area,
                                          float attack, float decay, float sustain, float release,
                                          float sustainDuration)
    {
        var width = area.GetWidth();
        var height = area.GetHeight() - 4;
        var baseY = area.Max.Y - 2;
        var peakY = area.Min.Y + 2;
        var sustainY = baseY - height * sustain;

        var totalTime = attack + decay + sustainDuration + release;
        var attackX = area.Min.X + width * (attack / totalTime);
        var decayX = area.Min.X + width * ((attack + decay) / totalTime);
        var sustainX = area.Min.X + width * ((attack + decay + sustainDuration) / totalTime);

        // Build curve points (5-point envelope)
        var points = new Vector2[5];
        points[0] = new Vector2(area.Min.X, baseY);       // Start
        points[1] = new Vector2(attackX, peakY);          // Attack peak
        points[2] = new Vector2(decayX, sustainY);        // End of decay
        points[3] = new Vector2(sustainX, sustainY);      // End of sustain
        points[4] = new Vector2(area.Max.X, baseY);       // End of release

        // Fill area under curve with dark color
        var fillPoints = new Vector2[points.Length + 2];
        fillPoints[0] = new Vector2(area.Min.X, baseY);
        for (var i = 0; i < points.Length; i++)
            fillPoints[i + 1] = points[i];
        fillPoints[^1] = new Vector2(area.Max.X, baseY);
        drawList.AddConvexPolyFilled(ref fillPoints[0], fillPoints.Length, UiColors.BackgroundFull.Fade(0.3f));

        // Draw curve line (matching AnimValueUi style)
        drawList.AddPolyline(ref points[0], points.Length, UiColors.WidgetLine, ImDrawFlags.None, 1.5f);

        // Draw subtle grid lines at key positions (matching AnimValueUi)
        drawList.AddLine(new Vector2(attackX, area.Min.Y), new Vector2(attackX, area.Max.Y), UiColors.WidgetAxis, 1f);
        drawList.AddLine(new Vector2(decayX, area.Min.Y), new Vector2(decayX, area.Max.Y), UiColors.WidgetAxis, 1f);
        drawList.AddLine(new Vector2(sustainX, area.Min.Y), new Vector2(sustainX, area.Max.Y), UiColors.WidgetAxis, 1f);

        // Draw sustain level line
        drawList.AddLine(new Vector2(decayX, sustainY), new Vector2(sustainX, sustainY), UiColors.WidgetAxis, 1);

        // Draw labels centered in each region
        var labelColor = UiColors.TextMuted;
        ImGui.PushFont(Fonts.FontSmall);
        var labelY = area.Max.Y - 12;
        
        // Calculate label positions centered in each region
        var attackCenter = area.Min.X + (attackX - area.Min.X) / 2;
        var decayCenter = attackX + (decayX - attackX) / 2;
        var sustainCenter = decayX + (sustainX - decayX) / 2;
        var releaseCenter = sustainX + (area.Max.X - sustainX) / 2;
        
        // Get text width to center horizontally
        var aSize = ImGui.CalcTextSize("A");
        var dSize = ImGui.CalcTextSize("D");
        var sSize = ImGui.CalcTextSize("S");
        var rSize = ImGui.CalcTextSize("R");
        
        drawList.AddText(new Vector2(attackCenter - aSize.X / 2, labelY), labelColor, "A");
        drawList.AddText(new Vector2(decayCenter - dSize.X / 2, labelY), labelColor, "D");
        drawList.AddText(new Vector2(sustainCenter - sSize.X / 2, labelY), labelColor, "S");
        drawList.AddText(new Vector2(releaseCenter - rSize.X / 2, labelY), labelColor, "R");
        ImGui.PopFont();
    }

    private static bool DrawAndHandleHandles(Binding data, ImDrawListPtr drawList, ImRect area,
                                             HandleInfo[] handles, Vector2 scale)
    {
        var isActive = false;
        // Diamond size and style matching SampleCurveUi
        var diamondSize = 8f;
        var half = diamondSize / 2f;
        var hitboxSize = 16f; // Larger hitbox for easier interaction
        var hitboxHalf = hitboxSize / 2f;
        var anyHandleActive = false;

        for (int i = 0; i < handles.Length; i++)
        {
            var handle = handles[i];
            var id = $"##adsr_handle_{i}";
            ImGui.SetCursorScreenPos(handle.Position - new Vector2(hitboxHalf, hitboxHalf));
            ImGui.InvisibleButton(id, new Vector2(hitboxSize, hitboxSize));
            var isHovered = ImGui.IsItemHovered();
            var isActiveHandle = ImGui.IsItemActive();
            
            if (isHovered && !anyHandleActive)
            {
                // Set cursor type based on handle
                switch (handle.Target)
                {
                    case DragTarget.Attack:
                    case DragTarget.Decay:
                    case DragTarget.Release:
                        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
                        break;
                    case DragTarget.Sustain:
                        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
                        break;
                }
            }
            if (isActiveHandle)
            {
                anyHandleActive = true;
            }
            // Draw diamond (rotated square)
            var fillColor = isHovered || isActiveHandle ? UiColors.ForegroundFull : UiColors.StatusAnimated;
            var outlineColor = 0xFF000000; // Black border
            var center = handle.Position;
            var diamond = new Vector2[4];
            diamond[0] = new Vector2(center.X, center.Y - half); // top
            diamond[1] = new Vector2(center.X + half, center.Y); // right
            diamond[2] = new Vector2(center.X, center.Y + half); // bottom
            diamond[3] = new Vector2(center.X - half, center.Y); // left
            drawList.AddConvexPolyFilled(ref diamond[0], 4, fillColor);
            drawList.AddPolyline(ref diamond[0], 4, outlineColor, ImDrawFlags.Closed, 1f);
            if (isHovered)
            {
                ImGui.SetTooltip($"{handle.Label}: drag to adjust");
            }
            
            // Handle dragging
            if (isActiveHandle)
            {
                isActive = true;
                var delta = ImGui.GetIO().MouseDelta;
                var width = area.GetWidth();
                var height = area.GetHeight();
                var timeSensitivity = 0.5f;
                if (ImGui.GetIO().KeyShift)
                    timeSensitivity *= 0.1f;
                var newEnvelope = data.Envelope.TypedInputValue.Value;
                switch (handle.Target)
                {
                    case DragTarget.Attack:
                        newEnvelope.X = Math.Max(0.001f, newEnvelope.X + delta.X / width * timeSensitivity);
                        break;
                    case DragTarget.Decay:
                        newEnvelope.Y = Math.Max(0.001f, newEnvelope.Y + delta.X / width * timeSensitivity);
                        break;
                    case DragTarget.Sustain:
                        newEnvelope.Z = Math.Clamp(newEnvelope.Z - delta.Y / height, 0f, 1f);
                        break;
                    case DragTarget.Release:
                        newEnvelope.W = Math.Max(0.001f, newEnvelope.W + delta.X / width * timeSensitivity);
                        break;
                }
                data.Envelope.SetTypedInputValue(newEnvelope);
            }
        }
        
        return isActive;
    }

    private static void DrawPositionIndicator(Binding data, ImDrawListPtr drawList, ImRect area,
                                              float attack, float decay, float sustain, float release)
    {
        var calculator = data.Calculator;
        if (calculator == null || calculator.CurrentStage == AdsrCalculator.Stage.Idle)
            return;

        const float sustainDuration = 0.15f;
        var totalTime = attack + decay + sustainDuration + release;
        var width = area.GetWidth();

        // Calculate position based on current stage and value
        float normalizedX;
        
        switch (calculator.CurrentStage)
        {
            case AdsrCalculator.Stage.Attack:
                // During attack: value goes from 0 to 1
                // X position: proportional to value within attack region
                normalizedX = calculator.Value * (attack / totalTime);
                break;

            case AdsrCalculator.Stage.Decay:
                // During decay: value goes from 1 to sustain level
                // X position: attack region + proportional position in decay
                var decayProgress = (1f - calculator.Value) / (1f - sustain);
                decayProgress = Math.Clamp(decayProgress, 0f, 1f);
                normalizedX = (attack / totalTime) + decayProgress * (decay / totalTime);
                break;

            case AdsrCalculator.Stage.Sustain:
                // During sustain: value stays at sustain level
                // X position: middle of sustain region (it's held here)
                normalizedX = (attack + decay) / totalTime + (sustainDuration / totalTime) * 0.5f;
                break;

            case AdsrCalculator.Stage.Release:
                // During release: value goes from sustain to 0
                // X position: sustain region end + proportional position in release
                var releaseProgress = 1f - (calculator.Value / sustain);
                releaseProgress = Math.Clamp(releaseProgress, 0f, 1f);
                normalizedX = (attack + decay + sustainDuration) / totalTime + releaseProgress * (release / totalTime);
                break;

            default:
                return;
        }

        var posX = area.Min.X + normalizedX * width;
        
        // Draw vertical position indicator line (like SequenceAnimUi)
        drawList.AddRectFilled(
            new Vector2(posX, area.Min.Y),
            new Vector2(posX + 2, area.Max.Y),
            UiColors.WidgetActiveLine);
    }

    private static void DrawValueIndicator(Binding data, ImDrawListPtr drawList, ImRect area, Vector2 scale)
    {
        if (scale.X < 0.5f)
            return;

        var value = data.Result.Value;
        var isGateActive = data.Gate.GetCurrentValue();

        // Draw current output value
        var valueText = $"{value:F2}";
        ImGui.PushFont(Fonts.FontSmall);
        var textSize = ImGui.CalcTextSize(valueText);
        var textPos = new Vector2(area.Max.X - textSize.X - 4, area.Min.Y + 2);
        var textColor = isGateActive ? UiColors.StatusAnimated : UiColors.TextMuted;
        drawList.AddText(textPos, textColor, valueText);
        ImGui.PopFont();
    }
}
