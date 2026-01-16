using T3.Core.Operator;
using T3.Editor.Gui.OutputUi;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.UiModel;

public sealed partial class SymbolUi
{
    internal Child AddChild(Symbol symbolToAdd, Guid addedChildId, Vector2 posInCanvas, Vector2 size, string name = null)
    {
        FlagAsModified();
        var symbolChild = Symbol.AddChild(symbolToAdd, addedChildId, name);
        var childUi = new Child(symbolChild.Id, _id, (EditorSymbolPackage)Symbol.SymbolPackage)
                          {
                              PosOnCanvas = posInCanvas,
                              Size = size,
                          };
        _childUis.Add(childUi.Id, childUi);

        return childUi;
    }

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
        VersionCounter++;
    }

    /// <summary>
    /// Can be used for invalidating display caching
    /// </summary>
    internal int VersionCounter { get;private set; }

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

        return new SymbolUi(newSymbol, _ => [], inputUis, outputUis, [], links, tourPoints, hasIdMap);
    }
}