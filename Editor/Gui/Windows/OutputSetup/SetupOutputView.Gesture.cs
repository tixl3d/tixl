#nullable enable
using T3.Core.Output;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.UiModel.Commands.Setup;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The one edit gesture a view runs at a time — every drag on every canvas (corner pins, edge crops, region
/// and patch edits, slice rects, measuring lines, Board cards, traces, reference points) goes through the same
/// three beats: <see cref="BeginGesture"/> snapshots the setup, the caller applies its edit live each frame,
/// <see cref="EndGesture"/> turns the difference into one undo step and one save. What was hot, and the hot
/// surface's pre-drag geometry (for re-basing a drag and freezing the rectify basis), ride along in
/// <see cref="_gesture"/>, so no edit needs a state machine of its own.
/// </summary>
internal sealed partial class SetupOutputView
{
    private enum GestureKinds
    {
        None,
        CornerPin,      // a mapping quad's corner (possibly a group) on the projector canvas
        SurfaceMove,    // a whole mapping quad by its label
        SurfaceResize,  // an edge crop / scale, or a region's corner/edge edit — the hot surface's rect changes
        RegionMove,     // a region by its body
        ContentPan,     // the slice UV slid under a region's fixed window (modifier + body drag)
        PatchQuad,      // a patch's corner or edge
        PatchMove,      // a patch by its label
        Slice,          // a slice rect edit
        SliceDraft,     // a new slice being drawn on empty source area
        Annotation,     // a measuring line's endpoint
        AnnotationDraft,// a new measuring line being drawn
        BoardCard,      // Board cards moving
        BoardScale,     // the Board scale handle
        TraceCorner,    // a traced quad's corner on the image card / in the photo
        TraceRefine,    // the straightened rect's handles refining the trace
        ReferencePoint, // a reference point on the straightened photo
        AimPoint,       // a reference point aimed on the projector canvas
    }

    private struct Gesture
    {
        public GestureKinds Kind;
        public Guid HotId;
        public string Name;
        public string? OldJson;

        /// <summary>The hot surface's rectangle, anchor and quads at the press — what a re-based drag restores
        /// each frame, and what the rectified view freezes its basis to while the drag runs.</summary>
        public ResizeSurfaceCommand.State? Snapshot;

        /// <summary>Where the press was, in the edit's own space (a move's grab point).</summary>
        public Vector2 GrabPoint;

        /// <summary>The slice a content-aware edit co-writes (crop, pan) and its UV at the press; empty when the
        /// rect edit runs alone (nothing shown, or a stretch).</summary>
        public Guid ContentSliceId;

        public Vector4 ContentUvStart;

        public readonly bool EditsContent => ContentSliceId != Guid.Empty;

        public readonly bool IsLive => Kind != GestureKinds.None;

        public readonly bool Is(GestureKinds kind, Guid id) => Kind == kind && HotId == id;

        /// <summary>Whether this gesture edits <paramref name="surfaceId"/>'s own geometry (quad, rect, anchor).</summary>
        public readonly bool EditsSurface(Guid surfaceId)
        {
            return HotId == surfaceId
                   && Kind is GestureKinds.CornerPin or GestureKinds.SurfaceMove or GestureKinds.SurfaceResize or GestureKinds.RegionMove;
        }
    }

    private void BeginGesture(Setup setup, GestureKinds kind, string name, Guid hotId, Surface? snapshotOf = null, Vector2 grabPoint = default)
    {
        _gesture = new Gesture
                       {
                           Kind = kind,
                           HotId = hotId,
                           Name = name,
                           OldJson = setup.ToJsonString(),
                           Snapshot = snapshotOf != null ? new ResizeSurfaceCommand.State(snapshotOf) : null,
                           GrabPoint = grabPoint,
                       };
    }

    /// <summary>
    /// Arms the live gesture to keep the hot surface's pixels in place: resolves (and if shared, clones) its slice
    /// and remembers the UV to re-derive from. Call right after <see cref="BeginGesture"/> for a plain crop or a pan.
    /// </summary>
    private void BeginContentEdit(Setup setup, Surface surface)
    {
        if (CropHandling.TryBegin(setup, surface, out var sliceId, out var uvStart))
        {
            _gesture.ContentSliceId = sliceId;
            _gesture.ContentUvStart = uvStart;
        }
    }

    /// <summary>After a re-based rect edit: keeps the pixels where the pre-drag rect showed them.</summary>
    private void ApplyCropHandling(Setup setup, Vector2 oldMin, Vector2 oldMax, Vector2 newMin, Vector2 newMax)
    {
        if (_gesture.EditsContent)
            CropHandling.ApplyCrop(setup, _gesture.ContentSliceId, _gesture.ContentUvStart, oldMin, oldMax, newMin, newMax);
    }

    /// <summary>One undo step for whatever the gesture changed (none for a click that moved nothing), one save.</summary>
    private void EndGesture(Setup setup)
    {
        if (_gesture.OldJson != null)
            SetupActions.CommitGesture(setup, _gesture.Name, _gesture.OldJson);

        _gesture = default;
    }

    /// <summary>Drops the gesture without an undo step — for a press that turned out to be something else (a double-click).</summary>
    private void CancelGesture()
    {
        _gesture = default;
    }

    /// <summary>
    /// The phase-driven form: begins on Started (snapshotting <paramref name="hot"/>), applies
    /// <paramref name="onDragging"/> while live, ends on Completed. The re-basing edits restore the snapshot
    /// inside <paramref name="onDragging"/> before applying, so a long drag never compounds.
    /// </summary>
    private void RunGesture(CanvasPointHandle.DragPhase phase, Setup setup, GestureKinds kind, string name, Surface hot,
                            Action onDragging, Action? onStarted = null, Action? onCompleted = null)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                BeginGesture(setup, kind, name, hot.Id, hot);
                onStarted?.Invoke();
                break;

            case CanvasPointHandle.DragPhase.Dragging:
                if (_gesture.Is(kind, hot.Id))
                    onDragging();

                break;

            case CanvasPointHandle.DragPhase.Completed:
                if (_gesture.Is(kind, hot.Id))
                    EndGesture(setup);

                onCompleted?.Invoke();
                break;
        }
    }

    private Gesture _gesture;
}
