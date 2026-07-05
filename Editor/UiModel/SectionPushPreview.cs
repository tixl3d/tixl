#nullable enable
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.UiModel;

/// <summary>
/// Live preview for pushing a section's neighbors while its bounds grow during an
/// interaction (slow border drag, slow-grow by dragged ops). Neighbor positions are
/// frozen on engage and the pushes are recomputed from them every frame — stateless,
/// so neighbors follow the border back when it retreats. <see cref="Complete"/> folds
/// the net movement into the interaction's MacroCommand.
/// </summary>
internal sealed class SectionPushPreview
{
    internal bool IsEngaged => _command != null;

    internal void Engage(SymbolUi symbolUi, Section section, ISelection selector)
    {
        Reset();
        SectionTree.CollectScopeBlocks(symbolUi, section, _blocks);
        if (_blocks.Count == 0)
            return;

        var allElements = new List<ISelectableCanvasObject>();
        for (var blockIndex = 0; blockIndex < _blocks.Count; blockIndex++)
        {
            var firstIndex = allElements.Count;
            allElements.Add(_blocks[blockIndex].Root);
            if (_blocks[blockIndex].Root is Section blockSection)
            {
                SectionTree.CollectSectionContents(symbolUi, blockSection, allElements);
            }

            for (var i = firstIndex; i < allElements.Count; i++)
            {
                _elements.Add((allElements[i], allElements[i].PosOnCanvas, blockIndex));
            }
        }

        _command = new ModifyCanvasElementsCommand(symbolUi, allElements, selector);
    }

    /// <summary>Recomputes and applies the pushes for the section's current bounds.</summary>
    internal void Update(Section section, bool pushDown, bool pushRight)
    {
        if (_command == null)
            return;

        var claimed = ImRect.RectWithSize(section.PosOnCanvas, section.Size);

        // One combined pass: each block resolves along a single axis, so a frame already
        // pushed down can't additionally be flung sideways by the other axis
        SectionTree.ComputePushesForBlocks(_blocks, claimed, allowDown: pushDown, allowRight: pushRight, _deltas);

        foreach (var (element, originalPos, blockIndex) in _elements)
        {
            element.PosOnCanvas = originalPos + _deltas[blockIndex];
        }
    }

    /// <summary>Folds the net movement into the macro as an executed command.</summary>
    internal void Complete(MacroCommand macro)
    {
        if (_command != null)
        {
            _command.StoreCurrentValues();
            macro.AddExecutedCommandForUndo(_command);
        }

        _command = null;
        Reset();
    }

    /// <summary>Restores the neighbors' original positions (aborted interactions).</summary>
    internal void CancelAndReset()
    {
        _command?.Undo();
        Reset();
    }

    internal void Reset()
    {
        _command = null;
        _blocks.Clear();
        _elements.Clear();
    }

    private ModifyCanvasElementsCommand? _command;
    private readonly List<(ISelectableCanvasObject Root, ImRect Bounds)> _blocks = [];
    private readonly List<(ISelectableCanvasObject Element, Vector2 OriginalPos, int BlockIndex)> _elements = [];
    private readonly List<Vector2> _deltas = [];
}
