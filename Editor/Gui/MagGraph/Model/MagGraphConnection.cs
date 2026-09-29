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

    /// <summary>
    /// Identifies this connection within its source child's UI, which is where manual bend points live.
    /// </summary>
    internal SymbolUi.Child.ConnectionTarget WaypointTarget =>
        new(SourceOutput.Id, TargetInput.Id);

    /// <summary>
    /// Manual bend points of this connection, or null when it still follows its automatic route.
    /// </summary>
    internal List<Vector2>? GetWaypoints()
    {
        return SourceItem.ChildUi?.GetConnectionWaypoints(WaypointTarget);
    }

    /// <summary>
    /// Appends this connection's manual bend points to <paramref name="points"/>, dropping the ones that
    /// land on their neighbour (they could only add cusps), and reports which point the last segment
    /// starts at.
    /// </summary>
    /// <param name="insertIndex">
    /// Where a bend point clicked on that last segment belongs in the saved list, so inserting keeps the
    /// route in order. Null when the connection has no bend points at all.
    /// </param>
    /// <returns>The anchor a bend point clicked on the last segment would be spliced into.</returns>
    internal Vector2 AppendWaypoints(List<Vector2> points, out int? insertIndex)
    {
        const float minimumDistanceBetweenPoints = 6;

        insertIndex = null;
        var waypoints = GetWaypoints();
        if (waypoints == null)
            return points.Count > 0 ? points[^1] : Vector2.Zero;

        var minimumDistanceSquared = minimumDistanceBetweenPoints * minimumDistanceBetweenPoints;
        for (var index = 0; index < waypoints.Count; index++)
        {
            var waypoint = waypoints[index];
            if (Vector2.DistanceSquared(points[^1], waypoint) < minimumDistanceSquared)
                continue;

            points.Add(waypoint);
            insertIndex = points.Count - 1;
        }

        return points[^1];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetItemInputHash(Guid itemId, Guid inputId, int multiInputIndex)
    {
        return itemId.GetHashCode() * 31 + inputId.GetHashCode() * 31 + multiInputIndex;
    }

    public bool IsTemporary;
    public bool WasDisconnected;

}