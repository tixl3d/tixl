using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Provides shared audio level meter (VU meter) drawing functionality.
/// </summary>
internal static class AudioLevelMeter
{
    private const float LedWidthBase = 20f;
    private const float LedSpacingBase = 5f;
    private const float DefaultDecayRate = 2f;
    
    private static float LedWidth => LedWidthBase * T3Ui.UiScaleFactor;
    private static float LedSpacing => LedSpacingBase * T3Ui.UiScaleFactor;
    private static float LedTotalWidth => LedWidth + LedSpacing;

    /// <summary>
    /// Draws an audio level meter with clipping indicator.
    /// </summary>
    internal static void Draw(string label, float currentLevel, ref float smoothedLevel, float decayRate = DefaultDecayRate, float leftPadding = 0f, float rightEdgeX = 0f)
    {
        var (min, max) = PrepareAndGetBounds(label, currentLevel, ref smoothedLevel, decayRate);
        min.X += leftPadding;
        
        var meterEndX = rightEdgeX > 0
            ? rightEdgeX - LedTotalWidth
            : max.X - LedTotalWidth - 10f * T3Ui.UiScaleFactor;
        
        DrawMeterContent(min, max, meterEndX, currentLevel, smoothedLevel);
    }
    
    /// <summary>
    /// Draws an audio level meter where the entire meter (bar + LED) fits within the specified bounds.
    /// </summary>
    internal static void DrawAbsoluteWithinBounds(string label, float currentLevel, ref float smoothedLevel, float decayRate, float leftEdgeX, float rightEdgeX)
    {
        var meterWidth = rightEdgeX - leftEdgeX;
        
        // Position cursor at the left edge (convert from screen to window coordinates)
        var windowPos = ImGui.GetWindowPos();
        ImGui.SetCursorPosX(leftEdgeX - windowPos.X);
        
        var uniqueId = string.IsNullOrEmpty(label) ? "##levelMeter" : $"##{label}Meter";
        ImGui.InvisibleButton(uniqueId, new Vector2(meterWidth, ImGui.GetFrameHeight()));

        smoothedLevel = currentLevel > smoothedLevel 
            ? currentLevel 
            : Math.Max(currentLevel, smoothedLevel - decayRate * ImGui.GetIO().DeltaTime);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        
        // Fit the entire meter (bar + LED) within bounds
        var meterEndX = min.X + meterWidth - LedTotalWidth;
        var ledRightX = min.X + meterWidth;
        DrawMeterContent(min, max, meterEndX, currentLevel, smoothedLevel, ledRightX);
    }
    
    private static (Vector2 min, Vector2 max) PrepareAndGetBounds(string label, float currentLevel, ref float smoothedLevel, float decayRate)
    {
        FormInputs.DrawInputLabel(label);
        
        var uniqueId = string.IsNullOrEmpty(label) ? "##levelMeter" : $"##{label}Meter";
        ImGui.InvisibleButton(uniqueId, new Vector2(-1, ImGui.GetFrameHeight()));

        smoothedLevel = currentLevel > smoothedLevel 
            ? currentLevel 
            : Math.Max(currentLevel, smoothedLevel - decayRate * ImGui.GetIO().DeltaTime);

        return (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }
    
    private static void DrawMeterContent(Vector2 min, Vector2 max, float meterEndX, float currentLevel, float smoothedLevel, float? maxRightEdge = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var meterWidth = meterEndX - min.X;
        var paddedHeight = (max.Y - min.Y) / 3f;
        var topY = min.Y + paddedHeight;
        var bottomY = max.Y - paddedHeight;

        // Gradient bar: green to orange
        dl.AddRectFilledMultiColor(
            new Vector2(min.X, topY),
            new Vector2(min.X + meterWidth, bottomY),
            UiColors.StatusControlled, UiColors.StatusWarning,
            UiColors.StatusWarning, UiColors.StatusControlled);

        // Cover unfilled portion
        var clampedLevel = Math.Min(smoothedLevel, 1f);
        var levelPosition = min.X + meterWidth * clampedLevel;
        dl.AddRectFilled(
            new Vector2(levelPosition, topY),
            new Vector2(min.X + meterWidth, bottomY),
            UiColors.BackgroundHover);

        // Clipping LED indicator
        var ledLeftX = meterEndX + LedSpacing;
        var ledRightX = maxRightEdge ?? (meterEndX + LedSpacing + LedWidth);
        dl.AddRectFilled(
            new Vector2(ledLeftX, topY),
            new Vector2(ledRightX, bottomY),
            currentLevel >= 1f ? UiColors.StatusError : UiColors.BackgroundHover);
    }
}
