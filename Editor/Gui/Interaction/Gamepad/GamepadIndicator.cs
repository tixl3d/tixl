#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.IoServices;

namespace T3.Editor.Gui.Interaction.Gamepad;

/// <summary>
/// Decides who a connected game controller drives: operators like [Gamepad], the camera of the output (see
/// <see cref="Camera.GamepadCamera"/>), or nothing. The menu bar shows a controller icon only while one is connected;
/// it flashes on input and cycles the usage when clicked.
/// </summary>
internal static class GamepadIndicator
{
    /** Call once per frame on the main thread, after SDL events were pumped. */
    internal static void Update()
    {
        SdlGamepads.Update();

        // Applied every frame rather than on change, so the setting holds even while the menu bar is hidden
        SdlGamepads.IsAvailableToOperators = UserSettings.Config.GamepadUsage == UserSettings.GamepadUsages.Operators;
    }

    internal static void Draw()
    {
        if (SdlGamepads.Count == 0)
            return;

        var usage = UserSettings.Config.GamepadUsage;
        var scale = T3Ui.UiScaleFactor;
        var height = ImGui.GetFrameHeight();
        var size = new Vector2(MathF.Round(GlyphWidth * scale), height);

        ImGui.SameLine(0, AppMenuBar.AppBarSpacingX);
        var pos = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("##gamepadUsage", size))
            CycleUsage();

        var msSinceActivity = Environment.TickCount64 - SdlGamepads.LastActivityTicksMs;
        var flash = Math.Clamp(1f - msSinceActivity / FlashDurationMs, 0f, 1f);
        var color = usage switch
                        {
                            UserSettings.GamepadUsages.Operators => UiColors.StatusAutomated,
                            UserSettings.GamepadUsages.Editor    => UiColors.StatusControlled,
                            _                                    => UiColors.TextMuted,
                        };
        var opacity = usage == UserSettings.GamepadUsages.Off
                          ? RestingOpacity
                          : RestingOpacity + (1 - RestingOpacity) * flash;

        DrawGlyph(ImGui.GetWindowDrawList(), new Vector2(pos.X, pos.Y + height * 0.5f), scale, color.Fade(opacity));

        if (ImGui.IsItemHovered())
            DrawTooltip(usage);
    }

    private static void CycleUsage()
    {
        var next = UserSettings.Config.GamepadUsage switch
                       {
                           UserSettings.GamepadUsages.Operators => UserSettings.GamepadUsages.Editor,
                           UserSettings.GamepadUsages.Editor    => UserSettings.GamepadUsages.Off,
                           _                                    => UserSettings.GamepadUsages.Operators,
                       };
        UserSettings.Config.GamepadUsage = next;
        UserSettings.Save();
        ActionToast.Show(GetUsageLabel(next));
    }

    private static void DrawTooltip(UserSettings.GamepadUsages usage)
    {
        ImGui.BeginTooltip();
        for (var index = 0; index < SdlGamepads.Count; index++)
        {
            ImGui.TextUnformatted(SdlGamepads.GetName(index));
        }

        ImGui.TextUnformatted(GetUsageLabel(usage));
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted("Click to switch between operators, camera and off.");
        ImGui.PopStyleColor();
        ImGui.EndTooltip();
    }

    private static string GetUsageLabel(UserSettings.GamepadUsages usage)
    {
        return usage switch
                   {
                       UserSettings.GamepadUsages.Operators => "{Game controller drives operators}",
                       UserSettings.GamepadUsages.Editor    => "{Game controller steers the camera}",
                       _                                    => "{Game controller is off}",
                   };
    }

    /// <summary>
    /// A small controller outline with a d-pad and two buttons; the icon atlas has no glyph for it.
    /// </summary>
    private static void DrawGlyph(ImDrawListPtr drawList, Vector2 leftCenter, float scale, Color color)
    {
        var width = MathF.Round(GlyphWidth * scale);
        var height = MathF.Round(GlyphHeight * scale);
        var min = new Vector2(MathF.Round(leftCenter.X), MathF.Round(leftCenter.Y - height * 0.5f));
        var max = min + new Vector2(width, height);
        var thickness = MathF.Max(1, MathF.Round(scale));
        drawList.AddRect(min, max, color, height * 0.45f, ImDrawFlags.None, thickness);

        var center = (min + max) * 0.5f;
        var padCenter = new Vector2(MathF.Round(min.X + width * 0.28f), MathF.Round(center.Y));
        var arm = MathF.Round(2 * scale);
        drawList.AddRectFilled(padCenter - new Vector2(arm, thickness * 0.5f), padCenter + new Vector2(arm + thickness, thickness * 0.5f + thickness), color);
        drawList.AddRectFilled(padCenter - new Vector2(thickness * 0.5f, arm), padCenter + new Vector2(thickness * 0.5f + thickness, arm + thickness), color);

        var buttonRadius = 1.2f * scale;
        drawList.AddCircleFilled(new Vector2(min.X + width * 0.68f, center.Y + 1.5f * scale), buttonRadius, color);
        drawList.AddCircleFilled(new Vector2(min.X + width * 0.80f, center.Y - 1.5f * scale), buttonRadius, color);
    }

    private const float GlyphWidth = 18;
    private const float GlyphHeight = 11;
    private const float FlashDurationMs = 400;
    private const float RestingOpacity = 0.5f;
}
