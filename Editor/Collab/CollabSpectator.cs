#nullable enable
using ImGuiNET;
using T3.Editor.Gui;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Collab;

/// <summary>
/// Follows one remote participant's cursor on the graph canvas: the view scrolls whenever their pointer
/// nears the edge, so what they are working on stays in sight. Started from the participant's context
/// menu in the Collab window and ended from there, with Escape, or when they or the session go away.
/// </summary>
internal static class CollabSpectator
{
    public static bool IsSpectating => _peerId >= 0;

    public static bool IsSpectatingPeer(int peerId) => _peerId == peerId;

    public static void Start(int peerId) => _peerId = peerId;

    public static void Stop() => _peerId = NoPeer;

    /// <summary>Called by the graph overlay each frame, after the canvas was updated.</summary>
    public static void Update(CollabManager manager, ScalableCanvas canvas, Guid compositionId, ImDrawListPtr drawList)
    {
        if (_peerId < 0)
            return;

        if (!manager.Roster.TryGetValue(_peerId, out var name) || manager.GetPresence(_peerId) is not { } display)
        {
            Stop();
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape) && !ImGui.IsAnyItemActive())
        {
            Stop();
            return;
        }

        var isInSameComposition = display.CompositionId == compositionId;
        if (isInSameComposition && display.IsHoveringCanvas)
        {
            // Scrolls only once the pointer nears the edge, so the view stays calm while they work in the middle.
            var margin = MathF.Min(canvas.WindowSize.X, canvas.WindowSize.Y) * EdgeMarginFraction;
            canvas.ScrollToMakeAreaVisible(new ImRect(display.CursorOnCanvas, display.CursorOnCanvas), margin);
        }

        DrawBanner(drawList, canvas, name, display.AccentColor, isInSameComposition);
    }

    private static void DrawBanner(ImDrawListPtr drawList, ScalableCanvas canvas, string name, Vector4 accentColor, bool isInSameComposition)
    {
        var label = isInSameComposition
                        ? $"Following {name}  ·  Esc to stop"
                        : $"Following {name}  ·  in another operator  ·  Esc to stop";

        var scale = T3Ui.UiScaleFactor;
        var padding = new Vector2(10, 4) * scale;
        var size = ImGui.CalcTextSize(label) + padding * 2;
        var min = new Vector2(canvas.WindowPos.X + (canvas.WindowSize.X - size.X) * 0.5f, canvas.WindowPos.Y + 12 * scale);
        var rounding = size.Y * 0.5f;

        drawList.AddRectFilled(min, min + size, CollabColors.ToUint(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.92f)), rounding);
        drawList.AddText(min + padding, CollabColors.ToUint(CollabColors.Ink), label);
    }

    private const int NoPeer = -1;
    private const float EdgeMarginFraction = 0.25f;

    private static int _peerId = NoPeer;
}
