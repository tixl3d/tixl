#nullable enable
using ImGuiNET;

namespace T3.Editor.Gui.Help;

/// <summary>
/// A tiny per-frame broker that decouples "what is currently hovered" from the <see cref="Windows.HelpWindow"/>.
/// The graph (and later the symbol library, browser, and <c>[HelpUiID]</c> elements) records the topic the
/// mouse is over; the Help window reads it while in its follow-selection state.
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
        _operatorSymbolId = symbolId;
        _frameSet = ImGui.GetFrameCount();
    }

    /// <summary>The operator symbol hovered this frame or the previous one, or false if the hover is stale.</summary>
    internal static bool TryGetOperator(out Guid symbolId)
    {
        if (_operatorSymbolId != Guid.Empty && ImGui.GetFrameCount() - _frameSet <= 1)
        {
            symbolId = _operatorSymbolId;
            return true;
        }

        symbolId = Guid.Empty;
        return false;
    }

    private static Guid _operatorSymbolId;
    private static int _frameSet = int.MinValue;
}
