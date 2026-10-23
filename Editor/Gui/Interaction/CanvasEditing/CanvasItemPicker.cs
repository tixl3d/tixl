#nullable enable
using System;
using System.Collections.Generic;
using ImGuiNET;
using T3.Editor.Gui.UiHelpers;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Interaction.CanvasEditing;

/// <summary>
/// The pointer-picking scaffolding shared by every labeled thing on a <see cref="ScalableCanvas"/> — surfaces,
/// slices, and (later) the points and lines of geometry operators. It owns only the parts that don't care what
/// is being edited: collecting screen-space pick rects for the frame, hit-testing them, cycling through a stack
/// under the cursor on repeated clicks, and reporting a fresh left-click or a right-click-menu request.
/// <para>A stack under the cursor has two answers. A press (and the drag it may become) takes a selected item if
/// one is there, so a selection is never lost to whatever is drawn over it; otherwise the topmost. A click —
/// a press released without moving — walks the stack: it picks the item after the current one.</para>
/// <para>Everything downstream of a hit — what "select" means, which context menu opens, isolate rules — stays
/// with the caller, which knows the domain. <typeparamref name="TKind"/> is the caller's own discriminator,
/// carried through so the caller can dispatch on it.</para>
/// <para>Usage per frame: <see cref="AddTarget"/> each label as it's drawn (and style it with
/// <see cref="IsPicked"/>), then call <see cref="Resolve"/> once at the end. Resolve clears the targets for
/// the next frame; <see cref="IsPicked"/> reflects the previous resolve, so the label styling lags by a frame
/// — imperceptible, and the same lag the rest of the canvas hover uses.</para>
/// </summary>
internal sealed class CanvasItemPicker<TKind> where TKind : struct
{
    /// <param name="Kind">With <see cref="Id"/>: what a press under the cursor takes and a drag moves.</param>
    /// <param name="IsSelected">Whether that item was added as selected — a press on it keeps the selection.</param>
    /// <param name="NextKind">With <see cref="NextId"/>: what a click walking the stack selects next.</param>
    public readonly record struct Result(bool HasHit, TKind Kind, Guid Id, bool IsSelected, TKind NextKind, Guid NextId,
                                         bool LeftClicked, bool MenuRequested);

    /// <summary>Targets added later are drawn over earlier ones; the stack under the cursor runs topmost first.</summary>
    /// <param name="isBackground">A large area target (a card) that only picks where no foreground target
    /// (a label, a handle chip) is under the cursor — so a label on a card never cycles to the card.</param>
    public void AddTarget(TKind kind, Guid id, Vector2 min, Vector2 max, bool isBackground = false, bool isSelected = false)
    {
        _targets.Add((kind, id, min, max, isBackground, isSelected));
    }

    /// <summary>Whether <paramref name="id"/> is what a press under the cursor took at the last resolve — the
    /// item a drag moves. Used to brighten that label and to hand the press to its move.</summary>
    public bool IsPicked(Guid id)
    {
        return id != Guid.Empty && id == _pickedId;
    }

    /// <summary>
    /// Hit-tests the collected targets: what a press takes (a selected item under the cursor, else the topmost)
    /// and what a click selects (the entry after <paramref name="current"/>, so repeated clicks walk the stack).
    /// Also reports whether this frame carried a fresh left-click or a right-click-without-drag (a menu request).
    /// Clears the targets for the next frame.
    /// </summary>
    public Result Resolve(Guid current)
    {
        _underMouse.Clear();
        _backgroundUnderMouse.Clear();
        var mouse = ImGui.GetMousePos();
        for (var targetIndex = _targets.Count - 1; targetIndex >= 0; targetIndex--)
        {
            var (kind, id, min, max, isBackground, isSelected) = _targets[targetIndex];
            if (mouse.X < min.X || mouse.X > max.X || mouse.Y < min.Y || mouse.Y > max.Y)
                continue;

            if (isBackground)
                _backgroundUnderMouse.Add((kind, id, isSelected));
            else
                _underMouse.Add((kind, id, isSelected));
        }

        _targets.Clear();

        // Not over any target, or the pointer is busy with a handle/button — nothing to pick.
        if ((_underMouse.Count == 0 && _backgroundUnderMouse.Count == 0) || ImGui.IsAnyItemHovered())
        {
            _pickedId = Guid.Empty;
            return default;
        }

        // A selected card under a label still takes the press, so its drag isn't lost to what is drawn over it.
        if (!TryFindSelected(_underMouse, out var grab) && !TryFindSelected(_backgroundUnderMouse, out grab))
            grab = _underMouse.Count > 0 ? _underMouse[0] : _backgroundUnderMouse[0];

        var stack = _underMouse.Count > 0 ? _underMouse : _backgroundUnderMouse;
        var index = -1; // -1 (not in stack) → topmost
        for (var i = 0; i < stack.Count; i++)
        {
            if (stack[i].Id == current)
            {
                index = i;
                break;
            }
        }

        var next = stack[(index + 1) % stack.Count];
        _pickedId = grab.Id;

        var leftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        // Right release without a drag — a right-drag pans the canvas, and firing on press would open the menu
        // at the start of every pan that happens to begin over a label.
        var wasDraggingRight = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Length() > UserSettings.Config.ClickThreshold;
        var menuRequested = ImGui.IsMouseReleased(ImGuiMouseButton.Right) && !wasDraggingRight;

        return new Result(true, grab.Kind, grab.Id, grab.IsSelected, next.Kind, next.Id, leftClicked, menuRequested);
    }

    private static bool TryFindSelected(List<(TKind Kind, Guid Id, bool IsSelected)> hits, out (TKind Kind, Guid Id, bool IsSelected) selected)
    {
        for (var i = 0; i < hits.Count; i++)
        {
            if (!hits[i].IsSelected)
                continue;

            selected = hits[i];
            return true;
        }

        selected = default;
        return false;
    }

    private readonly List<(TKind Kind, Guid Id, Vector2 Min, Vector2 Max, bool IsBackground, bool IsSelected)> _targets = [];
    private readonly List<(TKind Kind, Guid Id, bool IsSelected)> _underMouse = [];
    private readonly List<(TKind Kind, Guid Id, bool IsSelected)> _backgroundUnderMouse = [];
    private Guid _pickedId;
}
