#nullable enable
using T3.Core.Operator;

namespace T3.Editor.Gui.MagGraph.Model;

/// <summary>
/// Stand-in for a <see cref="Symbol.UnresolvedChild"/>. It has no instance and no slots, so its lines
/// are derived from the slot ids its connections refer to. Drawn as an overlay: it is deliberately not
/// a <see cref="MagGraphItem"/>, which everything else assumes to have an instance.
/// </summary>
internal sealed class MagGraphMissingItem
{
    public required Symbol.UnresolvedChild Child;

    /// <summary>Name without namespace. Cached because the canvas draws it every frame.</summary>
    public required string Label;
    public Vector2 PosOnCanvas;
    public bool HasSavedPosition;

    /// <summary>One entry per connection into this item, in connection order.</summary>
    public readonly List<Guid> InputSlotIds = new(4);

    /// <summary>Distinct output slots that have connections.</summary>
    public readonly List<Guid> OutputSlotIds = new(2);

    public int LineCount => Math.Max(1, InputSlotIds.Count) + Math.Max(0, OutputSlotIds.Count - 1);
    public Vector2 Size => new(MagGraphItem.Width, MagGraphItem.LineHeight * LineCount);

    public Vector2 GetInputAnchorOnCanvas(int inputLineIndex)
    {
        return PosOnCanvas + new Vector2(0, (0.5f + inputLineIndex) * MagGraphItem.LineHeight);
    }

    /// <summary>Mirrors real items: the first output shares line 0, further ones follow the inputs.</summary>
    public Vector2 GetOutputAnchorOnCanvas(int outputIndex)
    {
        var lineIndex = outputIndex == 0 ? 0 : Math.Max(1, InputSlotIds.Count) + outputIndex - 1;
        return PosOnCanvas + new Vector2(MagGraphItem.Width, (0.5f + lineIndex) * MagGraphItem.LineHeight);
    }
}

/// <summary>A connection with a <see cref="MagGraphMissingItem"/> on at least one end.</summary>
internal sealed class MagGraphMissingConnection
{
    public MagGraphMissingItem? SourceMissingItem;
    public int SourceMissingOutputIndex;
    public MagGraphItem? SourceItem;
    public Guid SourceSlotId;
    public int SourceItemVisibleIndex;

    public MagGraphMissingItem? TargetMissingItem;
    public int TargetMissingInputLineIndex;
    public MagGraphItem? TargetItem;
    public Guid TargetSlotId;
    public int TargetItemVisibleIndex;
}
