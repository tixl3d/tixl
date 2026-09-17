using System.Numerics;
using System.Reflection;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>Checks allocation-free drawing and sorting while preserving splice and stack geometry.</summary>
public sealed partial class ConnectionStrokeTests
{
    /// <summary>Drawing warmed persistent wires must not allocate managed objects per connection.</summary>
    /// <param name="style">Connection style name to exercise during the allocation measurement.</param>
    [Theory]
    [InlineData("RightToLeft")]
    [InlineData("MainOutToMainInSnappedHorizontal")]
    [InlineData("MainOutToInputSnappedHorizontal")]
    [InlineData("MainOutToMainInSnappedVertical")]
    public void PersistentWireDrawingDoesNotAllocate(string style)
    {
        _connection.Style = Enum.Parse<MagGraphConnection.ConnectionStyles>(style);
        _connection.DampedSourcePos = new Vector2(150, 100);
        _connection.DampedTargetPos = new Vector2(400, 100);
        // Exercise the splice lookup as well as the ordinary wire path.
        _context.ItemMovement.SpliceSets.Add(Splice(MagGraphItem.Directions.Horizontal, typeof(float)));
        _context.ItemMovement.SpliceSets.Add(Splice(MagGraphItem.Directions.Vertical, typeof(float)));
        for (var i = 0; i < 256; i++)
            _draw(_connection, _drawList, _context, null);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            _draw(_connection, _drawList, _context, null);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(0, allocated);
    }

    /// <summary>Splice highlighting requires a matching direction and type and excludes the dragged source.</summary>
    /// <param name="vertical">Whether to test vertical rather than horizontal snapped splice highlighting.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnappedSpliceHighlightPreservesEligibility(bool vertical)
    {
        _connection.Style = vertical ? MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical
                                    : MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal;
        _connection.DampedSourcePos = new Vector2(150, 100);
        var direction = vertical ? MagGraphItem.Directions.Vertical : MagGraphItem.Directions.Horizontal;
        var otherDirection = vertical ? MagGraphItem.Directions.Horizontal : MagGraphItem.Directions.Vertical;
        var ordinaryVertices = DrawVertices();
        _context.ItemMovement.SpliceSets.Add(Splice(otherDirection, typeof(float)));
        _context.ItemMovement.SpliceSets.Add(Splice(direction, typeof(int)));
        Assert.Equal(ordinaryVertices, DrawVertices());
        _context.ItemMovement.SpliceSets.Add(Splice(direction, typeof(float)));
        Assert.True(DrawVertices() > ordinaryVertices);
        _context.ItemMovement.DraggedItems.Add(_connection.SourceItem);
        Assert.Equal(ordinaryVertices, DrawVertices());

        int DrawVertices()
        {
            var before = _drawList.VtxBuffer.Size;
            _draw(_connection, _drawList, _context, null);
            return _drawList.VtxBuffer.Size - before;
        }
    }

    /// <summary>Stack boundary updates reuse their sorting storage after graph construction.</summary>
    [Fact]
    public void StackBoundaryUpdatesDoNotAllocate()
    {
        var update = typeof(MagGraphLayout).GetMethod("ComputeVerticalStackBoundaries", BindingFlags.Instance | BindingFlags.NonPublic)!
                                          .CreateDelegate<Action<ScalableCanvas>>(_context.Layout);
        for (var i = 0; i < 256; i++)
            update(_canvas);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            update(_canvas);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(0, allocated);
    }

    /// <summary>Equal sort positions retain layout enumeration order; movement recomputes extents and anchor breaks.</summary>
    [Fact]
    public void StackLayoutPreservesTiesAndRespondsToMovement()
    {
        var ordinary = _home.Symbol.Children[_connection.SourceItem.Id].Symbol;
        var reroute = _package.Add(typeof(Types.Routing.TestReroute<float>)).Symbol;
        var a = _home.AddChild(ordinary, Guid.NewGuid(), new Vector2(5000, 0), new Vector2(110, 25));
        var b = _home.AddChild(ordinary, Guid.NewGuid(), new Vector2(5000, 25), new Vector2(110, 25));
        var anchor = _home.AddChild(reroute, Guid.NewGuid(), new Vector2(5000, 50), MagGraphItem.RerouteSize);
        var c = _home.AddChild(ordinary, Guid.NewGuid(), new Vector2(5000, 75), new Vector2(110, 25));
        var d = _home.AddChild(ordinary, Guid.NewGuid(), new Vector2(5000, 100), new Vector2(110, 25));
        var e = _home.AddChild(ordinary, Guid.NewGuid(), new Vector2(5300, 0), new Vector2(110, 25));
        var tiedAnchor = _home.AddChild(reroute, Guid.NewGuid(), new Vector2(5300, 0), MagGraphItem.RerouteSize);
        var f = _home.AddChild(ordinary, Guid.NewGuid(), new Vector2(5300, 0), new Vector2(110, 25));
        var g = _home.AddChild(ordinary, Guid.NewGuid(), new Vector2(5300, 25), new Vector2(110, 25));
        _context.Layout.ComputeLayout(_context, true);
        _context.Layout.ComputeLayout(_context);
        // Symbol loading may enumerate children concurrently; give the layout a known order for tied positions.
        var ids = new[] { _connection.SourceItem.Id, _connection.TargetItem.Id, a.Id, b.Id, anchor.Id, c.Id, d.Id,
                          e.Id, tiedAnchor.Id, f.Id, g.Id };
        var items = ids.Select(id => _context.Layout.Items[id]).ToArray();
        _context.Layout.Items.Clear();
        foreach (var item in items)
            _context.Layout.Items.Add(item.Id, item);
        _context.Layout.ComputeLayout(_context);
        AssertStack(a.Id, b.Id);
        AssertStack(anchor.Id, anchor.Id);
        AssertStack(c.Id, d.Id);
        AssertStack(e.Id, e.Id);
        AssertStack(tiedAnchor.Id, tiedAnchor.Id);
        AssertStack(f.Id, g.Id);

        b.PosOnCanvas += new Vector2(20, 0);
        _context.Layout.ComputeLayout(_context);
        AssertStack(a.Id, a.Id);
        AssertStack(b.Id, b.Id);
        AssertStack(c.Id, d.Id);

        void AssertStack(Guid firstId, Guid lastId)
        {
            var first = _context.Layout.Items[firstId];
            var last = _context.Layout.Items[lastId];
            Assert.Equal(first.PosOnCanvas, first.VerticalStackArea.Min);
            Assert.Equal(last.Area.Max, first.VerticalStackArea.Max);
            Assert.Equal(first.VerticalStackArea.Min, last.VerticalStackArea.Min);
            Assert.Equal(first.VerticalStackArea.Max, last.VerticalStackArea.Max);
        }
    }

    /// <summary>Builds a candidate whose slot identities do not affect drawing eligibility.</summary>
    /// <param name="direction">Splice orientation to use in the test candidate.</param>
    /// <param name="type">Value type that the candidate accepts and produces.</param>
    /// <returns>Synthetic splice candidate with fresh identities and the requested direction and type.</returns>
    private static MagItemMovement.SpliceLink Splice(MagGraphItem.Directions direction, Type type)
    {
        return new MagItemMovement.SpliceLink(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                                              direction, type, 0, Vector2.Zero, Vector2.Zero);
    }
}
