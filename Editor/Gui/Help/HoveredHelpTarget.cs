#nullable enable
using ImGuiNET;

namespace T3.Editor.Gui.Help;

/// <summary>
/// A tiny per-frame broker that decouples "what is currently hovered" from the <see cref="Windows.HelpWindow"/>.
/// The graph, the symbol library/browser, and documentation affordances record the topic the mouse is over;
/// the Help window previews it while its hover mode is enabled.
/// </summary>
/// <remarks>
/// The recorded value carries the frame it was set on so the read tolerates either draw order between the
/// producer (e.g. the graph) and the Help window — a one-frame staleness is imperceptible and avoids a
/// hard ordering dependency in <see cref="Windows.Layouts.WindowManager"/>.
/// </remarks>
internal static class HoveredHelpTarget
{
    /// <summary>Records the operator the mouse is hovering as the help topic for this frame.</summary>
    internal static void SetOperator(Guid symbolId)
    {
        _hoveredTopic = HelpTopic.ForOperator(symbolId);
        _frameSet = ImGui.GetFrameCount();
    }

    /// <summary>Records the <c>ui:</c> topic the mouse is hovering (a markdown link or documentation icon).</summary>
    internal static void SetTopic(string keyOrId)
    {
        _hoveredTopic = HelpTopic.ForUiTopic(keyOrId);
        _frameSet = ImGui.GetFrameCount();
    }

    /// <summary>The topic hovered this frame or the previous one, or false if the hover is stale.</summary>
    internal static bool TryGetHovered(out HelpTopic topic)
    {
        if (!_hoveredTopic.IsEmpty && ImGui.GetFrameCount() - _frameSet <= 1)
        {
            topic = _hoveredTopic;
            return true;
        }

        topic = default;
        return false;
    }

    private static HelpTopic _hoveredTopic;
    private static int _frameSet = int.MinValue;
}
