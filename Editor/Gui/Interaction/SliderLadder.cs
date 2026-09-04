using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Interaction;

/// <summary>
/// Draws a range of virtual slider overlays
/// </summary>
internal static class SliderLadder
{
    private sealed class RangeDef
    {
        // public readonly float YMin;
        // public readonly float YMax;
        public readonly float ScaleFactor;
        public readonly string Label;

        public RangeDef(float scaleFactor, string label)
        {
            ScaleFactor = scaleFactor;
            Label = label;
        }
    }

    private static readonly RangeDef[] Ranges =
        {
            new(1000, "1000"),
            new(100, "100"),
            new(10, "10"),
            new(1, "1"),
            new(0.1f, "0.1"),
            new(0.01f, "0.01"),
            new(0.001f, "0.001"),
        };

    private static RangeDef _lockedRange;

    private const float LockDistance = 100;

    public static void Draw(ref double editValue, double min, double max, float scale, float timeSinceVisible, bool clampMin, bool clampMax, Vector2 center)
    {
        var io = ImGui.GetIO();
        var foreground = ImGui.GetForegroundDrawList();
        const double initialDelay = 0.2;

        var pNow = io.MousePos - center;
        if (timeSinceVisible < initialDelay)
        {
            _lockedRange = null;
            _hasExceededDragThreshold = false;
            _lastX = 0f;
        }

        var x = MathF.Floor(pNow.X / PixelsPerStep);

        _hasExceededDragThreshold |= MathF.Abs(pNow.X) > PixelsPerStep * 4;

        var dx = x - _lastX;
        var activeScaleFactor = 0.0;
        var usingCenterRange = false;

        // Find center range index
        int centerRangeIndex;
        for (centerRangeIndex = 0; centerRangeIndex < Ranges.Length; centerRangeIndex++)
        {
            var range = Ranges[centerRangeIndex];
            if (range.ScaleFactor < scale * 1.5f)
                break;
        }
            
            

        for (var rangeIndex = 0; rangeIndex < Ranges.Length; rangeIndex++)
        {
            var yMin = OuterRangeHeight * (rangeIndex - centerRangeIndex-1 + 0.5f);
            var yMax = OuterRangeHeight * (rangeIndex  - centerRangeIndex + 0.5f);
                
            var range = Ranges[rangeIndex];
            var isVerticalMatch = pNow.Y > yMin && pNow.Y < yMax;
            var isWithinLockRange = Math.Abs(pNow.X) < LockDistance;

            var isActiveRange = isVerticalMatch && isWithinLockRange
                                || _lockedRange == null && isVerticalMatch
                                || _lockedRange == range;

            if (isActiveRange)
            {
                activeScaleFactor = range.ScaleFactor;
                _lockedRange = isWithinLockRange ? null : range;
            }

            var isCenterRange = rangeIndex == centerRangeIndex;
            if (isCenterRange)
            {
                usingCenterRange = isActiveRange;
                continue;
            }

            // Draw
            var isLookedRange = _lockedRange == range;
            var centerColor = !isActiveRange
                                  ? _rangeFillColor
                                  : isLookedRange
                                      ? _lockedRangeFillColor
                                      : _activeRangeFillColor;

            var bMin = new Vector2(-RangeWidth, yMin) + center;
            var bMax = new Vector2(RangeWidth, yMax) + center;
            foreground.AddRectFilled(bMin, bMax, centerColor);
            foreground.AddRect(bMin, bMax, UiColors.BackgroundFull);

            var labelSize = ImGui.CalcTextSize(range.Label);
            var pText = (bMin + bMax) / 2 - labelSize / 2;

            foreground.AddText(pText,
                               isActiveRange ? UiColors.BackgroundFull : UiColors.ForegroundFull,
                               range.Label);
        }

        if (Math.Abs(dx) < 0.0001f)
            return;

        if (_hasExceededDragThreshold)
        {
            var delta = dx;
                
            if (io.KeyAlt)
            {
                ImGui.PushFont(Fonts.FontSmall);
                foreground.AddText(ImGui.GetMousePos() + new Vector2(10, 10), UiColors.Gray, "×0.01");
                ImGui.PopFont();

                scale *= 0.01f;
            }
            else if (io.KeyShift)
            {
                ImGui.PushFont(Fonts.FontSmall);
                foreground.AddText(ImGui.GetMousePos() + new Vector2(10, 10), UiColors.Gray, "×10");
                ImGui.PopFont();

                scale *= 10f;
            }

            var scaling = (usingCenterRange)
                              ? scale
                              : activeScaleFactor;

            editValue += delta * scaling;

            if (activeScaleFactor != 0 && (!usingCenterRange && ImGui.GetIO().KeyCtrl))
            {
                editValue = Math.Round(editValue / (activeScaleFactor)) * (activeScaleFactor);
            }

            editValue = MathUtils.OptionalClamp(editValue, min, clampMin, max, clampMax);
        }

        _lastX = x;
    }

    private const float PixelsPerStep = 10;
    private const float RangeWidth = 40;
    private const float OuterRangeHeight = 50;
    private static readonly Color _rangeFillColor = new(0.3f, 0.3f, 0.3f);
    private static readonly Color _activeRangeFillColor = new(0.8f, 0.8f, 0.8f);
    private static readonly Color _lockedRangeFillColor = new(1f, 0.6f, 0.6f);
    private static bool _hasExceededDragThreshold;
    private static float _lastX;
}