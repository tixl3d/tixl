using System.Diagnostics;
using T3.Core.Operator;
using T3.Editor.Gui.OutputUi;
using T3.Editor.UiModel.InputsAndTypes;

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

    internal Symbol.Child AddChildAsCopyFromSource(Symbol symbolToAdd, Symbol.Child sourceChild, SymbolUi sourceCompositionSymbolUi, Vector2 posInCanvas,
                                                  Guid newChildId)
    {
        FlagAsModified();
        var newChild = Symbol.AddChild(symbolToAdd, newChildId);
        newChild.Name = sourceChild.Name;

        var sourceChildUi = sourceCompositionSymbolUi.ChildUis[sourceChild.Id];
        var newChildUi = sourceChildUi!.Clone(this, newChild);

        newChildUi.PosOnCanvas = posInCanvas;

        _childUis.Add(newChildUi.Id, newChildUi);
        return newChild;
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
        //var stackTrace = new StackTrace();
        //var method = stackTrace.GetFrame(1)?.GetMethod();        
        
        //Log.Debug($" SymbolUi FlagAsModified called by {method}");
        _hasBeenModified = true;
    }

    internal SymbolUi CloneForNewSymbol(Symbol newSymbol, Dictionary<Guid, Guid> oldToNewIds = null)
    {
        FlagAsModified();
            
        //var childUis = new List<SymbolUi.Child>(ChildUis.Count);
        // foreach (var sourceChildUi in ChildUis)
        // {
        //     var clonedChildUi = sourceChildUi.Clone();
        //     Guid newChildId = oldToNewIds[clonedChildUi.Id];
        //     clonedChildUi.SymbolChild = newSymbol.Children.Single(child => child.Id == newChildId);
        //     childUis.Add(clonedChildUi);
        // }

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

        var annotations = new OrderedDictionary<Guid, Annotation>(Annotations.Count);
        foreach (var (_, annotation) in Annotations)
        {
            var clonedAnnotation = annotation.Clone();
            annotations.Add(clonedAnnotation.Id, clonedAnnotation);
        }

        var links = new OrderedDictionary<Guid, ExternalLink>(Links.Count);
        foreach (var (_, link) in Links)
        {
            var clonedLink = link.Clone();
            links.Add(clonedLink.Id, clonedLink);
        }

        return new SymbolUi(newSymbol, _ => [], inputUis, outputUis, annotations, links, hasIdMap);
    }
}