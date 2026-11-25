using System.Runtime.CompilerServices;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.MagGraph.Model;

internal sealed class MagGraphConnection
{
    public ConnectionStyles Style;
    public Vector2 SourcePos;
    public Vector2 TargetPos;
    public Vector2 DampedSourcePos;
    public Vector2 DampedTargetPos;

    public MagGraphItem SourceItem;
    public MagGraphItem TargetItem;
    public ISlot SourceOutput;
    public ISlot TargetInput => TargetItem.InputLines[InputLineIndex].Input;

    /// <summary>
    /// A saved connection whose output type doesn't match the input, e.g. after an operator changed a slot's type.
    /// The evaluation rejects it, so it carries no data and is drawn as needing attention.
    /// </summary>
    public bool IsTypeMismatch => SourceOutput != null
                                  && TargetItem != null
                                  && InputLineIndex < TargetItem.InputLines.Length
                                  && SourceOutput.ValueType != TargetInput.ValueType;

    public Type Type
    {
        get
        {
            if (SourceOutput != null)
            {
                return SourceOutput.ValueType;
            }

            if (TargetItem != null)
            {
                if (InputLineIndex >= TargetItem.InputLines.Length)
                {
                    Log.Warning("Invalid target input for connection?");
                    return null;
                }
                return TargetInput.ValueType;
            }
            return null;
        }
    }

    public int InputLineIndex;
    public int OutputLineIndex;
    public int VisibleOutputIndex; // Do we need that?
    public int ConnectionHash;
    public int MultiInputIndex;

    public bool IsSnapped => Style < ConnectionStyles.BottomToTop;


    public enum ConnectionStyles
    {
        MainOutToMainInSnappedHorizontal = 0,
        MainOutToMainInSnappedVertical,
        MainOutToInputSnappedHorizontal,
        AdditionalOutToMainInputSnappedVertical,

        BottomToTop = 4,
        BottomToLeft,
        RightToTop,
        RightToLeft,
        
        Unknown,
    }

    /** Symbol connections use Guid.Empty for the composition's own inputs and outputs */
    public Guid SourceParentOrChildId =>
        SourceItem.Variant == MagGraphItem.Variants.Input ? Guid.Empty : SourceItem.Id;

    /** Symbol connections use Guid.Empty for the composition's own inputs and outputs */
    public Guid TargetParentOrChildId =>
        TargetItem.Variant == MagGraphItem.Variants.Output ? Guid.Empty : TargetItem.Id;

    internal ConnectionRoutes.Key RouteKey => new(SourceParentOrChildId, SourceOutput.Id, TargetParentOrChildId, TargetInput.Id);

    public Symbol.Connection AsSymbolConnection()
    {
        return new Symbol.Connection(
                                     SourceParentOrChildId,
                              SourceOutput.Id,
                              TargetParentOrChildId,
                              TargetInput.Id
                             );
    }

    public int GetItemInputHash()
    {
        return GetItemInputHash(TargetItem.Id, TargetInput.Id, MultiInputIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetItemInputHash(Guid itemId, Guid inputId, int multiInputIndex)
    {
        return itemId.GetHashCode() * 31 + inputId.GetHashCode() * 31 + multiInputIndex;
    }

    public bool IsTemporary;
    public bool WasDisconnected;

}