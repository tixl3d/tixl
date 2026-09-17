using Newtonsoft.Json;
using T3.Core.Operator;
using T3.Editor.Gui.OutputUi;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.UiModel;

/// <summary>Creates and removes graph children together with their model and editor state.</summary>
public sealed partial class SymbolUi
{
    /// <summary>Creates a child and its UI metadata with the supplied persistent identity, canvas placement, and bypass state.</summary>
    /// <param name="symbolToAdd">Operator definition to instantiate as a child.</param>
    /// <param name="addedChildId">Stable identity assigned to the new child.</param>
    /// <param name="posInCanvas">Initial child position in canvas coordinates.</param>
    /// <param name="size">Initial child size in canvas units.</param>
    /// <param name="name">Optional custom child name; null retains the default operator name.</param>
    /// <param name="isBypassed">Requested initial bypass state, subject to reroute restrictions.</param>
    /// <returns>UI record for the newly added child.</returns>
    internal Child AddChild(Symbol symbolToAdd, Guid addedChildId, Vector2 posInCanvas, Vector2 size, string name = null, bool isBypassed = false)
    {
        FlagAsModified();
        var symbolChild = Symbol.AddChild(symbolToAdd, addedChildId, name, isBypassed);
        var childUi = new Child(symbolChild.Id, _id, (EditorSymbolPackage)Symbol.SymbolPackage)
                          {
                              PosOnCanvas = posInCanvas,
                              Size = size,
                          };
        _childUis.Add(childUi.Id, childUi);

        return childUi;
    }

    /// <summary>Copies a child and its presentation into this composition under a new identity.</summary>
    /// <param name="symbolToAdd">Operator definition used by the copied child.</param>
    /// <param name="sourceChild">Existing child whose values and settings are copied.</param>
    /// <param name="sourceCompositionSymbolUi">Composition UI owning the source child's presentation.</param>
    /// <param name="posInCanvas">Destination position in canvas coordinates.</param>
    /// <param name="newChildId">Stable identity assigned to the copied child.</param>
    /// <param name="newChild">New symbol child carrying the copied runtime settings.</param>
    /// <param name="newChildUi">New child UI carrying the copied presentation.</param>
    internal void AddChildAsCopyFromSource(Symbol symbolToAdd, Symbol.Child sourceChild, SymbolUi sourceCompositionSymbolUi, Vector2 posInCanvas,
                                                  Guid newChildId,
                                                  out Symbol.Child newChild,
                                                  out SymbolUi.Child newChildUi)
    {
        FlagAsModified();
        newChild = Symbol.AddChild(symbolToAdd, newChildId);
        newChild.Name = sourceChild.Name;

        var sourceChildUi = sourceCompositionSymbolUi.ChildUis[sourceChild.Id];
        newChildUi = sourceChildUi!.Clone(this, newChild);

        newChildUi.PosOnCanvas = posInCanvas;

        _childUis.Add(newChildUi.Id, newChildUi);
    }

    /// <summary>Removes a child and its associated presentation and graph connections.</summary>
    /// <param name="id">Identity of the child to remove with its attached graph state.</param>
    internal void RemoveChild(Guid id)
    {
        FlagAsModified();

        var removed = Symbol.RemoveChild(id); // remove from symbol

        // now remove ui entry
        var removedUi = _childUis.Remove(id, out _);

        if (removed != removedUi)
        {
            Log.Error($"Removed {removed} but removedUi {removedUi}!!");
        }

        if (removed == false)
        {
            Log.Error($"Could not remove child with id {id}");
        }

        if (removedUi == false)
        {
            Log.Error($"Could not remove child ui with id {id}");
        }
    }

    internal void FlagAsModified()
    {
        _hasBeenModified = true;
        BumpVersionCounter();
    }

    /// <summary>
    /// Invalidates display caches without marking the symbol as modified. Use for view-only refreshes
    /// (e.g. recomputing the graph layout after checking out a composition); actual edits go through
    /// <see cref="FlagAsModified"/>.
    /// </summary>
    internal void BumpVersionCounter()
    {
        VersionCounter++;
        GlobalVersionCounter++;
        // Forward to the Core Symbol so operators can cache per-frame child scans and rebuild only when
        // the symbol changes (the Player has no SymbolUi, but its graph is static so the mirror stays 0).
        Symbol.VersionCounter = VersionCounter;
    }

    /// <summary>
    /// Can be used for invalidating display caching
    /// </summary>
    internal int VersionCounter { get;private set; }

    /// <summary>
    /// Process-wide monotonic change counter, bumped on every symbol-ui modification.
    /// Lets external observers (debug protocol clients) cheaply detect "did anything change".
    /// </summary>
    internal static int GlobalVersionCounter { get; private set; }

    /// <summary>Clones the symbol presentation for a destination definition, remapping identities when supplied.</summary>
    /// <param name="newSymbol">Destination operator definition represented by the clone.</param>
    /// <param name="oldToNewIds">Optional mapping from source child and slot IDs to destination IDs.</param>
    /// <returns>Independent symbol UI associated with the destination definition.</returns>
    internal SymbolUi CloneForNewSymbol(Symbol newSymbol, Dictionary<Guid, Guid> oldToNewIds = null)
    {
        FlagAsModified();
        
        var hasIdMap = oldToNewIds != null;
            
        Func<Guid, Guid> idMapper = hasIdMap ? id => oldToNewIds[id] : id => id;

        var inputUis = new OrderedDictionary<Guid, IInputUi>(InputUis.Count);
        foreach (var (_, inputUi) in InputUis)
        {
            var clonedInputUi = inputUi.Clone();
            clonedInputUi.Parent = this;
            Guid newInputId = idMapper(clonedInputUi.Id);
            clonedInputUi.InputDefinition = newSymbol.InputDefinitions.Single(inputDef => inputDef.Id == newInputId);
            inputUis.Add(clonedInputUi.Id, clonedInputUi);
        }

        var outputUis = new OrderedDictionary<Guid, IOutputUi>(OutputUis.Count);
        foreach (var (_, outputUi) in OutputUis)
        {
            var clonedOutputUi = outputUi.Clone();
            Guid newOutputId = idMapper(clonedOutputUi.Id);
            clonedOutputUi.OutputDefinition = newSymbol.OutputDefinitions.Single(outputDef => outputDef.Id == newOutputId);
            outputUis.Add(clonedOutputUi.Id, clonedOutputUi);
        }

        var links = new OrderedDictionary<Guid, ExternalLink>(Links.Count);
        foreach (var (_, link) in Links)
        {
            var clonedLink = link.Clone();
            links.Add(clonedLink.Id, clonedLink);
        }
        
        var tourPoints = new List<TourPoint>(TourPoints.Count);
        foreach (var tp in TourPoints)
        {
            tourPoints.Add(tp.Clone());
        }

        var newSymbolUi = new SymbolUi(newSymbol, _ => [], inputUis, outputUis, [], links, tourPoints, hasIdMap);

        // Per-symbol editor settings (timeline view, render and recording setup, window layout) travel
        // with the duplicate. Output-window pinning is deliberately skipped — its pinned instance
        // paths would still reference the source project's instances.
        newSymbolUi.RenderSettings = RenderSettings?.Clone();
        newSymbolUi.RecordingSettings = CloneViaJson(RecordingSettings);
        newSymbolUi.TimelineState = CloneViaJson(TimelineState);
        newSymbolUi.WindowLayout = WindowLayout;
        newSymbolUi.WindowLayoutImGuiVersion = WindowLayoutImGuiVersion;
        newSymbolUi.WindowVisibility = WindowVisibility == null ? null : new Dictionary<string, bool>(WindowVisibility);

        return newSymbolUi;
    }

    /// <summary>
    /// Deep-copies a settings object through the same Newtonsoft serialization that persists it,
    /// so the clone matches what a save/load round-trip of the source would produce.
    /// </summary>
    /// <param name="source">Presentation object to copy through JSON serialization.</param>
    /// <typeparam name="T">Reference type of the serialized presentation object.</typeparam>
    /// <returns>Deserialized copy of the source object, or null when the source is null.</returns>
    private static T CloneViaJson<T>(T source) where T : class
    {
        if (source == null)
            return null;

        return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(source));
    }
}
