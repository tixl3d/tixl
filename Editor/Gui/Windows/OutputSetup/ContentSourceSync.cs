#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The op ↔ source relationship: keeps the setup's <see cref="ContentSource"/> list 1:1 with the ops that supply
/// pixels, and is the setup side's way into the graph — placing, finding, naming and revealing a send op.
/// <para>Bound to the op's <b>SymbolChild</b> — the durable graph entity — not to a live instance. Instances
/// come and go with hot-reloads and with whichever part of the graph happens to be instantiated, so "no live
/// send op" only means "no pixels this frame". A source is removed only once its child is confirmed *gone* from
/// a symbol we can actually see, which is what makes deleting the op cascade to its slices and to every
/// surface showing them.</para>
/// </summary>
internal static class ContentSourceSync
{
    /// <summary>Runs the sync against the published active setup. Called from the frame loop, so the
    /// source list stays in step with the graph whether or not any output UI is open.</summary>
    public static void UpdateFrame()
    {
        var setup = ActiveSetup.Current;
        if (setup != null)
            Update(setup);
    }

    /// <summary>
    /// Drops a <c>SendToOutput</c> op into the focused composition, selects it, and frames the view on it. When a
    /// texture-outputting op is selected it lands to its right and is wired straight in, so the feed shows up in
    /// the setup at once (the CONTENT item appears next frame, once the sync adopts it).
    /// </summary>
    public static void AddContentSend(SetupEntitySelection selection)
    {
        var projectView = ProjectView.Focused;
        var composition = projectView?.CompositionInstance;
        if (projectView == null || composition == null)
            return;

        if (!composition.Symbol.TryGetSymbolUi(out var compositionUi)
            || !SymbolUiRegistry.TryGetSymbolUi(SendToOutputSymbolId, out var sendSymbolUi))
            return;

        // A selected texture op becomes the feed: place the send op to its right and wire it up.
        var selected = projectView.NodeSelection.GetSelectedInstanceWithoutComposition();
        var sourceSlot = selected == null ? null : FindTextureOutput(selected);
        var selectedUi = selected?.GetChildUi();
        var pos = selectedUi != null
                      ? selectedUi.PosOnCanvas + new Vector2(selectedUi.Size.X + 40, 0)
                      : Vector2.Zero;

        var newChildUi = GraphOperations.AddSymbolChild(sendSymbolUi.Symbol, compositionUi, pos);

        if (sourceSlot != null && selectedUi != null)
        {
            var connection = new Symbol.Connection(selectedUi.Id, sourceSlot.Id, newChildUi.Id, SendToOutputTextureInputId);
            UndoRedoStack.AddAndExecute(new AddConnectionCommand(compositionUi.Symbol, connection, 0));
        }

        projectView.NodeSelection.TrySelectCompositionChild(composition, newChildUi.Id, add: false);
        projectView.FocusViewToSelection();
    }

    /// <summary>The live send op with this SymbolChildId, or null while it isn't instantiated.</summary>
    public static Instance? FindSendInstance(Guid childId)
    {
        var suppliers = ContentSupplierRegistry.Suppliers;
        for (var i = 0; i < suppliers.Count; i++)
        {
            if (suppliers[i] is Instance instance && instance.SymbolChildId == childId)
                return instance;
        }

        return null;
    }

    /// <summary>The name a send op shows as a CONTENT item.</summary>
    public static string SendName(Instance instance)
    {
        var parent = instance.Parent;
        if (parent != null && parent.Symbol.Children.TryGetValue(instance.SymbolChildId, out var child))
            return child.ReadableName;

        return "content";
    }

    /// <summary>
    /// Renames a content source by renaming its op — the source has no name of its own, it mirrors the
    /// SendToOutput op. Needs a live instance to reach the graph; an op that isn't instantiated can't be
    /// renamed from here.
    /// </summary>
    public static void RenameContentSourceOp(Guid childId, string newName)
    {
        var parent = FindSendInstance(childId)?.Parent;
        var parentSymbolUi = parent?.GetSymbolUi();
        if (parentSymbolUi == null || !parentSymbolUi.ChildUis.TryGetValue(childId, out var childUi))
            return;

        UndoRedoStack.AddAndExecute(new ChangeSymbolChildNameCommand(childUi, parentSymbolUi.Symbol) { NewName = newName });
    }

    /// <summary>Selects the content's SendToOutput op in the focused graph and frames it — the setup → graph
    /// half of the sync (the graph → setup highlight is handled by the highlighted-content id).</summary>
    public static void RevealContentOpInGraph(Guid childId)
    {
        var instance = FindSendInstance(childId);
        var parentSymbolUi = instance?.Parent?.GetSymbolUi();
        if (instance == null || parentSymbolUi == null || ProjectView.Focused == null)
            return;

        if (!parentSymbolUi.ChildUis.TryGetValue(instance.SymbolChildId, out var childUi))
            return;

        ProjectView.Focused.NodeSelection.SetSelection(childUi, instance);
        FitViewToSelectionHandling.FitViewToSelection();
    }

    private static void Update(Setup setup)
    {
        var changed = false;

        // Adopt any send that has no source yet.
        foreach (var supplier in ContentSupplierRegistry.Suppliers)
        {
            if (supplier is not Instance instance)
                continue;

            var childId = instance.SymbolChildId;
            if (setup.FindSourceByChildId(childId) != null)
                continue;

            setup.ContentSources.Add(new ContentSource
                                         {
                                             SymbolChildId = childId,
                                             Name = ReadName(instance),
                                             IsRenamed = HasCustomName(instance),
                                         });
            changed = true;
        }

        // Keep names in step with the ops, so the setup still reads sensibly with nothing instantiated.
        foreach (var source in setup.ContentSources)
        {
            if (!TryFindInstance(source.SymbolChildId, out var instance))
                continue;

            var name = ReadName(instance!);
            var renamed = HasCustomName(instance!);
            if ((string.IsNullOrEmpty(name) || name == source.Name) && renamed == source.IsRenamed)
                continue;

            source.Name = name;
            source.IsRenamed = renamed;
            changed = true;
        }

        // The deletion sweep scans the whole symbol library, so it only runs when a send could actually have
        // gone away — i.e. when registry membership changed. Everything above is O(sends × sources) on two
        // small lists, so it stays per-frame and keeps names live.
        if (_sweptRegistryVersion != ContentSupplierRegistry.Version || _sweptSetupId != setup.Id)
        {
            _sweptRegistryVersion = ContentSupplierRegistry.Version;
            _sweptSetupId = setup.Id;
            changed |= DropDeletedSources(setup);
        }

        // Debounced: rename propagation changes the mirrored name once per keystroke, so saving
        // immediately would write the setup file per keypress. A short quiet period batches them;
        // every other setup edit saves through SaveActive anyway, flushing whatever is pending.
        if (changed)
            _pendingSaveSince = ImGui.GetTime();

        if (_pendingSaveSince != null && ImGui.GetTime() - _pendingSaveSince > 1.0)
        {
            _pendingSaveSince = null;
            OutputSetupHandling.SaveActive();
        }
    }

    private static double? _pendingSaveSince;
    private static int _sweptRegistryVersion = -1;
    private static Guid _sweptSetupId;

    /// <summary>
    /// Removes sources whose op is provably gone, cascading to their slices and to any surface showing one.
    /// "Provably" matters: a missing *instance* is normal, so the child is only treated as deleted once its
    /// owning symbol is loaded and no longer lists it.
    /// </summary>
    private static bool DropDeletedSources(Setup setup)
    {
        var changed = false;
        for (var i = setup.ContentSources.Count - 1; i >= 0; i--)
        {
            var source = setup.ContentSources[i];
            if (!IsChildConfirmedDeleted(source.SymbolChildId))
                continue;

            var sourceId = source.Id;
            for (var s = setup.Slices.Count - 1; s >= 0; s--)
            {
                if (setup.Slices[s].SourceId != sourceId)
                    continue;

                var sliceId = setup.Slices[s].Id;
                foreach (var surface in setup.Surfaces)
                {
                    if (surface.SliceId == sliceId)
                        surface.SliceId = Guid.Empty;
                }

                setup.Slices.RemoveAt(s);
            }

            setup.ContentSources.RemoveAt(i);
            changed = true;
        }

        return changed;
    }

    private static bool IsChildConfirmedDeleted(Guid childId)
    {
        if (TryFindInstance(childId, out _))
            return false;

        // No instance is not evidence on its own — only a loaded symbol that no longer lists the child is.
        foreach (var package in EditorSymbolPackage.AllPackages)
        {
            foreach (var symbol in package.Symbols.Values)
            {
                foreach (var child in symbol.Children.Values)
                {
                    if (child.Id == childId)
                        return false;
                }
            }
        }

        return true;
    }

    private static bool TryFindInstance(Guid childId, out Instance? instance)
    {
        instance = FindSendInstance(childId);
        return instance != null;
    }

    private static ISlot? FindTextureOutput(Instance instance)
    {
        foreach (var slot in instance.Outputs)
        {
            if (slot.ValueType == typeof(Texture2D))
                return slot;
        }

        return null;
    }

    private static string ReadName(Instance instance)
    {
        var childUi = instance.Parent?.GetSymbolUi().ChildUis.GetValueOrDefault(instance.SymbolChildId);
        var name = childUi?.SymbolChild.Name;
        return string.IsNullOrEmpty(name) ? instance.Symbol.Name : name!;
    }

    /// <summary>The op carries a name of its own when its <see cref="SymbolChild"/> name is set; otherwise it
    /// only shows its symbol's default. This is what distinguishes "Slice N" from "{op}.N".</summary>
    private static bool HasCustomName(Instance instance)
    {
        var childUi = instance.Parent?.GetSymbolUi().ChildUis.GetValueOrDefault(instance.SymbolChildId);
        return !string.IsNullOrEmpty(childUi?.SymbolChild.Name);
    }

    // Lib SendToOutput op and its texture input — the CONTENT "+" instantiates this and wires a selected feed in.
    private static readonly Guid SendToOutputSymbolId = new("0b8f2d4e-6a1c-47d3-9f5e-8c2a1b7d4e60");
    private static readonly Guid SendToOutputTextureInputId = new("8a4dd1b3-2e6f-4c25-9d0a-7f3b61c8e942");
}
