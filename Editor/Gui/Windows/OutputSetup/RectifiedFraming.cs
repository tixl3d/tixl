#nullable enable
using T3.Core.Output;
using T3.Editor.Gui.Interaction.CanvasEditing;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The output canvas' rectify transform R (output px → view px) and the window of the rectified plane the
/// canvas renders. R is identity at straighten 0 and by 1 maps the basis surface's quad onto its own
/// axis-aligned, anchored rectangle at true scale, carrying the whole composite with it; the framing
/// interpolates from the whole canvas to that rectangle plus a surround. Pure maths — no ImGui — so a frame's
/// transform can be reasoned about (and held) apart from the drawing.
/// </summary>
internal struct RectifiedFraming
{
    /// <summary>
    /// What keeps the straight framing still: the held window, and the freeze bookkeeping that decides whether
    /// a lifted freeze eases (same basis, window kept) or the window re-derives (new basis).
    /// </summary>
    public struct FramingHold
    {
        /// <summary>The settled straight framing, held across edits; null = re-derive on the next rectified frame.</summary>
        public Vector2? FrozenMin;

        public Vector2 FrozenMax;

        /// <summary>Post-edit settle on the same basis: ease R, but hold the framing window.</summary>
        public bool EaseKeepsFraming;

        /// <summary>Last frame's framing freeze, so the frame the freeze lifts adopts the new framing without moving.</summary>
        public bool WasFrozen;

        /// <summary>Last frame's basis freeze, so a lifted freeze can ease instead of jumping.</summary>
        public bool BasisWasFrozen;

        public bool BasisHasLast;
    }

    /// <summary>Fraction of the basis surface's straightened size kept as surround margin (context) around it.</summary>
    public const float StraightSurroundFactor = 0.4f;

    public Homography RectifiedToView;
    public Homography RectifiedToOutput;

    /// <summary>The rendered window in rectified px: its top-left, and its size.</summary>
    public Vector2 ViewMin;

    public Vector2 ViewSize;

    /// <summary>The basis surface's straightened rectangle in output px — what Straight settles on.</summary>
    public Vector2 StraightMin;

    public Vector2 StraightMax;

    /// <summary>No usable basis this frame: R is identity and the window is the whole canvas.</summary>
    public void Reset(Vector2 canvasSize)
    {
        RectifiedToView = Homography.Identity;
        RectifiedToOutput = Homography.Identity;
        ViewMin = Vector2.Zero;
        ViewSize = canvasSize;
    }

    /// <param name="basisQuad">The basis surface's corner pin, as fractions of the canvas.</param>
    /// <param name="basisSize">Its size in metres; with <paramref name="anchor"/> and <paramref name="pixelsPerMeter"/>
    /// this gives the straightened rectangle its aspect, scale and placement.</param>
    /// <param name="viewSettled">Whether the view morph has settled — held framing is only ever captured at rest.</param>
    /// <param name="basisSettled">Whether the basis transition has settled (a post-edit settle on the same basis keeps the hold regardless).</param>
    /// <param name="hold">The view's framing hold, captured and released here; it outlives any one frame's framing.</param>
    public void Compute(ReadOnlySpan<Vector2> basisQuad, Vector2 basisSize, Vector2 anchor, float pixelsPerMeter, Vector2 canvasSize,
                        float straighten, bool viewSettled, bool basisSettled, ref FramingHold hold)
    {
        // Mappings are stored as fractions of the canvas; this view works in its pixels (so does R, and the
        // straightened rect it lands on, which is metres × px/m). Convert once, here.
        Span<Vector2> basisPx = stackalloc Vector2[4];
        for (var c = 0; c < 4; c++)
            basisPx[c] = basisQuad[c] * canvasSize;

        CanvasDraw.Bounds(basisPx, out var quadMin, out var quadMax);

        // Straightening lands on the surface's real content canvas (metres × px/m) — so Size (m) is what
        // gives the rectangle its aspect. Anchored at the anchor, so changing a dimension extends the rect
        // from there rather than recentring it.
        var straightSize = new Vector2(MathF.Max(basisSize.X, 0.001f), MathF.Max(basisSize.Y, 0.001f)) * MathF.Max(pixelsPerMeter, 1f);
        Span<Vector2> stageTarget = stackalloc Vector2[4];
        WriteAnchoredRect(quadMin, quadMax, anchor, straightSize, stageTarget);
        CanvasDraw.Bounds(stageTarget, out StraightMin, out StraightMax);

        Span<Vector2> interp = stackalloc Vector2[4];
        for (var c = 0; c < 4; c++)
            interp[c] = Vector2.Lerp(basisPx[c], stageTarget[c], straighten);

        if (!Homography.TryComputeQuadToQuad(basisPx, interp, out RectifiedToView)
            || !Homography.TryComputeQuadToQuad(interp, basisPx, out RectifiedToOutput))
        {
            RectifiedToView = Homography.Identity;
            RectifiedToOutput = Homography.Identity;
            ViewMin = Vector2.Zero;
            ViewSize = canvasSize;
            return;
        }

        // Frame to the focused surface's straightened bounds + margin — not the whole warped canvas, which a
        // steep rectify sends toward infinity. Interpolated from the full canvas at t=0.
        CanvasDraw.Bounds(interp, out var focusMin, out var focusMax);

        // Uniform surround from the larger dimension, not per-axis: a thin surface (a beam, a strip) has a
        // near-zero short axis, and a per-axis margin there collapses the frame onto the surface, clipping
        // the neighbouring surfaces' content out of the warped composite. Off the long side it stays generous.
        var focusSpan = focusMax - focusMin;
        var surround = new Vector2(MathF.Max(focusSpan.X, focusSpan.Y) * StraightSurroundFactor);
        var framedMin = focusMin - surround;
        var framedMax = focusMax + surround;

        // Once the view and basis transitions have settled, the framing — the world window this rectified
        // view renders — stays put across edits and releases: a dragged surface stays where it was dropped
        // instead of the window re-centering on it. Re-framing comes only from a basis or mode change;
        // anything else is the user's own pan/zoom. R itself stays live, so corner edits still update the
        // rectification within the held window. Held framing is only ever captured *at* the settled state —
        // capturing during a transition would freeze a half-way window. A post-edit settle ease (same basis)
        // keeps the hold, so releasing a drag never moves the camera; a basis/mode transition re-derives live.
        var framingHeld = viewSettled && (basisSettled || hold.EaseKeepsFraming);
        if (!framingHeld)
        {
            hold.FrozenMin = null;
        }
        else if (hold.FrozenMin == null)
        {
            hold.FrozenMin = framedMin;
            hold.FrozenMax = framedMax;
        }
        else
        {
            framedMin = hold.FrozenMin.Value;
            framedMax = hold.FrozenMax;
        }

        ViewMin = Vector2.Lerp(Vector2.Zero, framedMin, straighten);
        var viewMax = Vector2.Lerp(canvasSize, framedMax, straighten);
        ViewSize = viewMax - ViewMin;
    }

    /// <summary>
    /// An axis-aligned rect of <paramref name="size"/> placed so its anchor coincides with the same anchor of
    /// the reference box — so resizing extends the rect from the anchor instead of recentring it. The anchor
    /// is signed and Y-up, while canvas Y grows downward. Writes TL, TR, BR, BL.
    /// </summary>
    private static void WriteAnchoredRect(Vector2 refMin, Vector2 refMax, Vector2 anchor, Vector2 size, Span<Vector2> corners)
    {
        var t = (anchor + Vector2.One) * 0.5f;
        var anchorX = refMin.X + t.X * (refMax.X - refMin.X);
        var anchorY = refMax.Y - t.Y * (refMax.Y - refMin.Y);

        var minX = anchorX - t.X * size.X;
        var maxX = minX + size.X;
        var maxY = anchorY + t.Y * size.Y;
        var minY = maxY - size.Y;

        SurfaceGeometry.WriteRectCorners(new Vector2(minX, minY), new Vector2(maxX, maxY), corners, yUp: false);
    }
}
