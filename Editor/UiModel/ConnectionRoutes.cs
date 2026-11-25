#nullable enable
using T3.Core.Operator;

namespace T3.Editor.UiModel;

/// <summary>
/// Manual paths of connection lines in a composition. A route is an ordered list of point ids from source to
/// target. Routes from the same output may share points, so their cables run along a common trunk.
/// </summary>
/// <remarks>
/// Purely visual: evaluation never reads it. Routes of connections that were deleted stay until the next save,
/// so undoing the deletion brings the route back; drawing only looks up live connections.
/// </remarks>
internal sealed class ConnectionRoutes
{
    /// <summary>The four ids of a <see cref="Symbol.Connection"/>, as an allocation-free dictionary key.</summary>
    internal readonly record struct Key(Guid SourceParentOrChildId, Guid SourceSlotId, Guid TargetParentOrChildId, Guid TargetSlotId)
    {
        internal static Key From(Symbol.Connection connection)
        {
            return new Key(connection.SourceParentOrChildId, connection.SourceSlotId,
                           connection.TargetParentOrChildId, connection.TargetSlotId);
        }

        internal Symbol.Connection ToConnection()
        {
            return new Symbol.Connection(SourceParentOrChildId, SourceSlotId, TargetParentOrChildId, TargetSlotId);
        }

        internal bool HasSameSource(in Key other)
        {
            return SourceParentOrChildId == other.SourceParentOrChildId && SourceSlotId == other.SourceSlotId;
        }

        /** The composition's own inputs and outputs are layout items identified by their slot id. */
        internal Guid SourceItemId => SourceParentOrChildId == Guid.Empty ? SourceSlotId : SourceParentOrChildId;

        internal Guid TargetItemId => TargetParentOrChildId == Guid.Empty ? TargetSlotId : TargetParentOrChildId;
    }

    internal Dictionary<Guid, Vector2> Points { get; } = new();
    internal Dictionary<Key, List<Guid>> Routes { get; } = new();

    internal bool IsEmpty => Routes.Count == 0;

    internal bool TryGetRoute(in Key key, out List<Guid> pointIds)
    {
        if (Routes.Count == 0)
        {
            pointIds = null!;
            return false;
        }

        return Routes.TryGetValue(key, out pointIds!);
    }

    internal ConnectionRoutes Clone()
    {
        var clone = new ConnectionRoutes();
        clone.CopyFrom(this);
        return clone;
    }

    internal void CopyFrom(ConnectionRoutes other)
    {
        Points.Clear();
        foreach (var (id, position) in other.Points)
        {
            Points[id] = position;
        }

        Routes.Clear();
        foreach (var (key, pointIds) in other.Routes)
        {
            Routes[key] = [..pointIds];
        }
    }

    /// <summary>Inserts a new point into the route of <paramref name="key"/>, creating the route if needed.</summary>
    internal Guid InsertPoint(in Key key, int index, Vector2 position)
    {
        if (!Routes.TryGetValue(key, out var pointIds))
        {
            pointIds = [];
            Routes[key] = pointIds;
        }

        var id = Guid.NewGuid();
        Points[id] = position;
        pointIds.Insert(Math.Clamp(index, 0, pointIds.Count), id);
        return id;
    }

    /// <summary>Removes a point from every route that uses it, and routes left without points.</summary>
    internal void RemovePoint(Guid pointId)
    {
        Points.Remove(pointId);
        _keysToRemove.Clear();
        foreach (var (key, pointIds) in Routes)
        {
            pointIds.RemoveAll(id => id == pointId);
            if (pointIds.Count == 0)
                _keysToRemove.Add(key);
        }

        foreach (var key in _keysToRemove)
        {
            Routes.Remove(key);
        }
    }

    /// <summary>
    /// Only routes from the same output may share a point, and never a route that already runs through both,
    /// which would make it loop back.
    /// </summary>
    internal bool CanMerge(Guid pointId, Guid intoPointId)
    {
        if (pointId == intoPointId)
            return false;

        Key? source = null;
        foreach (var (key, pointIds) in Routes)
        {
            var hasPoint = pointIds.Contains(pointId);
            var hasTarget = pointIds.Contains(intoPointId);
            if (!hasPoint && !hasTarget)
                continue;

            if (hasPoint && hasTarget)
                return false;

            if (source == null)
            {
                source = key;
            }
            else if (!source.Value.HasSameSource(key))
            {
                return false;
            }
        }

        return source != null;
    }

    /// <summary>Replaces <paramref name="pointId"/> with <paramref name="intoPointId"/> in every route.</summary>
    internal void MergePoint(Guid pointId, Guid intoPointId)
    {
        foreach (var pointIds in Routes.Values)
        {
            for (var index = 0; index < pointIds.Count; index++)
            {
                if (pointIds[index] == pointId)
                    pointIds[index] = intoPointId;
            }
        }

        Points.Remove(pointId);
    }

    /// <summary>
    /// Copies the routes of connections that were duplicated, giving their points new ids so the copy can be
    /// edited on its own while trunks shared within the copy stay shared.
    /// </summary>
    internal void CopyRoutesFrom(ConnectionRoutes source, List<(Symbol.Connection Original, Symbol.Connection Copy)> connections, Vector2 offset)
    {
        if (source.IsEmpty)
            return;

        var newPointIds = new Dictionary<Guid, Guid>();
        foreach (var (original, copy) in connections)
        {
            if (!source.Routes.TryGetValue(Key.From(original), out var pointIds))
                continue;

            var newRoute = new List<Guid>(pointIds.Count);
            foreach (var pointId in pointIds)
            {
                if (!source.Points.TryGetValue(pointId, out var position))
                    continue;

                if (!newPointIds.TryGetValue(pointId, out var newId))
                {
                    newId = Guid.NewGuid();
                    newPointIds[pointId] = newId;
                    Points[newId] = position + offset;
                }

                newRoute.Add(newId);
            }

            if (newRoute.Count > 0)
                Routes[Key.From(copy)] = newRoute;
        }
    }

    internal void RemoveRoutes(List<(Symbol.Connection Original, Symbol.Connection Copy)> connections)
    {
        foreach (var (_, copy) in connections)
        {
            Routes.Remove(Key.From(copy));
        }

        RemoveUnusedPoints();
    }

    internal void RemoveUnusedPoints()
    {
        _usedPointIds.Clear();
        foreach (var pointIds in Routes.Values)
        {
            foreach (var id in pointIds)
            {
                _usedPointIds.Add(id);
            }
        }

        _pointsToRemove.Clear();
        foreach (var id in Points.Keys)
        {
            if (!_usedPointIds.Contains(id))
                _pointsToRemove.Add(id);
        }

        foreach (var id in _pointsToRemove)
        {
            Points.Remove(id);
        }
    }

    private readonly List<Key> _keysToRemove = [];
    private readonly HashSet<Guid> _usedPointIds = [];
    private readonly List<Guid> _pointsToRemove = [];
}
