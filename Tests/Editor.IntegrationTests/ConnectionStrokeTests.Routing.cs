using System.Numerics;
using T3.Core.Operator;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.UiModel.Commands;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>Verifies source-grouped routing, overlap movement, and exact occurrence replay.</summary>
public sealed partial class ConnectionStrokeTests
{
    /// <summary>Dropping blank or chained anchors onto another anchor changes only positions, including mixed selections and ordered duplicates.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompletingOverlappingMovementPreservesAnchorsAndWiring(bool connected, bool grouped)
    {
        var definition = _package.Add(typeof(Types.Routing.TestReroute<float>));
        var a = _home.AddChild(definition.Symbol, Guid.NewGuid(), new Vector2(200, 100), MagGraphItem.RerouteSize);
        var b = _home.AddChild(definition.Symbol, Guid.NewGuid(), new Vector2(235, 100), MagGraphItem.RerouteSize);
        if (connected)
        {
            var multi = _package.Add(typeof(Types.Routing.MultiReroute));
            var target = _home.AddChild(multi.Symbol, Guid.NewGuid(), new Vector2(500, 200), new Vector2(110, 25));
            var input = definition.Symbol.InputDefinitions[0].Id;
            var output = definition.Symbol.OutputDefinitions[0].Id;
            _home.Symbol.AddConnection(new(_connection.SourceItem.Id, _connection.SourceOutput.Id, a.Id, input), 0);
            _home.Symbol.AddConnection(new(a.Id, output, b.Id, input), 0);
            _home.Symbol.AddConnection(new(b.Id, output, target.Id, multi.Symbol.InputDefinitions[0].Id), 0);
            _home.Symbol.AddConnection(new(_connection.SourceItem.Id, _connection.SourceOutput.Id, target.Id, multi.Symbol.InputDefinitions[0].Id), 1);
            _home.Symbol.AddConnection(new(b.Id, output, target.Id, multi.Symbol.InputDefinitions[0].Id), 2);
        }
        _context.Layout.ComputeLayout(_context, true);
        var before = _home.Symbol.Connections.Select(c => c.Clone()).ToArray();
        var positions = _home.ChildUis.ToDictionary(p => p.Key, p => p.Value.PosOnCanvas);
        var ordinary = _connection.TargetItem.ChildUi!;
        _context.ItemMovement.SetDraggedItems(grouped ? [a, ordinary] : [a]);
        _context.ItemMovement.StartDragOperation(_context);
        var offset = b.PosOnCanvas - a.PosOnCanvas;
        a.PosOnCanvas += offset;
        if (grouped) ordinary.PosOnCanvas += offset;
        _context.ItemMovement.CompleteDragOperation(_context);
        Assert.Equal(b.PosOnCanvas, a.PosOnCanvas);
        Assert.Equal(before, _home.Symbol.Connections);
        Assert.True(positions.Keys.ToHashSet().SetEquals(_home.Symbol.Children.Keys));
        Assert.Equal(MagGraphItem.RerouteSize, a.Size);
        Assert.Equal(MagGraphItem.RerouteSize, b.Size);
        UndoRedoStack.Undo();
        Assert.All(positions, p => Assert.Equal(p.Value, _home.ChildUis[p.Key].PosOnCanvas));
        Assert.Equal(before, _home.Symbol.Connections);
        UndoRedoStack.Redo();
        Assert.Equal(b.PosOnCanvas, a.PosOnCanvas);
        Assert.Equal(before, _home.Symbol.Connections);
        Assert.True(positions.Keys.ToHashSet().SetEquals(_home.Symbol.Children.Keys));
    }

    /// <summary>A/B/A target order and same-source fan-out survive routing, cutting one occurrence, and undo/redo.</summary>
    [Fact]
    public void RoutingGroupsSourcesAndPreservesDuplicateOccurrenceOrder()
    {
        var definition = _package.Add(typeof(Types.Routing.TestReroute<float>));
        var multi = _package.Add(typeof(Types.Routing.MultiReroute));
        var sourceA = _connection.SourceItem.Id;
        var sourceB = _home.AddChild(_connection.SourceItem.Instance!.Symbol, Guid.NewGuid(), new Vector2(10, 220), new Vector2(110, 25));
        var target = _home.AddChild(multi.Symbol, Guid.NewGuid(), new Vector2(400, 220), new Vector2(110, 25));
        var inputId = multi.Symbol.InputDefinitions[0].Id;
        var outputId = _connection.SourceOutput.Id;
        var a = new Symbol.Connection(sourceA, outputId, target.Id, inputId);
        var b = new Symbol.Connection(sourceB.Id, outputId, target.Id, inputId);
        _home.Symbol.AddConnection(a, 0);
        _home.Symbol.AddConnection(b, 1);
        _home.Symbol.AddConnection(a.Clone(), 2);
        _context.Layout.ComputeLayout(_context, true);
        var before = ConnectionsByTarget(_home.Symbol.Connections.Select(c => c.Clone()));
        var childrenBefore = _home.Symbol.Children.Keys.ToHashSet();
        var hits = _context.Layout.MagConnections.Select((c, i) => new RerouteOperations.StrokeHit(RerouteOperations.Capture(c), new Vector2(170, 100 + 30 * i))).ToArray();

        Assert.True(RerouteOperations.TryApply(_context, false, hits, out var error), error);
        var anchors = _home.Symbol.Children.Values.Where(c => !childrenBefore.Contains(c.Id)).ToArray();
        Assert.Equal(2, anchors.Length);
        Assert.All(anchors, child => Assert.Equal(definition.Symbol.Id, child.Symbol.Id));
        var ordered = _home.Symbol.Connections.Where(c => c.TargetParentOrChildId == target.Id).ToArray();
        Assert.Equal(3, ordered.Length);
        Assert.Equal(ordered[0].SourceParentOrChildId, ordered[2].SourceParentOrChildId);
        Assert.NotEqual(ordered[0].SourceParentOrChildId, ordered[1].SourceParentOrChildId);
        var sharedAnchor = ordered[0].SourceParentOrChildId;
        Assert.Contains(_home.Symbol.Connections, c => c.SourceParentOrChildId == sourceA && c.TargetParentOrChildId == sharedAnchor);
        Assert.Contains(_home.Symbol.Connections, c => c.SourceParentOrChildId == sharedAnchor && c.TargetParentOrChildId == _connection.TargetItem.Id);
        var routed = ConnectionsByTarget(_home.Symbol.Connections.Select(c => c.Clone()));
        UndoRedoStack.Undo();
        Assert.Equal(before, ConnectionsByTarget(_home.Symbol.Connections));
        Assert.True(childrenBefore.SetEquals(_home.Symbol.Children.Keys));
        UndoRedoStack.Redo();
        Assert.Equal(routed, ConnectionsByTarget(_home.Symbol.Connections));
        Assert.All(anchors, child => Assert.True(_home.Symbol.Children.ContainsKey(child.Id)));

        var first = ordered[0];
        var cut = new RerouteOperations.StrokeHit(new(first.SourceParentOrChildId, first.SourceSlotId, target.Id, inputId, 0), Vector2.Zero);
        Assert.True(RerouteOperations.TryApply(_context, true, [cut], out error), error);
        Assert.Equal(new[] { ordered[1], ordered[2] }, _home.Symbol.Connections.Where(c => c.TargetParentOrChildId == target.Id));
        Assert.True(_home.Symbol.Children.ContainsKey(sharedAnchor));
        UndoRedoStack.Undo();
        Assert.Equal(routed, ConnectionsByTarget(_home.Symbol.Connections));
        UndoRedoStack.Redo();
        Assert.Equal(new[] { ordered[1], ordered[2] }, _home.Symbol.Connections.Where(c => c.TargetParentOrChildId == target.Id));
    }

    /// <summary>Unavailable definitions, stale occurrence indices, and read-only packages reject the whole insertion without history or partial edits.</summary>
    [Fact]
    public void RoutingRejectsUnsupportedOrStaleEditsBeforeMutation()
    {
        var occurrence = RerouteOperations.Capture(_connection);
        var hit = new RerouteOperations.StrokeHit(occurrence, new Vector2(170, 100));
        var before = _home.Symbol.Connections.Select(c => c.Clone()).ToArray();
        var childrenBefore = _home.Symbol.Children.Keys.ToHashSet();
        Assert.False(RerouteOperations.TryApply(_context, false, [hit], out var error));
        Assert.NotEmpty(error);
        Assert.Equal(before, _home.Symbol.Connections);
        Assert.True(childrenBefore.SetEquals(_home.Symbol.Children.Keys));
        Assert.False(UndoRedoStack.CanUndo);

        _package.Add(typeof(Types.Routing.TestReroute<float>));
        var stale = hit with { Occurrence = occurrence with { MultiInputIndex = 1 } };
        Assert.False(RerouteOperations.TryApply(_context, false, [hit, stale], out error));
        Assert.NotEmpty(error);
        Assert.Equal(before, _home.Symbol.Connections);
        Assert.True(childrenBefore.SetEquals(_home.Symbol.Children.Keys));
        Assert.False(UndoRedoStack.CanUndo);

        _package.ReadOnly = true;
        Assert.False(RerouteOperations.TryApply(_context, false, [hit], out error));
        Assert.NotEmpty(error);
        Assert.Equal(before, _home.Symbol.Connections);
        Assert.True(childrenBefore.SetEquals(_home.Symbol.Children.Keys));
        Assert.False(UndoRedoStack.CanUndo);
    }
    /// <summary>Canonicalizes independent target groups while retaining every target's exact occurrence order.</summary>
    private static Symbol.Connection[] ConnectionsByTarget(IEnumerable<Symbol.Connection> connections)
        => connections.OrderBy(c => c.TargetParentOrChildId).ThenBy(c => c.TargetSlotId).ToArray();

}
