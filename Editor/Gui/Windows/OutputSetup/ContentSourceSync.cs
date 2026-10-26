#nullable enable
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Commands.Setup;
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
/// <para>Scoped to the active composition — the nearest op up the breadcrumbs with composition settings, else the
/// project root — because its sends are what an export of it ships. Sources of other compositions stay in the
/// setup with their routing, they are only neither adopted nor shown while another composition is active.</para>
/// </summary>
internal static class ContentSourceSync
{
    /// <summary>Runs the sync against the published active setup. Called from the frame loop, so the
    /// source list stays in step with the graph whether or not any output UI is open.</summary>
    public static void UpdateFrame()
    {
        UpdateScope();
        var setup = ActiveSetup.Current;
        if (setup != null)
            Update(setup);
    }

    /// <summary>Whether a live send op belongs to the active composition: it lies under it, and any composition
    /// nested in between lets it through.</summary>
    public static bool IsInScope(Instance send)
    {
        var scopePath = _scopePath;
        var path = send.InstancePath;
        if (scopePath == null || path.Count <= scopePath.Count)
            return false;

        for (var i = 0; i < scopePath.Count; i++)
        {
            if (path[i] != scopePath[i])
                return false;
        }

        // Ancestors strictly below the scope root, nearest first.
        var ancestor = send.Parent;
        for (var depth = path.Count - 1; ancestor != null && depth > scopePath.Count; depth--)
        {
            if (ancestor.Symbol.CompositionSettings.Enabled)
                return ContentSupplierSearch.IsExposedToParents(send.SymbolChild);

            ancestor = ancestor.Parent;
        }

        return true;
    }

    /// <summary>Whether a content source's send op lies under the active composition, instantiated or not.</summary>
    public static bool IsSourceInScope(Guid childId)
    {
        // The editor UI belongs to no composition.
        if (EditorUiContent.IsEditorUi(childId))
            return true;

        var instance = FindSendInstance(childId);
        return instance != null ? IsInScope(instance) : _scopeSendChildIds.Contains(childId);
    }

    /// <summary>
    /// Drops a <c>SendToOutput</c> op into the focused composition, selects it, and frames the view on it. When a
    /// texture-outputting op is selected it lands to its right and is wired straight in, so the feed shows up in
    /// the setup at once (the CONTENT item appears next frame, once the sync adopts it).
    /// </summary>
    public static void AddContentSend(SetupEntitySelection selection)
    {
        var macro = new MacroCommand("Add SendToOutput");
        if (TryAddSendOp(macro, string.Empty, out _))
            UndoRedoStack.Add(macro);
    }

    /// <summary>
    /// Adds a <c>SendToOutput</c> op like <see cref="AddContentSend"/>, executing its commands into
    /// <paramref name="macro"/> so a caller can fold them into one undo step with its own edits.
    /// </summary>
    public static bool TryAddSendOp(MacroCommand macro, string name, out Guid childId)
    {
        childId = Guid.Empty;
        var projectView = ProjectView.Focused;
        var composition = projectView?.CompositionInstance;
        if (projectView == null || composition == null)
            return false;

        if (!composition.Symbol.TryGetSymbolUi(out var compositionUi)
            || !SymbolUiRegistry.TryGetSymbolUi(SendToOutputSymbolId, out _))
            return false;

        // A selected texture op becomes the feed: place the send op to its right and wire it up.
        var selected = projectView.NodeSelection.GetSelectedInstanceWithoutComposition();
        var sourceSlot = selected == null ? null : FindTextureOutput(selected);
        var selectedUi = selected?.GetChildUi();
        var pos = selectedUi != null
                      ? selectedUi.PosOnCanvas + new Vector2(selectedUi.Size.X + 40, 0)
                      : Vector2.Zero;

        var addCommand = new AddSymbolChildCommand(compositionUi.Symbol, SendToOutputSymbolId) { PosOnCanvas = pos, ChildName = name };
        macro.AddAndExecCommand(addCommand);
        childId = addCommand.AddedChildId;

        if (sourceSlot != null && selectedUi != null)
        {
            var connection = new Symbol.Connection(selectedUi.Id, sourceSlot.Id, childId, SendToOutputTextureInputId);
            macro.AddAndExecCommand(new AddConnectionCommand(compositionUi.Symbol, connection, 0));
        }

        projectView.NodeSelection.TrySelectCompositionChild(composition, childId, add: false);
        projectView.FocusViewToSelection();
        return true;
    }

    /// <summary>
    /// Pins a send op's render size, as an undoable input change collected into <paramref name="macro"/>.
    /// The op is found through its parent symbol, so one that isn't instantiated can still be set.
    /// </summary>
    public static bool TrySetSendResolution(MacroCommand macro, Guid childId, Int2 resolution)
    {
        if (!TryFindSendParent(childId, out var parent)
            || !parent.Children[childId].Inputs.TryGetValue(SendToOutputResolutionInputId, out var input))
            return false;

        macro.AddAndExecCommand(new ChangeInputValueCommand(parent, childId, input, new InputValue<Int2>(resolution)));
        return true;
    }

    /// <summary>
    /// Deletes a content source's send op from the graph. Its slices and the routes showing them go in the same
    /// undo step, so an undo brings the routing back with the op instead of a bare, re-adopted source.
    /// </summary>
    public static void DeleteSendOp(Setup setup, Guid childId)
    {
        if (!TryFindSendParent(childId, out var parent)
            || !parent.TryGetSymbolUi(out var parentUi)
            || !parentUi.ChildUis.TryGetValue(childId, out var childUi))
            return;

        var oldJson = setup.ToJsonString();
        if (setup.FindSourceByChildId(childId) is { } source)
            SetupActions.RemoveContentSource(setup, source);

        var macro = new MacroCommand("Delete content");
        macro.AddExecutedCommandForUndo(new SetupSnapshotCommand("Delete content", setup.Id, oldJson, setup.ToJsonString()));
        macro.AddAndExecCommand(new DeleteSymbolChildrenCommand(parentUi, [childUi]));
        UndoRedoStack.Add(macro);
        OutputSetupHandling.SaveActive();
    }

    /// <summary>Whether a send op with this id exists in some symbol, instantiated or not.</summary>
    public static bool HasSendOp(Guid childId) => TryFindSendParent(childId, out _);

    /// <summary>Blinks the send op behind a hovered content source, or behind the source of a hovered slice, in the graph.</summary>
    public static void HighlightSendOpInGraph(Setup setup, SetupEntityKinds kind, Guid id)
    {
        if (kind == SetupEntityKinds.ContentSource)
        {
            FrameStats.AddHoveredId(id);
        }
        else if (kind == SetupEntityKinds.Slice && setup.FindSlice(id) is { } slice && setup.FindSource(slice.SourceId) is { } source)
        {
            FrameStats.AddHoveredId(source.SymbolChildId);
        }
    }

    /** The op is found through its parent symbol, so one that isn't instantiated can still be edited. */
    private static bool TryFindSendParent(Guid childId, [NotNullWhen(true)] out Symbol? parent)
    {
        foreach (var package in EditorSymbolPackage.AllPackages)
        {
            foreach (var symbol in package.Symbols.Values)
            {
                if (!symbol.Children.ContainsKey(childId))
                    continue;

                parent = symbol;
                return true;
            }
        }

        parent = null;
        return false;
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

    /// <summary>Opens the composition holding the content's SendToOutput op, selects and frames it — the setup →
    /// graph half of the sync (the graph → setup highlight is handled by the highlighted-content id).</summary>
    public static void RevealContentOpInGraph(Guid childId)
    {
        var instance = FindSendInstance(childId);
        var projectView = ProjectView.Focused;
        if (instance == null || projectView?.GraphView == null)
            return;

        // The registry spans every open project; a path from another one can't be opened in this graph.
        var path = instance.InstancePath;
        if (path.Count == 0 || path[0] != projectView.RootInstance.SymbolChildId)
            return;

        projectView.GraphView.OpenAndFocusInstance(path);
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
            if (!IsInScope(instance) || setup.FindSourceByChildId(childId) != null)
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

    private static void UpdateScope()
    {
        var projectView = ProjectView.Focused;
        var composition = projectView?.CompositionInstance;
        if (projectView == null || composition == null)
        {
            _scopePath = null;
            _scopeSendChildIds.Clear();
            return;
        }

        var scopeRoot = composition;
        while (scopeRoot.Parent != null && !scopeRoot.Symbol.CompositionSettings.Enabled)
        {
            scopeRoot = scopeRoot.Parent;
        }

        // The symbol walk only reruns when the scope moved or a send appeared or went away.
        if (_scopePath == scopeRoot.InstancePath && _scopeRegistryVersion == ContentSupplierRegistry.Version)
            return;

        _scopePath = scopeRoot.InstancePath;
        _scopeRegistryVersion = ContentSupplierRegistry.Version;
        _scopeSendChildIds.Clear();
        ContentSupplierSearch.CollectSupplierChildIds(scopeRoot.Symbol, _scopeSendChildIds);
    }

    private static IReadOnlyList<Guid>? _scopePath;
    private static int _scopeRegistryVersion = -1;
    private static readonly HashSet<Guid> _scopeSendChildIds = [];

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
            if (EditorUiContent.IsEditorUi(source.SymbolChildId) || !IsChildConfirmedDeleted(source.SymbolChildId))
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

    // Lib SendToOutput op and its inputs — the CONTENT "+" instantiates this and wires a selected feed in.
    private static readonly Guid SendToOutputSymbolId = new("0b8f2d4e-6a1c-47d3-9f5e-8c2a1b7d4e60");
    private static readonly Guid SendToOutputTextureInputId = new("8a4dd1b3-2e6f-4c25-9d0a-7f3b61c8e942");
    private static readonly Guid SendToOutputResolutionInputId = new("3c7f1e58-0a94-4d62-b8f3-2e5d9a0c4b17");
}
