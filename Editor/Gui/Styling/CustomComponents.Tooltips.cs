using ImGuiNET;
using T3.Core.DataTypes.Vector;

namespace T3.Editor.Gui.Styling;

internal static partial class CustomComponents
{
    /// <summary>
    /// This needs to be called once a frame
    /// </summary>
    public static void BeginFrame()
    {
        var frameDuration = 1 / ImGui.GetIO().Framerate;
        if (FrameStats.Last.SomethingWithTooltipHovered)
        {
            _toolTipHoverDelay -= frameDuration;
            _timeSinceTooltipHover = 0;
        }
        else
        {
            _timeSinceTooltipHover += frameDuration;
            if (_timeSinceTooltipHover > 0.2)
                _toolTipHoverDelay = 0.6f;
        }
    }    
    
    public static void TooltipForLastItem(Color color, string message, string additionalNotes = null, bool useHoverDelay = true)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;

        FrameStats.Current.SomethingWithTooltipHovered = true;
        if (!useHoverDelay)
            _toolTipHoverDelay = 0;

        if (_toolTipHoverDelay > 0)
            return;

        BeginTooltip();
        ImGui.TextColored(color, message);
        if (!string.IsNullOrEmpty(additionalNotes))
        {
            ImGui.TextColored(color.Fade(0.7f), additionalNotes);
        }

        ImGui.PopTextWrapPos();

        EndTooltip();
    }

    /** Should be used for drawing consistently styled tooltips */
    public static bool BeginTooltip(float wrapPos = 300)
    {
        var isHovered = false;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
        isHovered = ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(wrapPos);
        return isHovered;
    }

    public static void EndTooltip()
    {
        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    public static void TooltipForLastItem(Action drawContent, bool useHoverDelay = true)
    {
        if (!ImGui.IsItemHovered())
            return;

        FrameStats.Current.SomethingWithTooltipHovered = true;
        if (!useHoverDelay)
            _toolTipHoverDelay = 0;

        if (_toolTipHoverDelay > 0)
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
        ImGui.BeginTooltip();

        drawContent.Invoke();

        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    public static void TooltipForLastItem(string message, string additionalNotes = null, bool useHoverDelay = true)
    {
        TooltipForLastItem(UiColors.Text, message, additionalNotes, useHoverDelay);
    }

    private static double _toolTipHoverDelay;
    private static double _timeSinceTooltipHover;
}