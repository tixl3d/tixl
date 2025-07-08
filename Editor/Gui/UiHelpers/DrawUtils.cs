using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// A collection of helper and debug function for IMGUI development
/// </summary>
[SuppressMessage("ReSharper", "MemberCanBeInternal")]
public static class DrawUtils
{
    public static ImDrawListPtr OutlinedRect(ref ImDrawListPtr drawList, Vector2 position, Vector2 size, uint fill, uint outline, float cornerRadius = 4)
    {
        drawList.AddRectFilled(position, position + size, fill, cornerRadius);
        drawList.AddRect(position, position + size, outline, cornerRadius);
        return drawList;
    }

    public static ImDrawListPtr OutlinedRect(ref ImDrawListPtr drawList, Vector2 position, Vector2 size, Color fill, Color outline, float cornerRadius = 4)
    {
        drawList.AddRectFilled(position, position + size, fill, cornerRadius);
        drawList.AddRect(position, position + size, outline, cornerRadius);
        return drawList;
    }

    public static void DebugRect(ImRect screenRect, string label = "")
    {
        DebugRect(screenRect.Min,screenRect.Max, Color.Green, label);
    }

    public static void DebugContentRegion(string title="")
    {
        DebugRect(GetContentRegionArea(),title);
    }

    /// <summary>
    /// Draws an overlay rectangle in screen space
    /// </summary>
    public static void DebugRect(Vector2 screenMin, Vector2 screenMax, string label = "")
    {
        DebugRect(screenMin,screenMax, Color.Green, label);
    }

    public static void DebugRect(Vector2 screenMin, Vector2 screenMax, Color color, string label = "")
    {
        var overlayDrawList = ImGui.GetForegroundDrawList();
        overlayDrawList.AddRect(screenMin, screenMax, color);
        if (string.IsNullOrEmpty(label))
            return;
            
        overlayDrawList.AddText(new Vector2(screenMin.X, screenMax.Y), color, label);
    }

    /// <summary>
    /// Draws an outline of the current (last) Imgui item
    /// </summary>
    public static void DebugItemRect(string label = "", uint color = 0xff20ff80)
    {
        if (T3Ui.ItemRegionsVisible)
            DebugRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), new Color(color), label);
    }
        

    public static ImRect GetContentRegionArea()
    {
        return new ImRect(ImGui.GetWindowContentRegionMin() + ImGui.GetWindowPos(),
                          ImGui.GetWindowContentRegionMax() + ImGui.GetWindowPos());
    }
        
        
        
    // This has to be called on open
    public static void DisableImGuiKeyboardNavigation()
    {
        // Keep navigation setting to restore after window gets closed
        _keepNavEnableKeyboard = (ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != ImGuiConfigFlags.None;
        ImGui.GetIO().ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;
    }

    public static void RestoreImGuiKeyboardNavigation()
    {
        if (_keepNavEnableKeyboard)
            ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
    }
    private static bool _keepNavEnableKeyboard;

    public static Color RandomColorForHash(int channelHash)
    {
        var foreGroundBrightness = UiColors.ForegroundFull.V;
        var randomHue = (Math.Abs(channelHash) % 357) / 360f;
        var randomSaturation = (channelHash % 13) / 13f / 3f + 0.4f;
        var randomChannelColor = Color.FromHSV(randomHue, randomSaturation, foreGroundBrightness, 1);
        return randomChannelColor;
    }

    /// <summary>
    /// Ensures the string being modified starts with the given string
    /// </summary>
    /// <param name="start">The desired start of the string</param>
    /// <param name="valueEnforced">the string being modified</param>
    /// <param name="replaceStart">If true, the start of the string will be replaced. If false, the start will be prepended</param>
    /// <returns>True if the string was modified by this method</returns>
    public static bool EnforceStringStart(string start, ref string valueEnforced, bool replaceStart)
    {
        if (valueEnforced.StartsWith(start))
            return false;

        if (replaceStart)
        {
            valueEnforced = valueEnforced.Length <= start.Length
                                ? start
                                : start + valueEnforced[start.Length..];
        }
        else
        {
            valueEnforced = start + valueEnforced;
        }

        return true;
    }

    public static void DrawOverlayLine(ImDrawListPtr drawList, float opacity, Vector2 p1, Vector2 p2,  Vector2 pMin, Vector2 pMax)
    {
        var padding = new Vector2(3, 2);
        var size = pMax - pMin - padding;
        drawList.AddLine(pMin + p1 * size + padding,
                         pMin + p2 * size + padding,
                         UiColors.StatusWarning.Fade(opacity), 3);

    }
}