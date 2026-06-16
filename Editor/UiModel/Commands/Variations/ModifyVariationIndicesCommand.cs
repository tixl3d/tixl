#nullable enable
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.UiModel.Commands.Variations;

/// <summary>
/// Captures the <see cref="Variation.ActivationIndex"/> of a set of variations so a list reorder (which
/// swaps indices live as rows cross) can be committed as a single undoable step. Mirrors
/// <see cref="Graph.ModifyCanvasElementsCommand"/>, but for the activation index that now drives snapshot
/// order instead of the canvas position.
/// </summary>
internal sealed class ModifyVariationIndicesCommand : ICommand
{
    public string Name => "Reorder snapshots";
    public bool IsUndoable => true;

    internal ModifyVariationIndicesCommand(SymbolVariationPool pool, IReadOnlyList<Variation> variations)
    {
        _pool = pool;
        _entries = new Entry[variations.Count];
        for (var i = 0; i < variations.Count; i++)
        {
            var v = variations[i];
            _entries[i] = new Entry { Variation = v, OriginalIndex = v.ActivationIndex, NewIndex = v.ActivationIndex };
        }
    }

    /// <summary>Records the indices reached by the live reorder as the command's redo state.</summary>
    public void StoreCurrentValues()
    {
        foreach (var entry in _entries)
            entry.NewIndex = entry.Variation.ActivationIndex;
    }

    public void Do()
    {
        foreach (var entry in _entries)
            entry.Variation.ActivationIndex = entry.NewIndex;

        _pool.SaveVariationsToFile();
    }

    public void Undo()
    {
        foreach (var entry in _entries)
            entry.Variation.ActivationIndex = entry.OriginalIndex;

        _pool.SaveVariationsToFile();
    }

    private sealed class Entry
    {
        public Variation Variation = null!;
        public int OriginalIndex { get; init; }
        public int NewIndex { get; set; }
    }

    private readonly SymbolVariationPool _pool;
    private readonly Entry[] _entries;
}
