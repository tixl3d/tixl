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
    public readonly record struct Result(bool HasHit, TKind Kind, Guid Id, bool LeftClicked, bool MenuRequested);

    /// <param name="isBackground">A large area target (a card) that only picks where no foreground target
    /// (a label, a handle chip) is under the cursor — so a label on a card never cycles to the card.</param>
    public void AddTarget(TKind kind, Guid id, Vector2 min, Vector2 max, bool isBackground = false)
    {
        _targets.Add((kind, id, min, max, isBackground));
    }

    /// <summary>Whether <paramref name="id"/> was the top of the stack under the cursor at the last resolve —
    /// i.e. the one a click would act on. Used to brighten that label.</summary>
    public bool IsPicked(Guid id)
    {
        return id != Guid.Empty && id == _pickedId;
    }

    /// <summary>
    /// Hit-tests the collected targets, cycling to the entry after <paramref name="current"/> so repeated
    /// clicks on a stack walk it. Returns the picked entry and whether this frame carried a fresh left-click or
    /// a right-click-without-drag (a menu request). Clears the targets for the next frame.
    /// </summary>
    public Result Resolve(Guid current)
    {
        _underMouse.Clear();
        _backgroundUnderMouse.Clear();
        var mouse = ImGui.GetMousePos();
        foreach (var (kind, id, min, max, isBackground) in _targets)
        {
            if (mouse.X < min.X || mouse.X > max.X || mouse.Y < min.Y || mouse.Y > max.Y)
                continue;

            if (isBackground)
                _backgroundUnderMouse.Add((kind, id));
            else
                _underMouse.Add((kind, id));
        }

        _targets.Clear();
        if (_underMouse.Count == 0)
            _underMouse.AddRange(_backgroundUnderMouse);

        // Not over any label, or the pointer is busy with a handle/button — nothing to pick.
        if (_underMouse.Count == 0 || ImGui.IsAnyItemHovered())
        {
            _pickedId = Guid.Empty;
            return default;
        }

        var index = _underMouse.FindIndex(u => u.Id == current); // -1 (not in stack) → first
        var (pickKind, pickId) = _underMouse[(index + 1) % _underMouse.Count];
        _pickedId = pickId;

        var leftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        // Right release without a drag — a right-drag pans the canvas, and firing on press would open the menu
        // at the start of every pan that happens to begin over a label.
        var wasDraggingRight = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Length() > UserSettings.Config.ClickThreshold;
        var menuRequested = ImGui.IsMouseReleased(ImGuiMouseButton.Right) && !wasDraggingRight;

        return new Result(true, pickKind, pickId, leftClicked, menuRequested);
    }

    private readonly List<(TKind Kind, Guid Id, Vector2 Min, Vector2 Max, bool IsBackground)> _targets = [];
    private readonly List<(TKind Kind, Guid Id)> _underMouse = [];
    private readonly List<(TKind Kind, Guid Id)> _backgroundUnderMouse = [];
    private Guid _pickedId;
}
