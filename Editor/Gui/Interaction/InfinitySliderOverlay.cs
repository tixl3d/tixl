#nullable  enable
using System.Runtime.CompilerServices;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction;

/// <remarks>
/// Terminology
/// valueRange - delta value for complete revolution of current dial
/// tickInterval = Log10 delta vale between ticks.
/// </remarks>
internal static class InfinitySliderOverlay
{
    private const int Log10YDistance = 100;
    private const float Width = 750;

    internal static void Draw(ref double roundedValue, 
                              bool restarted, 
                              Vector2 center, 
                              double min = double.NegativeInfinity,
                              double max = double.PositiveInfinity,
                              float scale = 0.1f, 
                              bool clamp = false)
    {
        var drawList = ImGui.GetForegroundDrawList();
        _io = ImGui.GetIO();

        if (restarted)
        {
            _value = roundedValue;
            _center = _io.MousePos;
            _verticalDistance = 50;
            //_dampedAngleVelocity = 0;
            _dampedModifierScaleFactor = 1;
            _lastXOffset = 0;
            _originalValue = roundedValue;
            _isManipulating = false;
        }

        // Update value...
        var mousePosX = (int)(_io.MousePos.X * 2)/2;
        var xOffset = mousePosX - _center.X;
        var deltaX = xOffset - _lastXOffset;
        if(MathF.Abs(xOffset) > UserSettings.Config.ClickThreshold)
        {
            _isManipulating = true;
        }
            
        _lastXOffset = xOffset;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _dragCenterStart = _center;
        }

        var isDraggingWidgetPosition = ImGui.IsMouseDown(ImGuiMouseButton.Right);
        if (isDraggingWidgetPosition)
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            _center = _dragCenterStart + ImGui.GetMouseDragDelta(ImGuiMouseButton.Right);
        }
        else
        {
            _verticalDistance = _center.Y - _io.MousePos.Y;
        }
            
        //_dampedAngleVelocity = MathUtils.Lerp(_dampedAngleVelocity, (float)deltaX, 0.06f);

        // Update radius and value range
        var normalizedLogDistanceForLog10 = _verticalDistance / Log10YDistance;

        // Value range and tick interval 
        _dampedModifierScaleFactor = MathUtils.Lerp(_dampedModifierScaleFactor, GetKeyboardScaleFactor(), 0.1f);

        var valueRange = (Math.Pow(10, normalizedLogDistanceForLog10)) * scale *0.25f * _dampedModifierScaleFactor * 600;

        // Update value...
        if (!isDraggingWidgetPosition)
        {
            _value += deltaX / Width * valueRange;
            if (clamp)
                _value = _value.Clamp(min, max);
        }
        
        DrawUi(out roundedValue, min, max, valueRange, mousePosX, drawList);
        
        if (!_isManipulating)
            roundedValue = _originalValue;
    }

    private static bool DrawUi(out double roundedValue, double min, double max, double valueRange, int mousePosX,
                               ImDrawListPtr drawList)
    {
        var log10 = Math.Log10(valueRange);
        var iLog10 = Math.Floor(log10);
        var logRemainder = log10 - iLog10;
        var tickValueInterval = Math.Pow(10, iLog10 - 1);
        roundedValue = _io.KeyCtrl ? _value : Math.Round(_value / (tickValueInterval / 10)) * (tickValueInterval / 10);

        var rSize = new Vector2(Width, 40);
        var rCenter = new Vector2(mousePosX, _io.MousePos.Y - rSize.Y);
        var rect = new ImRect(rCenter - rSize / 2, rCenter + rSize / 2);

        var numberOfTicks = valueRange / tickValueInterval;
        var valueTickRemainder = MathUtils.Fmod(_value, tickValueInterval);

        // Draw scale range indicates
        {
            for (var yIndex = -2; yIndex < 4; yIndex++)
            {
                var centerPoint = new Vector2(mousePosX, _center.Y - Log10YDistance * yIndex);
                var v = Math.Pow(10, yIndex);
                var label = $"× {v:G5}";
                var size = ImGui.CalcTextSize(label);

                var fade = (MathF.Abs(centerPoint.Y - rCenter.Y) / 50).Clamp(0, 1);

                var boxSize = new Vector2(80, 100);
                var labelCenter = centerPoint + new Vector2(10);
                drawList.AddRectFilled(centerPoint - boxSize / 2, centerPoint + boxSize / 2 + new Vector2(0, -1), UiColors.BackgroundFull.Fade(0.3f * fade),
                                       3);
                drawList.AddText(
                                 labelCenter - new Vector2(size.X / 2 + 10, 18),
                                 UiColors.ForegroundFull.Fade(fade * 0.6f),
                                 label);
            }
        }

        // Draw ticks with labels
        drawList.AddRectFilled(rCenter - rSize / 2, rCenter + rSize / 2, UiColors.BackgroundFull.Fade(0.8f), 8);
        for (var tickIndex = -(int)numberOfTicks / 2; tickIndex < numberOfTicks / 2; tickIndex++)
        {
            var f = MathF.Pow(MathF.Abs(tickIndex / ((float)numberOfTicks / 2)), 2f);
            var negF = 1 - f;
            var valueAtTick = tickIndex * tickValueInterval + _value - valueTickRemainder;
            GetXForValueIfVisible(valueAtTick, valueRange, mousePosX, Width, out var tickX);
            var isPrimary = Math.Abs(MathUtils.Fmod(valueAtTick + tickValueInterval * 5, tickValueInterval * 10) - tickValueInterval * 5) <
                            tickValueInterval / 10;
            var isPrimary2 = Math.Abs(MathUtils.Fmod(valueAtTick + tickValueInterval * 50, tickValueInterval * 100) - tickValueInterval * 50) <
                             tickValueInterval / 100;

            var fff = MathUtils.SmootherStep(1, 0.8f, (float)logRemainder);
            drawList.AddLine(
                             new Vector2(tickX, rect.Max.Y),
                             new Vector2(tickX, rect.Max.Y - 10),
                             UiColors.ForegroundFull.Fade(negF * (isPrimary ? 1 : 0.5f * fff)),
                             1
                            );

            var font = isPrimary2 ? Fonts.FontBold : Fonts.FontNormal;
            var v = Math.Abs(valueAtTick) < 0.0001 ? 0 : valueAtTick;
            var label = $"{v:G5}";

            var ff = (1 - (float)logRemainder * 2);
            if (isPrimary2 || ff < 1)
            {
                ImGui.PushFont(font);
                var size = ImGui.CalcTextSize(label);
                ImGui.PopFont();

                drawList.AddText(font,
                                 font.FontSize,
                                 new Vector2(tickX - 1 - size.X / 2, rect.Max.Y - 30),
                                 UiColors.BackgroundFull.Fade(negF * ff),
                                 label);

                drawList.AddText(font,
                                 font.FontSize,
                                 new Vector2(tickX - size.X / 2 + 1, rect.Max.Y - 30),
                                 UiColors.BackgroundFull.Fade(negF * ff),
                                 label);

                var fadeOut = (isPrimary ? 1 : ff) * 0.7f;
                drawList.AddText(font,
                                 font.FontSize,
                                 new Vector2(tickX - size.X / 2, rect.Max.Y - 30),
                                 UiColors.ForegroundFull.Fade(negF * (isPrimary2 ? 1 : fadeOut)),
                                 label);
            }
        }



        // Draw Value range
        {
            var rangeMin = _value - valueRange / 2;
            var rangeMax = _value + valueRange / 2;
            if (min > -9999999 && max < 9999999 && !(min < rangeMin && max < rangeMin || (min > rangeMax && max > rangeMax)))
            {
                var visibleMinValue = Math.Max(min, rangeMin);
                var visibleMaxValue = Math.Min(max, rangeMax);

                var y = rect.Max.Y - 1.5f;
                GetXForValueIfVisible(visibleMinValue, valueRange, mousePosX, Width, out var visibleMinX);
                GetXForValueIfVisible(visibleMaxValue, valueRange, mousePosX, Width, out var visibleMaxX);
                drawList.AddLine(new Vector2(visibleMinX, y),
                                 new Vector2(visibleMaxX, y),
                                 UiColors.ForegroundFull.Fade(0.5f),
                                 2);
            }
        }

        // Current value at mouse
        {
            if (!GetXForValueIfVisible(roundedValue, valueRange, mousePosX, Width, out var screenX))
                return true;

            ImGui.PushFont(Fonts.FontLarge);
            var label = $"{roundedValue:G7}\n";
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddRectFilled(
                                   new Vector2(screenX - labelSize.X / 2 - 10, rect.Max.Y),
                                   new Vector2(screenX + labelSize.X / 2 + 10, rect.Max.Y + 35),
                                   UiColors.BackgroundFull.Fade(0.8f),
                                   5
                                  );
            drawList.AddLine(new Vector2(screenX, rect.Min.Y),
                             new Vector2(screenX, rect.Max.Y + 5),
                             UiColors.StatusActivated,
                             1
                            );
            drawList.AddText(new Vector2(screenX - labelSize.X / 2, rect.Max.Y + 3),
                             Color.White.Fade(1),
                             label
                            );
            ImGui.PopFont();
        }
            
        // Draw previous value
        {
            if (GetXForValueIfVisible(_originalValue, valueRange, mousePosX, Width, out var visibleMinX))
            {
                var y = rect.Min.Y;
                    
                drawList.AddTriangleFilled(new Vector2(visibleMinX, y+4),
                                           new Vector2(visibleMinX+4, y),
                                           new Vector2(visibleMinX-4, y),
                                           UiColors.ForegroundFull);
            }
        }
        return false;
    }

    private static bool IsValueVisible(double value, double valueRange)
    {
        return Math.Abs(_value - value) <= valueRange / 2 + 0.001;
    }

    private static float GetXForValue(double value, double valueRange, float mousePosX)
    {
        return (float)((_value - value) / valueRange + mousePosX);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool GetXForValueIfVisible(double v, double valueRange, float mouseX, float width, out float x)
    {
        x = float.NaN;
        if (!IsValueVisible(v, valueRange))
            return false;

        x = MathF.Floor((float)((v - _value) / valueRange * width + mouseX));
        return true;
    }

    private static double GetKeyboardScaleFactor()
    {
        if (_io.KeyAlt)
            return 10;

        if (_io.KeyShift)
            return 0.1;

        return 1;
    }

    /** The precise value before rounding. This used for all internal calculations. */
    private static double _value;

    private static float _verticalDistance;
    private static Vector2 _center = Vector2.Zero;
    private static Vector2 _dragCenterStart = Vector2.Zero;
    //private static float _dampedAngleVelocity;
    private static double _lastXOffset;
    private static double _dampedModifierScaleFactor;

    private static double _originalValue;
    private static bool _isManipulating;

    private static ImGuiIOPtr _io;
}