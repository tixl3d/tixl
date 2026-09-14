#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The one edit gesture a view runs at a time — every drag on every canvas (corner pins, edge crops, region
/// and patch edits, slice rects, measuring lines, Board cards, traces, reference points) goes through the same
/// three beats: <see cref="BeginGesture"/> snapshots the setup, the caller applies its edit live each frame,
/// <see cref="EndGesture"/> turns the difference into one undo step and one save. What was hot, and the hot
/// surface's pre-drag geometry (for re-basing a drag and freezing the rectify basis), ride along in
/// <see cref="_gesture"/>, so no edit needs a state machine of its own. Callers drive the beats with a
/// switch on the handle's drag phase — never with per-frame lambdas, which would allocate while a handle is live.
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
        Annotation,     // a measuring line's endpoint
        AnnotationDraft,// a new measuring line being drawn
        BoardCard,      // Board cards moving
        PlanVertex,     // a floor plan's corner on its Board card
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
        public SurfaceRectSnapshot? Snapshot;

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

    /// <summary>What a press was on, for the press → drag handoff.</summary>
    private enum PressOrigins
    {
        None,
        BoardCard,  // a card (or prop) on the Board; the drag moves the selected cards
        Label,      // a surface's, region's or patch's name chip on a canvas; the drag moves that entity
    }

    /// <summary>
    /// A press that selected something and may still become a drag: the button is down, and the move
    /// machinery watches for it to travel past the click threshold. One at a time for the whole view, so
    /// arming a new one — or any canvas taking the press for its own gesture — cancels the previous.
    /// </summary>
    private struct PressHandoff
    {
        public Vector2 ScreenPos;
        public PressOrigins Origin;
        public SetupEntityKinds Kind;
        public Guid Id;

        /// <summary>A plain press on an already selected card keeps the whole selection, so the drag moves the group.</summary>
        public bool KeepsSelection;

        public readonly bool IsArmed => Origin != PressOrigins.None;

        public void Arm(Vector2 screenPos, PressOrigins origin, SetupEntityKinds kind, Guid id, bool keepsSelection)
        {
            ScreenPos = screenPos;
            Origin = origin;
            Kind = kind;
            Id = id;
            KeepsSelection = keepsSelection;
        }

        public void Cancel()
        {
            this = default;
        }

        /// <summary>
        /// True while the armed press is held past <paramref name="threshold"/> — the moment it has become a
        /// drag rather than a click. Below the threshold a press is a selection click; promoting it would fire
        /// zero-distance "moves" when switching entities by clicking. The caller takes the press with <see cref="Cancel"/>.
        /// </summary>
        public readonly bool TryPromote(float threshold)
        {
            return IsArmed
                   && ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                   && (ImGui.GetMousePos() - ScreenPos).Length() > threshold;
        }
    }

    /// <summary>
    /// Takes the armed label press as a drag once it has travelled past the click threshold — but only when it
    /// began on <paramref name="label"/>'s chip in <paramref name="screenQuad"/>, and no gesture is live.
    /// </summary>
    private bool TryTakeLabelGrab(ReadOnlySpan<Vector2> screenQuad, string label)
    {
        if (string.IsNullOrEmpty(label))
            return false;

        var (min, max) = CornerPinHandles.GetCenteredLabelRect(screenQuad, label);
        return TryTakeLabelGrab(min, max);
    }

    /// <summary>As <see cref="TryTakeLabelGrab(ReadOnlySpan{Vector2},string)"/>, for a press anywhere inside a screen rect.</summary>
    private bool TryTakeLabelGrab(Vector2 screenMin, Vector2 screenMax)
    {
        if (_gesture.IsLive || _pressHandoff.Origin != PressOrigins.Label
            || !_pressHandoff.TryPromote(UserSettings.Config.ClickThreshold)
            || !CanvasDraw.Contains(screenMin, screenMax, _pressHandoff.ScreenPos))
            return false;

        _pressHandoff.Cancel();
        return true;
    }

    /// <summary>
    /// A press that never became a drag must not linger. A plain press on an already selected card kept the
    /// selection (for a group drag), so it is the release that selects that card alone.
    /// </summary>
    private void ReleasePressHandoff(SetupEntitySelection? selection)
    {
        if (!_pressHandoff.IsArmed || ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;

        if (_pressHandoff.Origin == PressOrigins.BoardCard && _pressHandoff.KeepsSelection)
            selection?.Select(_pressHandoff.Kind, _pressHandoff.Id);

        _pressHandoff.Cancel();
    }

    private void BeginGesture(Setup setup, GestureKinds kind, string name, Guid hotId, Surface? snapshotOf = null, Vector2 grabPoint = default)
    {
        _gesture = new Gesture
                       {
                           Kind = kind,
                           HotId = hotId,
                           Name = name,
                           OldJson = setup.ToJsonString(),
                           Snapshot = snapshotOf != null ? new SurfaceRectSnapshot(snapshotOf) : null,
                           GrabPoint = grabPoint,
                       };

        // A gesture with a surface snapshot is one that moves its pins; its marks must be placed before they move.
        if (snapshotOf != null)
            SeedPointAims(setup, snapshotOf);
    }

    /// <summary>
    /// Arms the live gesture to keep the hot surface's pixels in place: resolves (and if shared, clones) its slice
    /// and remembers the UV to re-derive from. Call right after <see cref="BeginGesture"/> for a plain crop or a pan.
    /// </summary>
    private void BeginContentEdit(Setup setup, Surface surface)
    {
        if (SliceUvAnchoring.TryBegin(setup, surface, out var sliceId, out var uvStart))
        {
            _gesture.ContentSliceId = sliceId;
            _gesture.ContentUvStart = uvStart;
        }
    }

    /// <summary>After a re-based rect edit: keeps the pixels where the pre-drag rect showed them.</summary>
    private void KeepContentInPlace(Setup setup, Vector2 oldMin, Vector2 oldMax, Vector2 newMin, Vector2 newMax)
    {
        if (_gesture.EditsContent)
            SliceUvAnchoring.ApplyCrop(setup, _gesture.ContentSliceId, _gesture.ContentUvStart, oldMin, oldMax, newMin, newMax);
    }

    /// <summary>One undo step for whatever the gesture changed (none for a click that moved nothing), one save.</summary>
    private void EndGesture(Setup setup)
    {
        if (_gesture.OldJson != null)
            SetupUndo.CommitGesture(setup, _gesture.Name, _gesture.OldJson);

        _gesture = default;
    }

    /// <summary>Drops the gesture without an undo step — for a press that turned out to be something else (a double-click).</summary>
    private void CancelGesture()
    {
        _gesture = default;
    }

    // The one live gesture, and the press that may still become one.
    private Gesture _gesture;
    private PressHandoff _pressHandoff;
}
