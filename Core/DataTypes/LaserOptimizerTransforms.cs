using System;
using System.Collections.Generic;

namespace T3.Core.DataTypes;

/// <summary>
/// The laser optimizer as a static, stateless-per-frame transform — the exact algorithm of the
/// [LaserOptimizer] operator, lifted so the output-setup pipeline and the operator share one
/// implementation. Reorders lit segments into a greedy nearest-neighbour tour (reversing segments
/// when that is closer), inserts distance-scaled blanked jumps between shapes and dwells on sharp
/// corners. Callers own the scratch buffers; nothing carries semantic state across frames.
/// </summary>
public static class LaserOptimizerTransforms
{
    /// <summary>Hard cap on generated points (1M points = 24 MB scratch) - bounds damage from
    /// pathological input, mirroring LaserPointsToBuffer.MaxPoints.</summary>
    public const int MaxOutputPoints = 1 << 20;

    public struct Config
    {
        /// <summary>false = keep the original point order, applying only blanking and corner dwelling.</summary>
        public bool EnableOptimization;

        /// <summary>ILDA units per blanked-jump segment; the blank count scales with jump length.</summary>
        public float MaxJumpDistance;

        /// <summary>Maximum blanked points inserted before a shape start (0..100).</summary>
        public int BlankingDelayPoints;

        /// <summary>Corners sharper than this angle at which the beam lingers.</summary>
        public float CornerAngleThreshold;

        /// <summary>Repeated points at a corner (1..10).</summary>
        public int CornerAnchorCount;

        /// <summary>µs of lit dwell at every shape's first sample (the scanner's motion
        /// response after a jump - the beam pauses before drawing). 0 = off. Converted to
        /// points at <see cref="PointsPerSecond"/>, on top of the blanking's own settle.</summary>
        public float MotionResponseUs;

        /// <summary>µs of extra dwell at sharp corners (the scanner's settle time there),
        /// added on top of <see cref="CornerAnchorCount"/>. 0 = off.</summary>
        public float CornerSettleUs;

        /// <summary>µs of extra blanked settle at shape entries (the transition hold between
        /// shapes), appended to the blanking ramp's own settle. 0 = off.</summary>
        public float TransitionUs;

        /// <summary>Scan rate (points per second) the µs budgets convert at; 0 skips the µs passes.
        /// Defaults to the classic 30 kpps when the caller doesn't resolve the rate.</summary>
        public int PointsPerSecond;

        public static Config Default => new()
                                           {
                                               EnableOptimization = true,
                                               MaxJumpDistance = 5000f,
                                               BlankingDelayPoints = 5,
                                               CornerAngleThreshold = 30f,
                                               CornerAnchorCount = 3,
                                               PointsPerSecond = 30000,
                                           };
    }

    /// <summary>Per-caller scratch buffers, reused across frames to avoid allocation. Not thread-safe:
    /// one instance per consumer (the operator, each output op).</summary>
    public sealed class Scratch
    {
        public LaserPoint[] Output = new LaserPoint[0];
        public readonly List<int> SegmentStartIndices = [];
        public readonly List<int> SegmentEndIndices = [];
        public readonly List<int> TourOrder = [];

        /// <summary>Borrowed buffer for the corner-dwell pass, so it does not allocate a frame copy.</summary>
        public LaserPoint[] Temp = new LaserPoint[0];

        /// <summary>The segments not yet toured, as a swap-remove array - the sequential query
        /// scans this compact list instead of chasing grid linked lists. RemainingPosition maps
        /// segment id to its index in Remaining, so removing one is O(1).</summary>
        public int[] Remaining = new int[0];
        public int[] RemainingPosition = new int[0];
        public int RemainingCount;

        // Uniform grid over the scan field for the greedy tour on dense frames: cell -> first entry,
        // entry -> next entry, entry -> segment index (entries are 2 per segment: start then end).
        // GridPrev/GridEntryCell let a consumed segment be unlinked from its cells, so later
        // queries stop re-traversing visited entries. The grid maps the CONTENT's bounding box
        // (not the whole scanner field), so a frame concentrated into a small area - a wireframe
        // seen small - still spreads thinly across the cells. GridCellLength tracks each cell's
        // list length; should a frame still pile up in a few cells, the tour goes sequential.
        public int[] GridHead = new int[0];
        public int[] GridNext = new int[0];
        public int[] GridPrev = new int[0];
        public int[] GridEntrySegment = new int[0];
        public int[] GridEntryCell = new int[0];
        public int[] GridCellLength = new int[0];
        public int MaxCellEntries;
        public float GridOriginX;
        public float GridOriginY;
        public float GridCellSizeCurrent = GridCellSize;
        public readonly List<int> GridUsedCells = [];
    }

    /// <summary>
    /// Runs the optimizer over <paramref name="input"/> into a freshly sized result array.
    /// Returns an empty array for empty input. A bad frame degrades (bounded writes), never throws.
    /// </summary>
    public static LaserPoint[] Apply(LaserPoint[] input, int count, in Config config, Scratch scratch)
    {
        var buffer = ApplyIntoBuffer(input, count, config, scratch, out var outputCount);
        return CopyExact(buffer, outputCount);
    }

    /// <summary>
    /// The same pipeline, but emitted into (and returned as) the scratch's own output buffer -
    /// zero allocation in steady state. The returned array is only valid until the next call on
    /// the same scratch, and its Length is capacity, not content: <paramref name="outputCount"/>
    /// is the frame length. A bad frame degrades (bounded writes), never throws.
    /// </summary>
    public static LaserPoint[] ApplyIntoBuffer(LaserPoint[] input, int count, in Config config, Scratch scratch, out int outputCount)
    {
        outputCount = 0;
        scratch ??= new Scratch();
        if (input == null || count <= 0)
            return scratch.Output;

        // Public entry point: a caller-supplied count must never index past the array it
        // describes. The transforms promise to degrade, not throw.
        count = Math.Min(count, input.Length);
        if (count <= 0)
            return scratch.Output;

        var blankingDelay = Math.Clamp(config.BlankingDelayPoints, 0, 100);
        var pps = config.PointsPerSecond;
        var motionResponsePoints = PointsFromUs(config.MotionResponseUs, pps, MaxMotionResponsePoints);
        var transitionPoints = PointsFromUs(config.TransitionUs, pps, MaxTransitionPoints);
        var settlePoints = PointsFromUs(config.CornerSettleUs, pps, MaxCornerSettlePoints);
        var cornerAnchors = Math.Clamp(config.CornerAnchorCount + settlePoints, 1, MaxCornerAnchors);
        var maxJumpDistance = float.IsFinite(config.MaxJumpDistance) && config.MaxJumpDistance > 0f
                                  ? config.MaxJumpDistance
                                  : Config.Default.MaxJumpDistance;
        var cornerAngleThreshold = float.IsFinite(config.CornerAngleThreshold)
                                       ? config.CornerAngleThreshold
                                       : Config.Default.CornerAngleThreshold;

        var resultSize = EstimateResultSize(count, blankingDelay + transitionPoints + motionResponsePoints, cornerAnchors);
        if (scratch.Output.Length < resultSize)
            scratch.Output = new LaserPoint[resultSize];

        if (config.EnableOptimization)
        {
            OptimizePath(input, count, scratch.Output, maxJumpDistance, blankingDelay,
                         cornerAngleThreshold, cornerAnchors, motionResponsePoints, transitionPoints, scratch, out outputCount);
        }
        else
        {
            ApplyCornerDwellingAndBlanking(input, count, scratch.Output, maxJumpDistance, blankingDelay,
                                           cornerAngleThreshold, cornerAnchors, motionResponsePoints, transitionPoints, out outputCount);
        }

        return scratch.Output;
    }

    /// <summary>µs converted to dwell points at the frame's scan rate: ceil(µs · PPS / 10⁶),
    /// clamped so one knob cannot explode the frame. 0 (or an unknown rate) stays off.</summary>
    private static int PointsFromUs(float us, int pointsPerSecond, int maxPoints)
    {
        if (us <= 0f || pointsPerSecond <= 0)
            return 0;

        var points = (int)MathF.Ceiling(us * pointsPerSecond / 1_000_000f);
        return Math.Clamp(points, 0, maxPoints);
    }

    private const int MaxMotionResponsePoints = 64;
    private const int MaxTransitionPoints = 128;
    private const int MaxCornerSettlePoints = 64;
    private const int MaxCornerAnchors = 40;

    private static LaserPoint[] CopyExact(LaserPoint[] buffer, int outputCount)
    {
        outputCount = Math.Min(outputCount, buffer.Length);
        var result = new LaserPoint[outputCount];
        Array.Copy(buffer, 0, result, 0, outputCount);
        return result;
    }

    /// <summary>
    /// Allocate the true worst case (every input point preceded by a full blank run and a full
    /// corner-dwell run) so jump-heavy frames cannot overrun the buffer; the bounded writes are
    /// the second line of defense.
    /// </summary>
    internal static int EstimateResultSize(int inputCount, int blankingDelay, int cornerAnchors)
    {
        // Long arithmetic: the product overflows int for a frame of ~20M points at a generous
        // blanking/anchor setting, and a negative estimate left the scratch buffer at its old size
        // so the whole frame was dropped by the bounded writes.
        var worstCase = (long)inputCount * (1 + blankingDelay + Math.Max(0, cornerAnchors - 1)) + 1;
        return (int)Math.Min(worstCase, MaxOutputPoints);
    }

    /// <summary>Bounded write into the output buffer. A full buffer drops the point rather than
    /// overrunning the array - a bad frame must degrade, never crash.</summary>
    private static bool TryAddPoint(LaserPoint[] output, ref int outputCount, in LaserPoint point)
    {
        if (outputCount >= output.Length)
            return false;

        output[outputCount++] = point;
        return true;
    }

    private static void OptimizePath(
        LaserPoint[] input, int inputCount,
        LaserPoint[] output,
        float maxJumpDistance, int blankingDelayPoints, float cornerAngleThreshold, int cornerAnchorCount,
        int motionResponsePoints, int transitionPoints,
        Scratch scratch, out int outputCount)
    {
        outputCount = 0;
        if (inputCount == 0)
            return;

        var starts = scratch.SegmentStartIndices;
        var ends = scratch.SegmentEndIndices;
        starts.Clear();
        ends.Clear();

        FindSegments(input, inputCount, starts, ends);

        if (starts.Count == 0)
        {
            if (inputCount > 0)
                TryAddPoint(output, ref outputCount, input[0]);
            return;
        }

        var currentX = 0;
        var currentY = 0;

        // The greedy tour is collected first and emitted after a rotation pass: the DAC replays
        // the finished frame in a continuous loop, so the link from the LAST emitted shape back
        // to the FIRST is scanned just as often as every other link. Starting the tour at the
        // shape nearest null (the natural greedy seed) puts that loop seam wherever it happens
        // to fall - rotating the visit order so the seam is the shortest available link keeps
        // the loop closed and smooth, which is repeated 40 times a second on a typical frame.
        var tourOrder = scratch.TourOrder;
        tourOrder.Clear();

        // Every consumed segment leaves the compact Remaining list, which both query strategies
        // answer from: the grid ring scan for spread-out content, a sequential scan otherwise.
        var segmentCount = starts.Count;
        if (scratch.Remaining.Length < segmentCount)
        {
            scratch.Remaining = new int[segmentCount];
            scratch.RemainingPosition = new int[segmentCount];
        }
        for (var i = 0; i < segmentCount; i++)
        {
            scratch.Remaining[i] = i;
            scratch.RemainingPosition[i] = i;
        }
        scratch.RemainingCount = segmentCount;

        // Dense frames (a mesh wireframe can be thousands of short segments) make the exhaustive
        // scan quadratic - seconds per frame at show densities. Spread-out frames answer the
        // nearest-unvisited question through a uniform grid of the scan field, expanding rings of
        // cells. A frame CONCENTRATED into a few cells (a whole wireframe seen small on screen -
        // the common case for a LaserCamera) would make every grid query sweep one enormous
        // linked list; those go sequential, which is the same answer over compact arrays for a
        // fraction of the cost.
        var useGrid = segmentCount > ExhaustiveSegmentLimit;
        if (useGrid)
        {
            BuildSegmentGrid(input, starts, ends, scratch);
            if (scratch.MaxCellEntries > DenseCellEntryLimit)
                useGrid = false;
        }

        // The sequential tour is quadratic in the segment count, and it runs on the render
        // thread. Past this size a concentrated frame keeps its original order (still blanked
        // and corner-dwelt) instead of stalling the editor for a reordering.
        if (!useGrid && segmentCount > MaxSequentialTourSegments)
        {
            ApplyCornerDwellingAndBlanking(input, inputCount, output, maxJumpDistance, blankingDelayPoints,
                                           cornerAngleThreshold, cornerAnchorCount, motionResponsePoints, transitionPoints, out outputCount);
            return;
        }

        for (var i = 0; i < starts.Count; i++)
        {
            var bestIndex = useGrid
                                ? FindNearestSegmentInGrid(input, starts, ends, currentX, currentY, scratch)
                                : FindNearestInRemaining(input, starts, ends, currentX, currentY, scratch);

            if (bestIndex == -1)
                break;

            // The walk-in side is decided on the chosen segment alone: entering from the end wins
            // only when it is strictly closer, exactly like the exhaustive scan's tie handling.
            var startToHere = CalculateDistanceSquared(currentX, currentY, input[starts[bestIndex]].X, input[starts[bestIndex]].Y);
            var endToHere = CalculateDistanceSquared(currentX, currentY, input[ends[bestIndex]].X, input[ends[bestIndex]].Y);
            var bestReverse = endToHere < startToHere;

            if (useGrid)
                UnlinkSegmentFromGrid(bestIndex, scratch);
            RemoveFromRemaining(bestIndex, scratch);
            tourOrder.Add(bestReverse ? ~bestIndex : bestIndex);

            var segStart = starts[bestIndex];
            var segEnd = ends[bestIndex];
            currentX = input[bestReverse ? segStart : segEnd].X;
            currentY = input[bestReverse ? segStart : segEnd].Y;
        }

        var rotation = FindBestSeamRotation(input, starts, ends, tourOrder);

        var emitX = 0;
        var emitY = 0;
        for (var i = 0; i < tourOrder.Count; i++)
        {
            var entry = tourOrder[(i + rotation) % tourOrder.Count];
            var bestReverse = entry < 0;
            var bestIndex = bestReverse ? ~entry : entry;
            var segStart = starts[bestIndex];
            var segEnd = ends[bestIndex];

            // The dark travel must land where this segment's first LIT sample is: its start when the
            // segment is drawn forward, its end when the tour reversed it. Approaching the other end
            // and then opening the beam across the whole segment is exactly the maximal-acceleration
            // lit jump the blanked ramps exist to prevent.
            var entrySample = bestReverse ? segEnd : segStart;
            InsertBlankingIfNeeded(output, ref outputCount, emitX, emitY, input[entrySample].X, input[entrySample].Y, maxJumpDistance, blankingDelayPoints, transitionPoints);

            // Motion response: the beam pauses at a shape's first sample after the jump - the
            // scanners need the time the µs budget grants before the shape is drawn.
            if (motionResponsePoints > 0)
            {
                var entryPoint = input[entrySample];
                for (var d = 0; d < motionResponsePoints && outputCount < output.Length; d++)
                    output[outputCount++] = entryPoint;
            }

            if (bestReverse)
            {
                for (var k = segEnd; k >= segStart; k--)
                {
                    if (!TryAddPoint(output, ref outputCount, input[k]))
                        break;
                }
            }
            else
            {
                for (var k = segStart; k <= segEnd; k++)
                {
                    if (!TryAddPoint(output, ref outputCount, input[k]))
                        break;
                }
            }

            emitX = input[bestReverse ? segStart : segEnd].X;
            emitY = input[bestReverse ? segStart : segEnd].Y;
        }

        ApplyCornerDwellingToOutput(output, ref outputCount, cornerAngleThreshold, cornerAnchorCount, scratch);
    }

    /// <summary>Up to this many lit segments the tour scans the compact remaining-list directly:
    /// the classic ordering, unchanged. A hand-drawn show never reaches it; a mesh wireframe is
    /// far beyond.</summary>
    private const int ExhaustiveSegmentLimit = 512;

    /// <summary>A frame whose most occupied grid cell holds more than this many segment endpoints
    /// is CONCENTRATED (a whole wireframe seen small): its grid queries would each sweep an
    /// enormous linked list, so the whole tour goes sequential over the remaining-list.</summary>
    private const int DenseCellEntryLimit = 256;

    /// <summary>Largest segment count the quadratic sequential tour is allowed to reorder
    /// (about 17M distance tests, a few milliseconds).</summary>
    private const int MaxSequentialTourSegments = 4096;

    /// <summary>Rings the grid scan expands before giving up and answering from the remaining-list
    /// - for the far-away-nearest case on spread-out frames.</summary>
    private const int GridFallbackRing = 4;

    /// <summary>Tour grid resolution. 128 cells over the ±32768 field = 512 units per cell, so a
    /// step inspects a handful of nearby segments instead of every segment in the frame.</summary>
    private const int GridCellsPerAxis = 128;

    private const int GridCellCount = GridCellsPerAxis * GridCellsPerAxis;

    private const float GridFieldExtent = 32768f;

    private const float GridCellSize = 2f * GridFieldExtent / GridCellsPerAxis;

    /// <summary>Nearest not-yet-toured segment, scanning the compact remaining list. Sequential
    /// memory access makes this several times faster per candidate than the grid's linked lists -
    /// it is the tour's workhorse for small and concentrated frames alike. Same rule everywhere:
    /// nearest endpoint wins, ties keep the lower segment index.</summary>
    private static int FindNearestInRemaining(LaserPoint[] input, List<int> starts, List<int> ends,
                                              int currentX, int currentY, Scratch scratch)
    {
        var bestDist = float.MaxValue;
        var bestIndex = -1;
        var remaining = scratch.Remaining;
        var count = scratch.RemainingCount;

        for (var i = 0; i < count; i++)
        {
            var j = remaining[i];
            var distToStart = CalculateDistanceSquared(currentX, currentY, input[starts[j]].X, input[starts[j]].Y);
            if (distToStart < bestDist || (distToStart == bestDist && j < bestIndex))
            {
                bestDist = distToStart;
                bestIndex = j;
            }

            var distToEnd = CalculateDistanceSquared(currentX, currentY, input[ends[j]].X, input[ends[j]].Y);
            if (distToEnd < bestDist || (distToEnd == bestDist && j < bestIndex))
            {
                bestDist = distToEnd;
                bestIndex = j;
            }
        }

        return bestIndex;
    }

    /// <summary>Swap-removes a consumed segment from the compact remaining list - O(1) via the
    /// id-to-position map. The list and the tour grid consume together, so a query may answer
    /// from either and see the same not-yet-toured set.</summary>
    private static void RemoveFromRemaining(int segment, Scratch scratch)
    {
        var position = scratch.RemainingPosition[segment];
        var last = scratch.Remaining[--scratch.RemainingCount];
        scratch.Remaining[position] = last;
        scratch.RemainingPosition[last] = position;
    }

    private static int GridCellOf(int x, int y, Scratch scratch)
    {
        var cx = Math.Clamp((int)((x - scratch.GridOriginX) / scratch.GridCellSizeCurrent), 0, GridCellsPerAxis - 1);
        var cy = Math.Clamp((int)((y - scratch.GridOriginY) / scratch.GridCellSizeCurrent), 0, GridCellsPerAxis - 1);
        return cy * GridCellsPerAxis + cx;
    }

    /// <summary>Buckets every segment's two endpoints into the tour grid (both endpoints, so the
    /// nearest end is found no matter which side the beam approaches from). The grid maps the
    /// content's bounding box: full-field cells would cram a small, centered frame into a
    /// handful of giant cell lists, which is exactly the shape a LaserCamera wireframe has.
    /// Also records each cell's list length - should a frame STILL pile into a few cells, the
    /// tour switches to the sequential remaining-scan.</summary>
    private static void BuildSegmentGrid(LaserPoint[] input, List<int> starts, List<int> ends, Scratch scratch)
    {
        if (scratch.GridHead.Length < GridCellCount)
        {
            scratch.GridHead = new int[GridCellCount];
            scratch.GridCellLength = new int[GridCellCount];
            Array.Fill(scratch.GridHead, -1);
        }
        else
        {
            // Only the cells the previous frame touched need clearing, and usually far fewer
            // than the whole grid.
            foreach (var cell in scratch.GridUsedCells)
            {
                scratch.GridHead[cell] = -1;
                scratch.GridCellLength[cell] = 0;
            }
        }

        scratch.GridUsedCells.Clear();
        scratch.MaxCellEntries = 0;

        // Content bounds -> cell size. A degenerate extent (all points on one spot) falls back
        // to the full-field mapping rather than dividing by zero.
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        for (var i = 0; i < starts.Count; i++)
        {
            Ref(ref minX, ref maxX, input[starts[i]].X);
            Ref(ref minY, ref maxY, input[starts[i]].Y);
            Ref(ref minX, ref maxX, input[ends[i]].X);
            Ref(ref minY, ref maxY, input[ends[i]].Y);
        }

        var extentX = maxX - minX;
        var extentY = maxY - minY;
        var extent = MathF.Max(MathF.Max(extentX, extentY), 1f);
        if (extent > 2f * GridFieldExtent)
            extent = 2f * GridFieldExtent;

        scratch.GridOriginX = minX;
        scratch.GridOriginY = minY;
        scratch.GridCellSizeCurrent = MathF.Max(extent / GridCellsPerAxis, 0.001f);

        int entryCount = starts.Count * 2;
        if (scratch.GridNext.Length < entryCount)
        {
            scratch.GridNext = new int[entryCount];
            scratch.GridPrev = new int[entryCount];
            scratch.GridEntrySegment = new int[entryCount];
            scratch.GridEntryCell = new int[entryCount];
        }

        var entry = 0;
        for (var i = 0; i < starts.Count; i++)
        {
            var startPoint = input[starts[i]];
            AddGridEntry(GridCellOf(startPoint.X, startPoint.Y, scratch), i, ref entry, scratch);

            var endPoint = input[ends[i]];
            AddGridEntry(GridCellOf(endPoint.X, endPoint.Y, scratch), i, ref entry, scratch);
        }

        void Ref(ref float lo, ref float hi, int value)
        {
            if (value < lo)
                lo = value;
            if (value > hi)
                hi = value;
        }
    }

    private static void AddGridEntry(int cell, int segment, ref int entry, Scratch scratch)
    {
        var head = scratch.GridHead[cell];
        if (head == -1)
            scratch.GridUsedCells.Add(cell);

        var length = ++scratch.GridCellLength[cell];
        if (length > scratch.MaxCellEntries)
            scratch.MaxCellEntries = length;

        scratch.GridNext[entry] = head;
        scratch.GridPrev[entry] = -1;
        scratch.GridEntrySegment[entry] = segment;
        scratch.GridEntryCell[entry] = cell;
        if (head != -1)
            scratch.GridPrev[head] = entry;
        scratch.GridHead[cell] = entry;
        entry++;
    }

    /// <summary>Removes a consumed segment's two entries from the tour grid. The nearest query
    /// only ever skips visited entries, so the tour is unchanged - but every later query stops
    /// paying to traverse them, which is what degraded toward quadratic as the tour emptied a
    /// dense neighborhood (each scan re-walked hundreds of consumed entries per cell).</summary>
    private static void UnlinkSegmentFromGrid(int segment, Scratch scratch)
    {
        // BuildSegmentGrid assigns entries sequentially: segment i owns entries 2i and 2i+1.
        UnlinkGridEntry(segment * 2, scratch);
        UnlinkGridEntry(segment * 2 + 1, scratch);
    }

    private static void UnlinkGridEntry(int entry, Scratch scratch)
    {
        var prev = scratch.GridPrev[entry];
        var next = scratch.GridNext[entry];
        var cell = scratch.GridEntryCell[entry];

        if (prev != -1)
            scratch.GridNext[prev] = next;
        else
            scratch.GridHead[cell] = next;

        if (next != -1)
            scratch.GridPrev[next] = prev;
    }

    /// <summary>Same answer as the exhaustive scan for a spread-out frame: expand rings of cells
    /// around the current position and stop once no cell still to be visited can hold a closer
    /// endpoint. Ties keep the lower segment index, so the tour stays deterministic. When the
    /// rings don't converge quickly (the nearest not-yet-toured segment is far away, or the
    /// content is one concentrated blob), the compact remaining-list answers instead.</summary>
    private static int FindNearestSegmentInGrid(LaserPoint[] input, List<int> starts, List<int> ends,
                                                int currentX, int currentY, Scratch scratch)
    {
        var cx = Math.Clamp((int)((currentX - scratch.GridOriginX) / scratch.GridCellSizeCurrent), 0, GridCellsPerAxis - 1);
        var cy = Math.Clamp((int)((currentY - scratch.GridOriginY) / scratch.GridCellSizeCurrent), 0, GridCellsPerAxis - 1);

        var bestDist = float.MaxValue;
        var bestIndex = -1;

        for (var ring = 0; ring < GridCellsPerAxis; ring++)
        {
            // Cells in this and every later ring start at least (ring - 1) cells away; once the
            // best hit is strictly closer than that, no candidate left can beat it.
            if (bestIndex >= 0 && ring > 0)
            {
                var minDistance = (ring - 1) * scratch.GridCellSizeCurrent;
                if (bestDist < minDistance * minDistance)
                    break;
            }

            // Not converging: the nearest candidate is far away (sparse frame, long tour legs)
            // and the rings would sweep thousands of mostly empty cells. The sequential scan
            // over the remaining list returns the identical answer.
            if (ring > GridFallbackRing)
                return FindNearestInRemaining(input, starts, ends, currentX, currentY, scratch);

            var minY = Math.Max(0, cy - ring);
            var maxY = Math.Min(GridCellsPerAxis - 1, cy + ring);
            var minX = Math.Max(0, cx - ring);
            var maxX = Math.Min(GridCellsPerAxis - 1, cx + ring);

            for (var gy = minY; gy <= maxY; gy++)
            {
                var onRowEdge = gy == cy - ring || gy == cy + ring;
                for (var gx = minX; gx <= maxX; gx++)
                {
                    // Only the ring's border is new; its interior was scanned by earlier rings.
                    if (ring > 0 && !onRowEdge && gx != cx - ring && gx != cx + ring)
                        continue;

                    for (var e = scratch.GridHead[gy * GridCellsPerAxis + gx]; e != -1; e = scratch.GridNext[e])
                    {
                        var j = scratch.GridEntrySegment[e];
                        var toStart = CalculateDistanceSquared(currentX, currentY, input[starts[j]].X, input[starts[j]].Y);
                        var toEnd = CalculateDistanceSquared(currentX, currentY, input[ends[j]].X, input[ends[j]].Y);
                        var distance = MathF.Min(toStart, toEnd);

                        // Strict improvement keeps the lower segment index on a tie, which is what
                        // the sequential scan's comparison does too.
                        if (distance < bestDist || (distance == bestDist && (bestIndex < 0 || j < bestIndex)))
                        {
                            bestDist = distance;
                            bestIndex = j;
                        }
                    }
                }
            }
        }

        // Every cell is within 127 rings of any other, so this only triggers if the grid is empty -
        // belt and braces so a frame can never lose shapes to a bookkeeping slip.
        return bestIndex >= 0
                   ? bestIndex
                   : FindNearestInRemaining(input, starts, ends, currentX, currentY, scratch);
    }

    /// <summary>
    /// Picks the rotation of the greedy tour whose closing link (last shape's exit point back to
    /// the first shape's entry point) is shortest. Rotating a tour only changes which two
    /// neighbouring links become the seam, so this never adds travel inside the tour.
    /// Deterministic: ties resolve to the earliest rotation.
    /// </summary>
    private static int FindBestSeamRotation(LaserPoint[] input, List<int> starts, List<int> ends, List<int> tourOrder)
    {
        if (tourOrder.Count < 2)
            return 0;

        var bestRotation = 0;
        var bestSeamSq = float.MaxValue;

        for (var r = 0; r < tourOrder.Count; r++)
        {
            // Rotating to start at r: the seam link connects tour order r-1's exit to r's entry.
            var lastEntry = tourOrder[(r + tourOrder.Count - 1) % tourOrder.Count];
            var firstEntry = tourOrder[r];

            var lastIdx = lastEntry < 0 ? ~lastEntry : lastEntry;
            var firstIdx = firstEntry < 0 ? ~firstEntry : firstEntry;

            // Exit of a reversed segment is its start index; entry of a reversed segment is its end.
            var lastExitX = input[lastEntry < 0 ? starts[lastIdx] : ends[lastIdx]].X;
            var lastExitY = input[lastEntry < 0 ? starts[lastIdx] : ends[lastIdx]].Y;
            var firstEntryX = input[firstEntry < 0 ? ends[firstIdx] : starts[firstIdx]].X;
            var firstEntryY = input[firstEntry < 0 ? ends[firstIdx] : starts[firstIdx]].Y;

            var seamSq = CalculateDistanceSquared(lastExitX, lastExitY, firstEntryX, firstEntryY);
            if (seamSq < bestSeamSq)
            {
                bestSeamSq = seamSq;
                bestRotation = r;
            }
        }

        return bestRotation;
    }

    private static void FindSegments(LaserPoint[] input, int inputCount, List<int> startIndices, List<int> endIndices)
    {
        if (inputCount == 0)
            return;

        var segmentStart = 0;
        // Non-positive intensity is dark, not just exactly zero: a negative gate is a
        // malformed frame and must never be promoted into a lit segment.
        var isBlanked = input[0].I <= 0;

        for (var i = 1; i < inputCount; i++)
        {
            var currentlyBlanked = input[i].I <= 0;

            if (currentlyBlanked != isBlanked)
            {
                if (!isBlanked)
                {
                    startIndices.Add(segmentStart);
                    endIndices.Add(i - 1);
                }

                isBlanked = currentlyBlanked;
                segmentStart = i;
            }
        }

        if (!isBlanked && segmentStart < inputCount)
        {
            startIndices.Add(segmentStart);
            endIndices.Add(inputCount - 1);
        }
    }

    private static void ApplyCornerDwellingAndBlanking(
        LaserPoint[] input, int inputCount,
        LaserPoint[] output,
        float maxJumpDistance, int blankingDelayPoints, float cornerAngleThreshold, int cornerAnchorCount,
        int motionResponsePoints, int transitionPoints,
        out int outputCount)
    {
        outputCount = 0;
        if (inputCount == 0)
            return;

        var currentX = 0;
        var currentY = 0;

        for (var i = 0; i < inputCount; i++)
        {
            // Dark travel only where a shape starts (a lit point after a blanked one). Inserting it
            // before every point put blanked samples between consecutive lit points and turned
            // every drawn line into a dotted one.
            var isShapeEntry = input[i].I > 0 && (i == 0 || input[i - 1].I <= 0);
            if (isShapeEntry)
                InsertBlankingIfNeeded(output, ref outputCount, currentX, currentY, input[i].X, input[i].Y, maxJumpDistance, blankingDelayPoints, transitionPoints);

            if (!TryAddPoint(output, ref outputCount, input[i]))
                break;

            // Motion response: the beam pauses at a shape's first sample after the jump.
            if (isShapeEntry && motionResponsePoints > 0)
            {
                for (var d = 0; d < motionResponsePoints && outputCount < output.Length; d++)
                    output[outputCount++] = input[i];
            }

            currentX = input[i].X;
            currentY = input[i].Y;

            if (i < inputCount - 1 && input[i].I > 0 && input[i + 1].I > 0)
            {
                if (i < inputCount - 2)
                {
                    var angle = CalculateInteriorAngle(input[i], input[i + 1], input[i + 2]);
                    if (angle < cornerAngleThreshold)
                    {
                        for (var a = 1; a < cornerAnchorCount; a++)
                        {
                            if (outputCount < output.Length)
                                output[outputCount++] = input[i + 1];
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Corner dwell on the finished tour. A sharp turn (angle at a lit point between its
    /// neighbours below the threshold) repeats the corner point so the beam lingers and the
    /// corner renders sharply. Reads only written samples: the original version probed the
    /// not-yet-written slot after the write head - an always-zero point that made every lit
    /// point register as a corner aimed at the origin and injected those zero samples into
    /// the frame (the scope's "beam goes to the middle between shapes").
    /// </summary>
    private static void ApplyCornerDwellingToOutput(
        LaserPoint[] output, ref int outputCount, float cornerAngleThreshold, int cornerAnchorCount, Scratch scratch)
    {
        if (outputCount < 3 || cornerAnchorCount <= 1)
            return;

        var originalCount = outputCount;
        if (scratch.Temp.Length < output.Length)
            scratch.Temp = new LaserPoint[output.Length];
        var tempArray = scratch.Temp;

        var writeIndex = 0;
        tempArray[writeIndex++] = output[0];

        for (var i = 1; i < originalCount; i++)
        {
            if (writeIndex >= tempArray.Length)
                break;

            tempArray[writeIndex++] = output[i];

            // Sharp corner at this point: measured between the two written neighbours and
            // the next input point, all real samples.
            if (i + 1 < originalCount && output[i].I > 0 && output[i + 1].I > 0 && writeIndex >= 2)
            {
                var angle = CalculateInteriorAngle(tempArray[writeIndex - 2], tempArray[writeIndex - 1], output[i + 1]);
                if (angle < cornerAngleThreshold)
                {
                    for (var a = 1; a < cornerAnchorCount && writeIndex < tempArray.Length; a++)
                    {
                        tempArray[writeIndex] = tempArray[writeIndex - 1];
                        writeIndex++;
                    }
                }
            }
        }

        Array.Copy(tempArray, output, writeIndex);
        outputCount = writeIndex;
    }

    /// <summary>
    /// Blanked approach to a shape start. The beam must always be off while moving between two
    /// shapes - otherwise nearby shapes get connected by a lit line, and dot-like single points
    /// would burn strokes to their neighbours. The blanked samples are distributed along the
    /// travel line so the scanners track a ramp instead of a single-sample step (a step is a
    /// maximal-acceleration demand that rings the galvos - the opposite of longevity), with the
    /// last samples held at the target so the scanners settle before the beam turns on.
    /// Sub-pixel moves are skipped: the beam is already at the target.
    /// </summary>
    private static void InsertBlankingIfNeeded(
        LaserPoint[] output, ref int outputCount,
        int fromX, int fromY, int toX, int toY,
        float maxJumpDistance, int blankingDelayPoints, int transitionPoints)
    {
        if (outputCount == 0)
            return;

        if (!float.IsFinite(maxJumpDistance) || maxJumpDistance <= 0 || blankingDelayPoints <= 0)
            return;

        // Cast to float before squaring: int dx*dx overflows for jumps beyond 46341 units and the
        // negative result silently skipped blanking on exactly the full-field jumps that need it
        // most (same bug as in LaserSafetyTransforms).
        var dx = (float)(toX - fromX);
        var dy = (float)(toY - fromY);
        var distance = MathF.Sqrt(dx * dx + dy * dy);

        if (distance < 0.5f)
            return;

        var blankCount = (int)MathF.Ceiling(distance / maxJumpDistance * blankingDelayPoints);
        blankCount = Math.Clamp(blankCount, 1, blankingDelayPoints);

        // Most samples ramp along the path, the rest settle at the target. One sample alone
        // cannot both ramp and settle - keep it as a pure settle like the classic blanking shift.
        var settleCount = Math.Min(2, blankCount - 1);
        var rampCount = blankCount - settleCount;

        for (var b = 1; b <= rampCount && outputCount < output.Length; b++)
        {
            var t = b / (float)(rampCount + 1);
            output[outputCount++] = LaserPoint.CreateBlanked(
                (int)MathF.Round(fromX + dx * t),
                (int)MathF.Round(fromY + dy * t));
        }

        for (var s = 0; s < settleCount && outputCount < output.Length; s++)
            output[outputCount++] = LaserPoint.CreateBlanked(toX, toY);

        // The transition hold: extra blanked samples parked on the shape's entry point - the
        // time the scanners get for the jump before the beam opens, independent of the
        // distance-scaled ramp above.
        for (var t = 0; t < transitionPoints && outputCount < output.Length; t++)
            output[outputCount++] = LaserPoint.CreateBlanked(toX, toY);
    }

    /// <summary>
    /// The interior angle at <paramref name="p2"/> in degrees: 180 is straight, small is a sharp
    /// spike - the convention the corner threshold is documented in ("angles below this count as
    /// sharp corners"). Zero-length legs (dwell runs) read as straight.
    /// </summary>
    private static float CalculateInteriorAngle(LaserPoint p1, LaserPoint p2, LaserPoint p3)
    {
        // Float before multiplying: full-field steps squared overflow int and turned the angle into NaN.
        var dx1 = (float)(p2.X - p1.X);
        var dy1 = (float)(p2.Y - p1.Y);
        var dx2 = (float)(p3.X - p2.X);
        var dy2 = (float)(p3.Y - p2.Y);

        var len1 = MathF.Sqrt(dx1 * dx1 + dy1 * dy1);
        var len2 = MathF.Sqrt(dx2 * dx2 + dy2 * dy2);

        if (len1 < 0.001f || len2 < 0.001f)
            return 180f;

        var dot = dx1 * dx2 + dy1 * dy2;
        var cosAngle = dot / (len1 * len2);
        cosAngle = Math.Clamp(cosAngle, -1f, 1f);

        // The angle between the two travel directions is the TURN (0 = straight on); the
        // interior angle is its supplement. Comparing the turn against the threshold dwelt every
        // straight sample (tripling line point counts) and skipped the real corners.
        var turnDegrees = MathF.Acos(cosAngle) * (180f / MathF.PI);
        return 180f - turnDegrees;
    }

    private static float CalculateDistanceSquared(int x1, int y1, int x2, int y2)
    {
        // Cast to float before squaring to avoid int32 overflow on long jumps
        // (e.g. 60000*60000 > int.MaxValue), same fix as in LaserSafetyTransforms.
        var dx = (float)(x2 - x1);
        var dy = (float)(y2 - y1);
        return dx * dx + dy * dy;
    }
}
