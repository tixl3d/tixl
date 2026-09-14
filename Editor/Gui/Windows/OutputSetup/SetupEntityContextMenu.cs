#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The one context menu for a setup entity — identical whether opened from an outliner item or a canvas
/// label: kind-specific extras first, then the common Duplicate / Rename / Delete verbs wherever the kind
/// supports them. Rename opens the outliner item's inline editor (<see cref="OutlinerItem.BeginRename"/>).
/// </summary>
internal static class SetupEntityContextMenu
{
    /// <summary>Attaches the menu to the last drawn item (right-click opens it). Runs every frame per item, so the
    /// body is a cached delegate reading static context rather than a closure per item.</summary>
    public static void DrawForLastItem(SetupEntitySelection selection, Setup setup, SetupEntityKinds kind, Guid id, string name)
    {
        // ContextMenuForItem invokes the body synchronously for the (single) item whose popup is open, so these
        // always hold that item's values.
        _menuSelection = selection;
        _menuSetup = setup;
        _menuKind = kind;
        _menuId = id;
        _menuName = name;
        CustomComponents.ContextMenuForItem(_drawMenuItemsCached, null);
    }

    /// <summary>The menu body, for a popup the caller opened itself (the canvas' pick menu).</summary>
    public static void Draw(SetupEntitySelection selection, Setup setup, SetupEntityKinds kind, Guid id, string name)
    {
        // Right-clicking inside a multi-selection acts on the whole thing. The per-entity actions stay
        // visible but dimmed rather than vanishing, so the menu keeps its shape and it is obvious *why*
        // they can't be used.
        var multi = selection.IsSelected(kind, id) && selection.Count > 1;

        // These menus carry no toggles or icons, so their labels sit flush left.
        CustomComponents.MenuItemsFlushLeft = true;
        CustomComponents.MenuItemsDisabled = multi;
        ImGui.BeginDisabled(multi);

        DrawKindMenuItems(selection, setup, kind, id);

        if (SetupActions.CanDuplicate(kind) && CustomComponents.DrawMenuItem(5, "Duplicate"))
            SetupActions.DuplicateEntity(selection, setup, kind, id);

        if (SetupActions.CanRename(kind) && CustomComponents.DrawMenuItem(3, "Rename"))
            OutlinerItem.BeginRename(selection, kind, id, name);

        ImGui.EndDisabled();
        CustomComponents.MenuItemsDisabled = false;

        // Deleting is the one action that reads the selection rather than the item, so it is offered even
        // from an item that isn't itself deletable.
        if (multi)
        {
            var deletable = SetupActions.CountDeletable(selection);
            if (deletable > 0 && CustomComponents.DrawMenuItem(2, $"Delete {deletable}"))
                SetupActions.DeleteSelection(selection, setup);
        }
        else if (SetupActions.CanDeleteDirectly(kind) && CustomComponents.DrawMenuItem(2, "Delete"))
        {
            SetupActions.DeleteEntity(setup, kind, id);
        }

        CustomComponents.MenuItemsFlushLeft = false;
    }

    private static void DrawMenuItemsForCurrent()
    {
        if (_menuSelection == null || _menuSetup == null)
            return;

        Draw(_menuSelection, _menuSetup, _menuKind, _menuId, _menuName);
    }

    /// <summary>The kind-specific entries, one case per kind, side by side.</summary>
    private static void DrawKindMenuItems(SetupEntitySelection selection, Setup setup, SetupEntityKinds kind, Guid id)
    {
        switch (kind)
        {
            case SetupEntityKinds.FloorPlan:
                var plan = setup.FindFloorPlan(id);
                if (plan == null)
                    break;

                if (CustomComponents.DrawMenuItem(21, "Draw Walls", isEnabled: !plan.IsClosed && plan.Vertices.Count >= 2))
                    SetupOutputView.PendingPlanDrawId = plan.Id;

                CustomComponents.TooltipForLastItem("Continue the run from its last corner on the Board: each click plants a corner and raises a wall.");
                break;

            case SetupEntityKinds.Output:
                var output = setup.FindOutput(id);
                if (output == null)
                    break;

                if (CustomComponents.DrawMenuItem(7, "Add Patch"))
                    SetupActions.AddPatch(selection, setup, output);

                if (CustomComponents.DrawMenuItem(10, "Split into 2×2"))
                    SetupActions.SplitOutput(selection, setup, output, 2, 2);

                if (CustomComponents.DrawMenuItem(11, "Split into 4×4"))
                    SetupActions.SplitOutput(selection, setup, output, 4, 4);

                if (CustomComponents.DrawMenuItem(14, "Clear Inputs", isEnabled: SetupActions.OutputHasInputs(setup, output)))
                    SetupActions.ClearOutputInputs(setup, output);

                CustomComponents.TooltipForLastItem("Disconnects every surface and patch from this output.",
                                                    "The surfaces and their content stay; only the routes into this canvas are removed.");

                // Content fed straight to the output rides a folded-away patch; turning it is the common case of
                // a display mounted on its side, so it is offered here rather than making the patch explicit first.
                if (SetupRelations.TryGetImplicitPatch(output, out var implicitPatch)
                    && CustomComponents.DrawMenuItem(18, "Rotate Content 90°"))
                {
                    SetupActions.RotatePatchContentClockwise(setup, implicitPatch!);
                }

                // No "Bind to" here: an output is bound by dragging it onto a plug, and released from the
                // plug's own menu. One gesture, in the place that shows what is plugged in.
                break;

            case SetupEntityKinds.Plug:
                if (!OutputSetupHandling.TryGetActiveSetup(out _, out var plugMachineConfig))
                    break;

                // Whatever output is bound here can be let go from the plug's side too.
                foreach (var binding in plugMachineConfig.Bindings)
                {
                    if (Plugs.BoundPlugId(binding) != id)
                        continue;

                    var boundOutput = setup.FindOutput(binding.OutputId);
                    if (boundOutput != null && CustomComponents.DrawMenuItem(15, $"Unbind {boundOutput.Name}"))
                    {
                        Plugs.UnbindOutput(plugMachineConfig, boundOutput.Id);
                        break;
                    }
                }

                if (plugMachineConfig.FindStreamPlug(id) != null && CustomComponents.DrawMenuItem(16, "Remove stream"))
                    Plugs.RemoveStream(plugMachineConfig, id);

                break;

            case SetupEntityKinds.ContentSource:
                var source = setup.FindSourceByChildId(id);
                if (source != null && CustomComponents.DrawMenuItem(8, "Add slice"))
                    SetupActions.AddSlice(selection, setup, source);

                break;

            case SetupEntityKinds.Patch:
                if (setup.FindPatch(id, out var turnedOwner) is { } turnedPatch && turnedOwner != null
                    && CustomComponents.DrawMenuItem(17, "Rotate 90°"))
                {
                    SetupActions.RotatePatchClockwise(setup, turnedOwner, turnedPatch);
                }

                CustomComponents.TooltipForLastItem("Turns the patch a quarter clockwise around its centre, shape and picture together.",
                                                    "For a display or LED panel on its side. The Rotation row in its parameters sets it directly.");

                if (CustomComponents.DrawMenuItem(12, "Use on Surface"))
                    SetupActions.PromotePatchToSurface(selection, setup, id);

                CustomComponents.TooltipForLastItem("Turns this patch into a surface with a corner pin.",
                                                    "The quad stays exactly where it is; the surface adds real size, raster and straightening.");
                break;

            case SetupEntityKinds.ReferenceImage:
                var image = setup.FindReferenceImage(id);
                if (image == null)
                    break;

                if (CustomComponents.DrawMenuItem(13, "Trace New Surface"))
                    SetupActions.TraceNewSurface(selection, setup, image);

                // Every root surface not yet traced anywhere can be traced here.
                for (var i = 0; i < setup.Surfaces.Count; i++)
                {
                    var candidate = setup.Surfaces[i];
                    if (candidate.Trace != null || candidate.ParentId != Guid.Empty)
                        continue;

                    if (CustomComponents.DrawMenuItem(100 + i, $"Trace {candidate.Name} Here"))
                        SetupActions.TraceSurfaceOnImage(selection, setup, candidate, image);
                }

                break;

            case SetupEntityKinds.Surface:
                var surface = setup.FindSurface(id);
                if (surface == null)
                    break;

                if (surface.Kind == Surface.Kinds.Physical && setup.FindFloorPlanOf(surface.Id, out _) == null)
                {
                    if (CustomComponents.DrawMenuItem(19, "Start Floor Plan from Bottom Edge"))
                        SetupActions.StartFloorPlanFromSurface(selection, setup, surface, asFloor: false);

                    if (CustomComponents.DrawMenuItem(20, "Use as Floor of New Plan"))
                        SetupActions.StartFloorPlanFromSurface(selection, setup, surface, asFloor: true);
                }

                if (CustomComponents.DrawMenuItem(4, "Add region"))
                    SetupActions.AddSubRegion(selection, setup, surface);

                // Only meaningful once something is shown here — there's no aspect to match otherwise.
                if (surface.SliceId != Guid.Empty && CustomComponents.DrawMenuItem(9, "Adjust aspect to slice"))
                    SurfaceMetrics.MatchSurfaceToSliceAspect(setup, surface);

                if (CustomComponents.DrawMenuItem(6, "Clear content inputs"))
                    SetupActions.ClearContentInputs(surface.Id);

                if (SetupActions.HasOwnPin(surface) && CustomComponents.DrawMenuItem(17, "Follow parent's pin"))
                    SetupActions.ClearOwnPin(setup, surface.Id);

                CustomComponents.TooltipForLastItem("Drops this region's own corner pin.",
                                                    "It goes back to riding its parent's pin, following the parent wherever it is aimed.");

                break;
        }
    }

    private static readonly Action _drawMenuItemsCached = DrawMenuItemsForCurrent;
    private static SetupEntitySelection? _menuSelection;
    private static Setup? _menuSetup;
    private static SetupEntityKinds _menuKind;
    private static Guid _menuId;
    private static string _menuName = string.Empty;
}
