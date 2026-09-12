#nullable enable
using ImGuiNET;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Label chips and the pick pass shared by every canvas: hit-testing, selection dispatch and the context menu.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>
    /// A name chip centred on an entity's quad — a surface, a region, or a slice — doubling as its pick/grab
    /// area. Registers the chip with <see cref="_picker"/> so a click can resolve to this entity, and
    /// styles by selection/hover so the same label reads the same in the tree and on the canvas.
    /// </summary>
    /// <param name="pickable">False where the frame itself is the pick target and the chip only names it and shows its state.</param>
    private void DrawEntityLabel(ImDrawListPtr dl, SetupEntitySelection.EntityKinds kind, ReadOnlySpan<Vector2> screenQuad, Guid id, string name, bool isSelected,
                                 float emphasis, float pulse = 0f, bool pickable = true)
    {
        if (string.IsNullOrEmpty(name) || emphasis <= 0.01f)
            return;

        var rect = CornerPinHandles.GetCenteredLabelRect(screenQuad, name);
        if (pickable)
            _picker.AddTarget(kind, id, rect.Min, rect.Max);

        var alpha = (_picker.IsPicked(id) ? 1f : 0.9f) * emphasis;
        var text = (isSelected ? SetupColors.ForKind(kind) : SetupColors.LabelFor(kind)).Fade(alpha);
        var background = UiColors.BackgroundFull.Fade(0.3f * alpha);

        // Pulls the chip toward the selected look while pulsing, so the label answers the hover like the outline.
        text = PulseColor(text, pulse);
        background = PulseColor(background, pulse);
        CornerPinHandles.DrawLabelChip(dl, rect, name, text, background);
    }

    /// <summary>Mixes <paramref name="baseColor"/> toward the selection highlight by the pulse amount (see
    /// <see cref="FrameStats.GetPulse"/>) — the shared way a hovered frame's outline/label/fill light up.</summary>
    private static T3.Core.DataTypes.Vector.Color PulseColor(T3.Core.DataTypes.Vector.Color baseColor, float pulse)
    {
        // Toward white and opaque, never toward a status hue: a hovered thing stays what it is, only louder.
        return pulse <= 0.001f ? baseColor : T3.Core.DataTypes.Vector.Color.Mix(baseColor, UiColors.ForegroundFull, pulse * 0.5f);
    }

    /// <summary>
    /// One pick pass for every labeled frame on the canvas — surfaces, regions, slices. Left-click selects the
    /// label under the cursor (cycling through a stack on repeated clicks), right-click opens that entity's
    /// context menu, and the picked label pulses. Selection and menu dispatch by the target's kind; everything
    /// else — hit-test, overlap cycling, isolate gating, the right-drag guard — is identical across kinds.
    /// </summary>
    private void ResolvePicking(Setup setup, SetupEntitySelection? selection)
    {
        // Cycle relative to whatever is currently the subject, so clicking a stack walks it. The primary
        // selection is that subject for either kind (the focused surface, or the selected slice).
        var current = selection != null && selection.TryResolve(setup, out _, out var primaryId) ? primaryId : Guid.Empty;
        var hit = _picker.Resolve(current);

        if (hit.HasHit)
        {
            FrameStats.PulseItemWithId(hit.Id);

            // Isolate takes selection off the canvas: only the focused frame's label still acts (so it can move
            // and its menu opens); the others are inert until picked in the sidebar. (Slices aren't isolated.)
            var canPick = !_isolate || hit.Id == _shownSurfaceId;

            // A Board press arms the card under the cursor — unless the pick resolves to something drawn on that
            // card (a region's or traced quad's label), which wins over the card beneath it.
            if (_boardGrabScreen != null && hit.Id != _boardGrabId)
            {
                _boardGrabScreen = null;
                _boardGrabOnSelected = false;
            }

            // A drag on the selected region's label moves it, so that press mustn't also count as a pick — nor
            // does a Board press that keeps the selection for a group drag.
            if (canPick && hit.LeftClicked && _gesture.Kind is not (GestureKinds.RegionMove or GestureKinds.SurfaceMove or GestureKinds.ContentPan) && !_boardGrabOnSelected)
            {
                SelectPicked(selection, hit.Kind, hit.Id);

                // Arm the held-grab: if the button stays down on this surface's label, its move starts next
                // frame — select-and-drag in one gesture. Plain presses only; a modifier press is a
                // selection edit, not a grab.
                var io = ImGui.GetIO();
                if (hit.Kind is SetupEntitySelection.EntityKinds.Surface or SetupEntitySelection.EntityKinds.Patch && !io.KeyCtrl && !io.KeyShift)
                    _labelGrabScreen = ImGui.GetMousePos();
            }

            if (canPick && hit.MenuRequested)
            {
                // Right-click selects too, so the menu always acts on what's under the cursor.
                SelectPicked(selection, hit.Kind, hit.Id);
                _menuKind = hit.Kind;
                _menuId = hit.Id;
                ImGui.OpenPopup(PickMenuId);
            }
        }

        if (ImGui.BeginPopup(PickMenuId))
        {
            DrawPickMenu(setup, selection);
            ImGui.EndPopup();
        }

        // Empty Board: a right-click offers what the Board holds that no outliner column lists any more —
        // reference images and props — plus a surface, so a venue can be started without leaving the canvas.
        if (!hit.HasHit && selection != null && ShowsBoard
            && ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered()
            && ImGui.IsMouseReleased(ImGuiMouseButton.Right)
            && ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Length() <= UserSettings.Config.ClickThreshold)
        {
            ImGui.OpenPopup(BoardMenuId);
        }

        if (ImGui.BeginPopup(BoardMenuId))
        {
            if (selection != null)
            {
                if (CustomComponents.DrawMenuItem(1, "Add Surface"))
                    SetupActions.AddSurface(selection);

                if (CustomComponents.DrawMenuItem(2, "Add Reference Image"))
                    SetupActions.AddReferenceImage(selection);

                CustomComponents.TooltipForLastItem("Adds an empty image card; pick its photo in the Parameter window.", "Or drop an image file onto the Board.");

                if (CustomComponents.DrawMenuItem(3, "Add Prop"))
                    SetupActions.AddProp(selection);
            }

            ImGui.EndPopup();
        }

        // Ctrl+D duplicates the primary selection, matching the menu entry — any duplicable kind, not just surfaces.
        if (selection != null
            && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)
            && ImGui.GetIO().KeyCtrl
            && ImGui.IsKeyPressed(ImGuiKey.D, false)
            && selection.TryResolve(setup, out var duplicateKind, out var duplicateId)
            && SetupActions.CanDuplicate(duplicateKind))
        {
            SetupActions.DuplicateEntity(selection, setup, duplicateKind, duplicateId);
        }
    }

    private void SelectPicked(SetupEntitySelection? selection, SetupEntitySelection.EntityKinds kind, Guid id)
    {
        if (selection != null)
        {
            // Shift toggles, plain replaces. Ctrl toggles too — except on the Board, where it is the push-through
            // into nested regions and the click it ends in is a plain pick.
            var io = ImGui.GetIO();
            if (io.KeyShift || (io.KeyCtrl && _editMode != EditModes.Board))
                selection.Toggle(kind, id);
            else
                selection.Select(kind, id);
        }

    }

    private void DrawPickMenu(Setup setup, SetupEntitySelection? selection)
    {
        if (selection == null)
            return;

        // Same menu as the sidebar row — the shared body keeps the two from drifting apart. Rename opens
        // the sidebar row's inline editor.
        var name = SetupActions.NameForEntity(_menuKind, _menuId);

        _entityItem.DrawContextMenuItems(selection, setup, _menuKind, _menuId, name);
    }
}
