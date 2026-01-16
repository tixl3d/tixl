using System.Runtime.CompilerServices;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

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

    public Symbol.Connection AsSymbolConnection()
    {
        var sourceParentOfSymbolChildId =
            SourceItem.Variant == MagGraphItem.Variants.Input ? Guid.Empty : SourceItem.Id;
            
        var targetParentOfSymbolChildId =
            TargetItem.Variant == MagGraphItem.Variants.Output ? Guid.Empty : TargetItem.Id;
        
        
        return new Symbol.Connection(
                                     sourceParentOfSymbolChildId,
                              SourceOutput.Id,
                              targetParentOfSymbolChildId,
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