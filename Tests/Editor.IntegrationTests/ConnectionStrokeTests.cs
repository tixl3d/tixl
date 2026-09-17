using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>Exercises stroke observation against native ImGui paths and real graph commands.</summary>
[Collection("EditorModel")]
public sealed partial class ConnectionStrokeTests : IDisposable
{
    /// <summary>Creates an isolated editable graph with a single ordinary wire and no GPU background.</summary>
    public ConnectionStrokeTests()
    {
        // The UI starts a long-lived OSC listener; do not attach it to xUnit's constructor completion tracking.
        var synchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        try { _ = T3Ui.UiScaleFactor; }
        finally { SynchronizationContext.SetSynchronizationContext(synchronizationContext); }
        _imgui = ImGui.CreateContext();
        _package = new RerouteVisibilityTests.TestPackage();
        _home = _package.Add(typeof(RerouteVisibilityTests.EmptyComposition));
        var op = _package.Add(typeof(RerouteVisibilityTests.OrdinaryFloat));
        var source = _home.AddChild(op.Symbol, Guid.NewGuid(), new Vector2(10, 100), new Vector2(110, 25));
        var target = _home.AddChild(op.Symbol, Guid.NewGuid(), new Vector2(400, 100), new Vector2(110, 25));
        _home.Symbol.AddConnection(new Symbol.Connection(source.Id, op.Symbol.OutputDefinitions[0].Id,
                                                        target.Id, op.Symbol.InputDefinitions[0].Id), 0);
        Assert.True(OpenedProject.TryCreateWithExplicitHome(_package, _home.Symbol.Id, out var opened, out _));
        var history = new NavigationHistory(opened.Structure);
        var selection = new NodeSelection(history, opened.Structure);
        // Connection drawing only reads the background's focus flag; it does not evaluate images.
        var background = (GraphImageBackground)RuntimeHelpers.GetUninitializedObject(typeof(GraphImageBackground));
        var view = new ProjectView(opened, history, selection, background);
        _canvas = (MagGraphView)Activator.CreateInstance(typeof(MagGraphView), BindingFlags.Instance | BindingFlags.NonPublic,
                                                        null, [view], null)!;
        view.GraphView = _canvas;
        _context = (GraphUiContext)typeof(MagGraphView).GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_canvas)!;
        _context.Layout.ComputeLayout(_context, true);
        _connection = Assert.Single(_context.Layout.MagConnections);
        _draw = typeof(MagGraphView).GetMethod("DrawConnection", BindingFlags.NonPublic | BindingFlags.Instance)!
                                   .CreateDelegate<Action<MagGraphConnection, ImDrawListPtr, GraphUiContext, ConnectionStroke?>>(_canvas);
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1280, 720);
        io.Fonts.AddFontDefault();
        io.Fonts.Build();
        ImGui.NewFrame();
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(io.DisplaySize);
        ImGui.Begin("Stroke checks", ImGuiWindowFlags.NoSavedSettings);
        _drawList = ImGui.GetWindowDrawList();
        _drawList.PushClipRect(Vector2.Zero, new Vector2(500, 500), false);
    }

    /// <summary>Hit highlighting preserves the pending native path and detects the final movement before commit.</summary>
    [Fact]
    public void ObservedPathSurvivesHighlightAndReleaseCommit()
    {
        var stroke = _context.ConnectionStroke;
        stroke.Begin(_context, true, new Vector2(150, 60));
        stroke.SetConnection(_connection);
        SetHorizontalPath();
        stroke.ObservePath(_drawList);
        Assert.False(stroke.Contains(_connection));
        stroke.UpdatePosition(new Vector2(150, 140), 2);
        var before = new[] { _drawList._Path[0], _drawList._Path[1] };
        stroke.ObservePath(_drawList);
        Assert.True(stroke.Contains(_connection));
        Assert.Equal(2, _drawList._Path.Size);
        Assert.Equal(before, new[] { _drawList._Path[0], _drawList._Path[1] });
        Assert.True(stroke.Commit(_context, out var error), error);
        Assert.Empty(_home.Symbol.Connections);
        UndoRedoStack.Undo();
        Assert.Single(_home.Symbol.Connections);
        UndoRedoStack.Redo();
        Assert.Empty(_home.Symbol.Connections);
        stroke.Cancel();
        Assert.False(stroke.IsActive);
        Assert.Null(typeof(ConnectionStroke).GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(stroke));
    }

    /// <summary>Invisible cable portions and markers cannot receive hits outside the clip rectangle.</summary>
    [Fact]
    public void ClippingRejectsHiddenPathsAndMarkers()
    {
        var stroke = _context.ConnectionStroke;
        _drawList.PushClipRect(Vector2.Zero, new Vector2(120, 200), false);
        stroke.Begin(_context, false, new Vector2(150, 60));
        stroke.UpdatePosition(new Vector2(150, 140), 2);
        stroke.SetConnection(_connection);
        SetHorizontalPath();
        stroke.ObservePath(_drawList);
        Assert.False(stroke.Contains(_connection));
        stroke.DrawMarker(_drawList, new Vector2(150, 100), 1);
        Assert.False(stroke.Contains(_connection));
        stroke.Cancel();
        stroke.Begin(_context, false, new Vector2(118, 60));
        stroke.UpdatePosition(new Vector2(118, 140), 2);
        stroke.SetConnection(_connection);
        stroke.DrawMarker(_drawList, new Vector2(125, 100), 1);
        Assert.True(stroke.Contains(_connection));
        _drawList.PopClipRect();
    }

    /// <summary>Temporary and fully collapsed wires receive no observer; snapped persistent markers remain hittable.</summary>
    [Fact]
    public void DrawingExcludesTemporaryAndHiddenWires()
    {
        var stroke = _context.ConnectionStroke;
        _connection.Style = MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal;
        _connection.DampedSourcePos = new Vector2(150, 100);
        stroke.Begin(_context, false, _canvas.TransformPosition(new Vector2(150, 60)));
        stroke.UpdatePosition(_canvas.TransformPosition(new Vector2(150, 140)), 2);
        _connection.IsTemporary = true;
        _draw(_connection, _drawList, _context, null);
        Assert.False(stroke.Contains(_connection));
        _connection.IsTemporary = false;
        _connection.SourceItem.ChildUi!.HiddenInCollapsedSectionId = Guid.NewGuid();
        _connection.TargetItem.ChildUi!.HiddenInCollapsedSectionId = Guid.NewGuid();
        _draw(_connection, _drawList, _context, stroke);
        Assert.False(stroke.Contains(_connection));
        _connection.SourceItem.ChildUi.HiddenInCollapsedSectionId = Guid.Empty;
        _connection.TargetItem.ChildUi.HiddenInCollapsedSectionId = Guid.Empty;
        _draw(_connection, _drawList, _context, stroke);
        Assert.True(stroke.Contains(_connection));
        stroke.SetConnection(null);
    }

    /// <summary>Cables crossing a collapsed section boundary are observed at their visible redirected endpoints.</summary>
    /// <param name="sourceCollapsed">Whether the source endpoint, rather than the target endpoint, is inside the collapsed section.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SectionBoundaryWiresRemainHittable(bool sourceCollapsed)
    {
        var section = new Section { Id = Guid.NewGuid(), PosOnCanvas = new Vector2(200, 200), Size = new Vector2(110, 80) };
        _context.Layout.Sections[section.Id] = new MagGraphSection { Id = section.Id, Section = section };
        (sourceCollapsed ? _connection.SourceItem : _connection.TargetItem).ChildUi!.HiddenInCollapsedSectionId = section.Id;
        _connection.Style = MagGraphConnection.ConnectionStyles.RightToLeft;
        _connection.DampedSourcePos = new Vector2(120, 112.5f);
        _connection.DampedTargetPos = new Vector2(400, 112.5f);
        var x = sourceCollapsed ? 350 : 170;
        var stroke = _context.ConnectionStroke;
        stroke.Begin(_context, false, _canvas.TransformPosition(new Vector2(x, 20)));
        stroke.UpdatePosition(_canvas.TransformPosition(new Vector2(x, 400)), 2);
        _draw(_connection, _drawList, _context, stroke);
        Assert.True(stroke.Contains(_connection));
        stroke.SetConnection(null);
    }

    /// <summary>Read-only state and graph edits invalidate a stroke before it can commit.</summary>
    [Fact]
    public void ChangedOrReadOnlyGraphsRejectCommit()
    {
        var stroke = _context.ConnectionStroke;
        stroke.Begin(_context, true, new Vector2(150, 60));
        Assert.True(stroke.IsCurrent(_context));
        _package.ReadOnly = true;
        Assert.False(stroke.IsCurrent(_context));
        Assert.False(stroke.Commit(_context, out _));
        _package.ReadOnly = false;
        _home.FlagAsModified();
        Assert.False(stroke.IsCurrent(_context));
        Assert.Single(_home.Symbol.Connections);
    }

    /// <summary>Cancellation keeps modified RMB reserved through release, including when another view receives input.</summary>
    /// <param name="cancellation">Cancellation route to exercise: Escape, focus loss, or input handled by another view.</param>
    [Theory]
    [InlineData("escape")]
    [InlineData("focus")]
    [InlineData("other view")]
    public void CancelledGestureKeepsMouseReservedUntilRelease(string cancellation)
    {
        SetCanvasFlag(_canvas, "IsFocused", true);
        SetCanvasFlag(_canvas, "IsHovered", true);
        NextFrame(() =>
                  {
                      ImGui.GetIO().AddKeyEvent(ImGuiKey.ModAlt, true);
                      ImGui.GetIO().AddMouseButtonEvent(1, true);
                  });
        InvokeCanvas(_canvas, "UpdateConnectionStrokeInput");
        Assert.True(_context.ConnectionStroke.IsActive);
        Assert.True(_canvas.ConsumesConnectionStrokeMouse);
        var scroll = _canvas.Scroll;
        if (cancellation == "escape")
            NextFrame(() => ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape, true));
        else
        {
            NextFrame();
            SetCanvasFlag(_canvas, cancellation == "focus" ? "IsFocused" : "IsHovered", false);
        }
        if (cancellation == "other view")
        {
            var other = (MagGraphView)Activator.CreateInstance(typeof(MagGraphView), BindingFlags.Instance | BindingFlags.NonPublic,
                                                               null, [_context.ProjectView], null)!;
            SetCanvasFlag(other, "IsFocused", true);
            SetCanvasFlag(other, "IsHovered", true);
            InvokeCanvas(other, "UpdateConnectionStrokeInput");
            Assert.True(other.ConsumesConnectionStrokeMouse);
            var otherContext = (GraphUiContext)typeof(MagGraphView).GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(other)!;
            Assert.False(otherContext.ConnectionStroke.IsActive);
        }
        InvokeCanvas(_canvas, "UpdateConnectionStrokeInput");
        Assert.False(_context.ConnectionStroke.IsActive);
        Assert.True(_canvas.ConsumesConnectionStrokeMouse);
        Assert.Equal(scroll, _canvas.Scroll);
        NextFrame(() =>
                  {
                      ImGui.GetIO().AddMouseButtonEvent(1, false);
                      ImGui.GetIO().AddKeyEvent(ImGuiKey.ModAlt, false);
                      ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape, false);
                  });
        InvokeCanvas(_canvas, "UpdateConnectionStrokeInput");
        InvokeCanvas(_canvas, "FinishConnectionStrokeInput");
        Assert.True(_canvas.ConsumesConnectionStrokeMouse);
        NextFrame();
        InvokeCanvas(_canvas, "UpdateConnectionStrokeInput");
        Assert.False(_canvas.ConsumesConnectionStrokeMouse);
        Assert.Single(_home.Symbol.Connections);
    }

    /// <summary>Advances input through native ImGui frame boundaries.</summary>
    /// <param name="input">Optional callback that queues native input before the next ImGui frame begins.</param>
    private void NextFrame(Action? input = null)
    {
        _drawList.PopClipRect();
        ImGui.End();
        ImGui.EndFrame();
        input?.Invoke();
        ImGui.NewFrame();
        ImGui.Begin("Stroke checks", ImGuiWindowFlags.NoSavedSettings);
        _drawList.PushClipRect(Vector2.Zero, new Vector2(500, 500), false);
    }

    /// <summary>Sets the focus state normally supplied by DrawGraph without evaluating unrelated editor panels.</summary>
    /// <param name="canvas">Graph canvas whose private interaction flag is set.</param>
    /// <param name="name">Name of the canvas flag to set.</param>
    /// <param name="value">Boolean state assigned to the flag.</param>
    private static void SetCanvasFlag(MagGraphView canvas, string name, bool value)
        => typeof(MagGraphView).GetProperty(name)!.SetValue(canvas, value);

    /// <summary>Calls the existing frame-level input handler without adding a production test API.</summary>
    /// <param name="canvas">Graph canvas on which to invoke the private interaction method.</param>
    /// <param name="name">Name of the parameterless canvas method to invoke.</param>
    private static void InvokeCanvas(MagGraphView canvas, string name)
        => typeof(MagGraphView).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(canvas, null);

    /// <summary>Builds a simple native path whose clipping and preservation are independently inspectable.</summary>
    private void SetHorizontalPath()
    {
        _drawList.PathClear();
        _drawList.PathLineTo(new Vector2(100, 100));
        _drawList.PathLineTo(new Vector2(200, 100));
    }

    /// <summary>Closes native state and the UI's background listener so test completion does not await it.</summary>
    public void Dispose()
    {
        _context.ConnectionStroke.Cancel();
        _drawList.PopClipRect();
        ImGui.End();
        ImGui.EndFrame();
        ImGui.DestroyContext(_imgui);
        UndoRedoStack.Clear();
        _package.Dispose();
        T3Ui.OscDataRecording.Dispose();
    }

    /// <summary>Owned native context for this test case.</summary>
    private readonly IntPtr _imgui;
    /// <summary>In-memory package and its graph presentation.</summary>
    private readonly RerouteVisibilityTests.TestPackage _package;
    /// <summary>Editable composition symbol containing the fixture graph.</summary>
    private readonly SymbolUi _home;
    /// <summary>Actual canvas, graph context, and wire under test.</summary>
    private readonly MagGraphView _canvas;
    /// <summary>Real graph context exercised by drawing and gesture tests.</summary>
    private readonly GraphUiContext _context;
    /// <summary>Persistent fixture wire supplied to the actual renderer.</summary>
    private readonly MagGraphConnection _connection;
    /// <summary>Native draw list and a bound delegate to the existing private wire drawer.</summary>
    private readonly ImDrawListPtr _drawList;
    /// <summary>Delegate bound to the production private connection drawing method.</summary>
    private readonly Action<MagGraphConnection, ImDrawListPtr, GraphUiContext, ConnectionStroke?> _draw;
}
