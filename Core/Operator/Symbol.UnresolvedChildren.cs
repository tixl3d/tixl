#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace T3.Core.Operator;

public sealed partial class Symbol
{
    /// <summary>
    /// A child whose symbol could not be found on load (missing package or deleted operator). It never
    /// becomes an instance; its serialized form is kept untouched and written back on save, so the
    /// operator returns intact once its symbol is available again.
    /// </summary>
    public sealed class UnresolvedChild
    {
        public Guid Id { get; }
        public Guid SymbolId { get; }

        /// <summary>Namespace and name of the missing symbol. Null for files saved before this was recorded.</summary>
        public string? SymbolName { get; }

        /// <summary>The name the user gave this child, if any.</summary>
        public string? Name { get; }

        /// <summary>
        /// Shown when <see cref="SymbolName"/> is not available. Might be the child's custom name rather
        /// than the symbol's, so it is only good for display.
        /// </summary>
        public string? FallbackName { get; set; }

        public string DisplayName => SymbolName ?? FallbackName ?? Name ?? SymbolId.ToString();

        internal JToken Json { get; }

        internal UnresolvedChild(Guid id, Guid symbolId, string? symbolName, string? name, JToken json)
        {
            Id = id;
            SymbolId = symbolId;
            SymbolName = symbolName;
            Name = name;
            Json = json;
        }
    }

    /// <param name="MultiInputIndex">Position among the connections into the same target slot when loaded.</param>
    public readonly record struct UnresolvedConnection(Connection Connection, int MultiInputIndex);

    public IReadOnlyList<UnresolvedChild> UnresolvedChildren => _unresolvedChildren;
    public bool HasUnresolvedChildren => _unresolvedChildren.Count > 0;

    /// <summary>
    /// Connections from or to <see cref="UnresolvedChildren"/>. Kept apart from <see cref="Connections"/>
    /// because instances prune every connection they can't create.
    /// </summary>
    public IReadOnlyList<UnresolvedConnection> ConnectionsOfUnresolvedChildren => _unresolvedConnections;

    /// <summary>
    /// Everything <see cref="TryRemoveUnresolvedChild"/> took out, with the list positions needed to put it
    /// back exactly. Pure data, so it can be kept by an undo command.
    /// </summary>
    public sealed class RemovedUnresolvedChild
    {
        public UnresolvedChild Child { get; }

        internal readonly int ChildIndex;
        internal readonly List<(int Index, UnresolvedConnection Connection)> Connections = [];
        internal readonly List<(int Index, Guid ChildId, JToken Json)> Animations = [];

        internal RemovedUnresolvedChild(UnresolvedChild child, int childIndex)
        {
            Child = child;
            ChildIndex = childIndex;
        }
    }

    /// <summary>
    /// Drops an unresolved child together with its connections and animations, so it is no longer written back.
    /// </summary>
    public bool TryRemoveUnresolvedChild(Guid childId, [NotNullWhen(true)] out RemovedUnresolvedChild? removed)
    {
        removed = null;
        var childIndex = _unresolvedChildren.FindIndex(child => child.Id == childId);
        if (childIndex < 0)
            return false;

        removed = new RemovedUnresolvedChild(_unresolvedChildren[childIndex], childIndex);
        _unresolvedChildren.RemoveAt(childIndex);

        // Backwards, so the recorded indices stay valid for restoring in ascending order
        for (var index = _unresolvedConnections.Count - 1; index >= 0; index--)
        {
            var connection = _unresolvedConnections[index].Connection;
            if (connection.SourceParentOrChildId != childId && connection.TargetParentOrChildId != childId)
                continue;

            removed.Connections.Insert(0, (index, _unresolvedConnections[index]));
            _unresolvedConnections.RemoveAt(index);
        }

        for (var index = _unresolvedAnimationJsons.Count - 1; index >= 0; index--)
        {
            if (_unresolvedAnimationJsons[index].ChildId != childId)
                continue;

            removed.Animations.Insert(0, (index, childId, _unresolvedAnimationJsons[index].Json));
            _unresolvedAnimationJsons.RemoveAt(index);
        }

        return true;
    }

    public void RestoreUnresolvedChild(RemovedUnresolvedChild removed)
    {
        if (IsUnresolvedChild(removed.Child.Id) || _children.ContainsKey(removed.Child.Id))
            return;

        _unresolvedChildren.Insert(Math.Min(removed.ChildIndex, _unresolvedChildren.Count), removed.Child);

        foreach (var (index, connection) in removed.Connections)
        {
            _unresolvedConnections.Insert(Math.Min(index, _unresolvedConnections.Count), connection);
        }

        foreach (var (index, childId, json) in removed.Animations)
        {
            _unresolvedAnimationJsons.Insert(Math.Min(index, _unresolvedAnimationJsons.Count), (childId, json));
        }
    }

    public bool IsUnresolvedChild(Guid childId)
    {
        foreach (var child in _unresolvedChildren)
        {
            if (child.Id == childId)
                return true;
        }

        return false;
    }

    internal void ClearUnresolved()
    {
        _unresolvedChildren.Clear();
        _unresolvedConnections.Clear();
        _unresolvedAnimationJsons.Clear();
    }

    internal void AddUnresolvedChild(UnresolvedChild child) => _unresolvedChildren.Add(child);

    internal void AddUnresolvedAnimation(Guid childId, JToken json) => _unresolvedAnimationJsons.Add((childId, json));

    /// <summary>
    /// Moves the connections touching unresolved children from <see cref="Connections"/> into their own
    /// list. They can't be wired up, and instances delete every connection they fail to wire.
    /// </summary>
    internal void MoveConnectionsOfUnresolvedChildrenToOwnList()
    {
        if (_unresolvedChildren.Count == 0)
            return;

        var countsByTarget = new Dictionary<(Guid, Guid), int>();
        for (var index = 0; index < Connections.Count; index++)
        {
            var connection = Connections[index];
            var target = (connection.TargetParentOrChildId, connection.TargetSlotId);
            countsByTarget.TryGetValue(target, out var multiInputIndex);
            countsByTarget[target] = multiInputIndex + 1;

            if (!IsUnresolvedChild(connection.SourceParentOrChildId) && !IsUnresolvedChild(connection.TargetParentOrChildId))
                continue;

            _unresolvedConnections.Add(new UnresolvedConnection(connection, multiInputIndex));
            Connections.RemoveAt(index);
            index--;
        }
    }

    /// <summary>
    /// <see cref="Connections"/> merged with the still valid connections of unresolved children, each
    /// restored to its original position among the connections into the same slot.
    /// </summary>
    internal List<Connection> GetConnectionsIncludingUnresolved()
    {
        var result = new List<Connection>(Connections);
        foreach (var (connection, multiInputIndex) in _unresolvedConnections)
        {
            if (!IsStillValid(connection))
                continue;

            var seenForTarget = 0;
            var insertIndex = result.Count;
            for (var index = 0; index < result.Count; index++)
            {
                if (!result[index].IsTargetOf(connection.TargetParentOrChildId, connection.TargetSlotId))
                    continue;

                if (seenForTarget == multiInputIndex)
                {
                    insertIndex = index;
                    break;
                }

                seenForTarget++;
                insertIndex = index + 1;
            }

            result.Insert(insertIndex, connection);
        }

        return result;
    }

    internal IEnumerable<JToken> UnresolvedAnimationJsons
    {
        get
        {
            foreach (var entry in _unresolvedAnimationJsons)
                yield return entry.Json;
        }
    }

    // The graph may have changed since loading: the other end can be gone, or a single input re-used.
    private bool IsStillValid(Connection connection)
    {
        if (!IsKnownEnd(connection.SourceParentOrChildId) || !IsKnownEnd(connection.TargetParentOrChildId))
            return false;

        if (!_children.TryGetValue(connection.TargetParentOrChildId, out var targetChild))
            return true;

        if (!targetChild.Inputs.TryGetValue(connection.TargetSlotId, out var input))
            return false;

        if (input.IsMultiInput)
            return true;

        foreach (var existing in Connections)
        {
            if (existing.IsTargetOf(connection.TargetParentOrChildId, connection.TargetSlotId))
                return false;
        }

        return true;
    }

    private bool IsKnownEnd(Guid parentOrChildId)
    {
        return parentOrChildId == Guid.Empty || _children.ContainsKey(parentOrChildId) || IsUnresolvedChild(parentOrChildId);
    }

    private readonly List<UnresolvedChild> _unresolvedChildren = [];
    private readonly List<UnresolvedConnection> _unresolvedConnections = [];
    private readonly List<(Guid ChildId, JToken Json)> _unresolvedAnimationJsons = [];
}
