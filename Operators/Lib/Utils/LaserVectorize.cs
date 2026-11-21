#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// CPU vectorization pipeline shared by the laser conversion operators: from a CPU copy of a
/// texture (per-pixel brightness + packed color at a working resolution) to ILDA laser points.
///
/// Two detectors:
/// - <b>Centerline tracing</b> of thin bright strokes: threshold → Zhang-Suen skeleton → path-graph
///   trace. One lit stroke per line — the encoding thin line art wants. Isolated bright pixels
///   survive as dwell dots.
/// - <b>Contour extraction</b>: marching-squares iso-contours of the brightness field at the
///   threshold (region outlines with holes; a thin line becomes the two sides of its ribbon — the
///   double stroke the Thickness reduction in vectorizer guides describes), and Canny edges
///   (blur → Sobel → non-max suppression → hysteresis) traced as thin paths.
///
/// Shapes are emitted as blanked approach point + evenly spaced lit samples — the encoding the
/// output pipeline's segment detection splits into shapes — so the output setup's optimizer
/// (reorder, blanking, corner dwell) and safety stage run on the result unchanged.
/// All buffers are reused across runs; a run allocates nothing per frame.
/// </summary>
public sealed class LaserVectorizeSession
{
    /// <summary>Working resolution the last copy landed on.</summary>
    public int WorkWidth => _workW;

    /// <summary>Working resolution the last copy landed on.</summary>
    public int WorkHeight => _workH;

    /// <summary>Statistics of the last run — the operator's status line and diagnostics.</summary>
    public RunStats Stats;

    /// <summary>Per-run statistics</summary>
    public struct RunStats
    {
        /// <summary>Working resolution after the max-pool downscale to MaxRes.</summary>
        public int WorkWidth, WorkHeight;

        /// <summary>Shapes kept; shapes dropped by Denoise or the path cap are counted separately.</summary>
        public int Shapes, ShapesDropped, ShapesClipped;

        /// <summary>Points emitted (blanked separators included).</summary>
        public int Points;

        /// <summary>Hit the point budget: trailing shapes were not emitted.</summary>
        public bool OverBudget;
    }

    /// <summary>Detector mode.</summary>
    public enum DetectMode
    {
        /// <summary>Threshold → centerlines of thin strokes (Thickness 1) or region outlines (Thickness &gt; 1).</summary>
        FindPaths = 0,

        /// <summary>Canny edges (blur → Sobel → non-max suppression → hysteresis), traced as thin paths.</summary>
        FindContours = 1,
    }

    /// <summary>One vectorize pass's parameters.</summary>
    public struct Params
    {
        public DetectMode Mode;
        public float Threshold;
        public int Thickness;      // FindPaths only: 1 = skeletonize, >1 = region outlines
        public int BlurSize;       // FindContours only: 0=off, 1=3px, 2=5px, 3=7px, 4=9px Gaussian
        public int Denoise;        // drop shapes shorter than this many working pixels
        public int MaxPaths;       // shape cap
        public float PointSpacing; // working px between lit samples
        public bool UseColor;
        public float Brightness;   // 0..1 multiplier on the emitted color
        public int PointCap;       // hard cap for the emitted frame (transport budget)

        /// <summary>Extra lit samples at each shape's end position, so the scanner settles on the
        /// real endpoint instead of rounding it off (the scanner never knows where a frame ends).</summary>
        public int EndRepeat;

        /// <summary>0..1: ramps the shape's first samples up from this dim level to full - the
        /// scanner is still catching up when a path starts, which otherwise blooms the start point.</summary>
        public float InFade;

        /// <summary>0..1: ramps the end-repeat samples down, so finishing a shape doesn't leave
        /// another bright dot where the beam pauses.</summary>
        public float OutFade;

        /// <summary>The working image's larger dimension - the base the path-length filter's
        /// percentages are taken of (the MadLaser guide's Path Filtering convention).</summary>
        public float WorkingMaxDimension;

        /// <summary>Shapes shorter than this percentage of the working image's larger dimension
        /// are dropped (noise specks that would cost a blank jump each). 0 = off.</summary>
        public float MinPathLengthPct;

        /// <summary>Shapes longer than this percentage are dropped (runaway background strokes).
        /// 0 = off.</summary>
        public float MaxPathLengthPct;
    }

    /// <summary>The emitted frame — valid up to <see cref="Count"/> after <see cref="Run"/>.</summary>
    public LaserPoint[] Buffer => _out;

    /// <summary>Points in the emitted frame after <see cref="Run"/>.</summary>
    public int Count => _outCount;

    // --------------------------------------------------------------------------------------------
    // Copy stage
    // --------------------------------------------------------------------------------------------

    public const int FmtBgra8 = 0;
    public const int FmtRgba8 = 1;
    public const int FmtRgba16F = 2;
    public const int FmtR8 = 3;
    public const int FmtR32F = 4;

    // --------------------------------------------------------------------------------------------
    // Copy stage: staging bytes → working buffers (max-pool downscale)
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Sizes the working buffers for a source frame. Returns false when the size is unusable.
    /// </summary>
    public bool BeginCopy(int srcW, int srcH, int maxRes)
    {
        if (srcW <= 0 || srcH <= 0 || srcW > 8192 || srcH > 8192)
            return false;

        maxRes = Math.Clamp(maxRes, 64, 4096);
        var scale = Math.Min(1f, maxRes / (float)Math.Max(srcW, srcH));
        _workW = Math.Max(1, (int)MathF.Round(srcW * scale));
        _workH = Math.Max(1, (int)MathF.Round(srcH * scale));

        var n = _workW * _workH;
        if (_gray == null || _gray.Length < n)
        {
            _gray = new float[n];
            _color = new uint[n];
            _binary = new byte[n];
            _blurred = new float[n];
            _mag = new float[n];
            _dirq = new byte[n];
            _visited = new byte[n];
        }

        return true;
    }

    /// <summary>
    /// Converts one staging frame into the working buffers: max-pool downscale — each working pixel
    /// takes the brightest source pixel of its block, so a thin bright line survives the downscale.
    /// Brightness is the max of the three color channels: a saturated pure-blue line stays above
    /// threshold where a luma sum would dim it.
    /// </summary>
    public void CopyFrame(byte[] src, int rowPitch, int srcW, int srcH, int formatId)
    {
        var w = _workW;
        var h = _workH;
        var bw = Math.Max(1, (srcW + w - 1) / w); // ceil: the last block must reach the source edge
        var bh = Math.Max(1, (srcH + h - 1) / h);

        var gray = _gray;
        var color = _color;
        var pitch = rowPitch;

        for (var wy = 0; wy < h; wy++)
        {
            var y0 = wy * bh;
            var y1 = Math.Min(srcH, y0 + bh);
            for (var wx = 0; wx < w; wx++)
            {
                var x0 = wx * bw;
                var x1 = Math.Min(srcW, x0 + bw);
                var best = -1f;
                var bestColor = 0xFFFFFFu;

                for (var y = y0; y < y1; y++)
                {
                    var rowBase = y * pitch;
                    for (var x = x0; x < x1; x++)
                    {
                        float lum;
                        uint packed;
                        var o = rowBase + x * BytesPerPixel(formatId);
                        switch (formatId)
                        {
                            case FmtBgra8:
                            {
                                var b = src[o];
                                var g = src[o + 1];
                                var r = src[o + 2];
                                lum = MathF.Max(r, MathF.Max(g, b));
                                packed = (uint)(r << 16) | (uint)(g << 8) | b;
                                break;
                            }
                            case FmtRgba8:
                            {
                                var r = src[o];
                                var g = src[o + 1];
                                var b = src[o + 2];
                                lum = MathF.Max(r, MathF.Max(g, b));
                                packed = (uint)(r << 16) | (uint)(g << 8) | b;
                                break;
                            }
                            case FmtRgba16F:
                            {
                                var r = HalfBitsToFloat(BitConverter.ToUInt16(src, o));
                                var g = HalfBitsToFloat(BitConverter.ToUInt16(src, o + 2));
                                var b = HalfBitsToFloat(BitConverter.ToUInt16(src, o + 4));
                                r = Math.Clamp(r, 0f, 1f);
                                g = Math.Clamp(g, 0f, 1f);
                                b = Math.Clamp(b, 0f, 1f);
                                lum = MathF.Max(r, MathF.Max(g, b)) * 255f;
                                packed = (uint)((int)(r * 255f) << 16) | (uint)((int)(g * 255f) << 8) | (uint)(int)(b * 255f);
                                break;
                            }
                            case FmtR8:
                            {
                                var v = src[o];
                                lum = v;
                                packed = (uint)(v << 16) | (uint)(v << 8) | v;
                                break;
                            }
                            case FmtR32F:
                            {
                                var v = Math.Clamp(BitConverter.Int32BitsToSingle(BitConverter.ToInt32(src, o)), 0f, 1f);
                                lum = v * 255f;
                                var q = (int)(v * 255f);
                                packed = (uint)(q << 16) | (uint)(q << 8) | (uint)q;
                                break;
                            }
                            default:
                                lum = 0;
                                packed = 0;
                            break;
                        }

                        if (lum > best)
                        {
                            best = lum;
                            bestColor = packed;
                        }
                    }
                }

                var wi = wy * w + wx;
                gray[wi] = best / 255f;
                color[wi] = bestColor;
            }
        }
    }

    /// <summary>IEEE-754 binary16 bits → single precision float.</summary>
    private static float HalfBitsToFloat(ushort bits)
    {
        var sign = (bits & 0x8000) << 16;
        var exp = (bits >> 10) & 0x1F;
        var man = bits & 0x3FF;

        int single;
        if (exp == 0)
        {
            single = sign | (man << 13); // subnormal: renormalize via the same exponent field
        }
        else if (exp == 0x1F)
        {
            single = sign | 0x7F800000 | (man << 13); // inf / NaN
        }
        else
        {
            var singleExp = exp - 15 + 127;
            single = sign | (singleExp << 23) | (man << 13);
        }

        return BitConverter.Int32BitsToSingle(single);
    }

    private static int BytesPerPixel(int formatId)
    {
        return formatId switch
               {
                   FmtRgba16F => 8,
                   FmtR8 => 1,
                   FmtR32F => 4,
                   _ => 4,
               };
    }

    // --------------------------------------------------------------------------------------------
    // Run: detect + emit
    // --------------------------------------------------------------------------------------------

    /// <summary>Upper bound on thinning iterations — fat blobs beyond this keep partial thickness
    /// (use region outlines for those); thin strokes skeletonize long before it.</summary>
    private const int MaxThinIterations = 48;

    /// <summary>Fallback point cap when the caller passes none.</summary>
    private const int DefaultPointCap = 1 << 17;

    /// <summary>Upper bound on contour segments per run.</summary>
    private const int MaxSegments = 1 << 20;

    public void Run(in Params p)
    {
        Stats = default;
        _outCount = 0;
        _shapes = 0;
        _overBudget = false;
        if (_workW <= 0 || _workH <= 0)
            return;

        var cap = p.PointCap > 0 ? p.PointCap : DefaultPointCap;
        cap = Math.Min(cap, DefaultPointCap);
        EnsureOut(cap);
        _params = p;
        // The path-length filter's percentages base on the working image's larger dimension.
        _params.WorkingMaxDimension = Math.Max(_workW, _workH);

        switch (p.Mode)
        {
            case DetectMode.FindPaths:
            {
                ToBinary(p.Threshold);
                if (p.Thickness <= 1)
                {
                    Thin();
                    TraceThinPaths(_binary, EmitPathAction);
                }
                else
                {
                    TraceContoursMS(p.Threshold);
                }

                break;
            }
            case DetectMode.FindContours:
            {
                Canny(p.Threshold, p.BlurSize);
                TraceThinPaths(_binary, EmitPathAction);
                break;
            }
        }

        Stats.WorkWidth = _workW;
        Stats.WorkHeight = _workH;
        Stats.Shapes = _shapes;
        Stats.Points = _outCount;
        Stats.OverBudget = _outCount >= _out.Length && _out.Length > 0;
    }

    private readonly Action<List<float>, bool> EmitPathAction;

    public LaserVectorizeSession()
    {
        EmitPathAction = EmitPath;
    }

    private Params _params;

    // --------------------------------------------------------------------------------------------
    // Binary map
    // --------------------------------------------------------------------------------------------

    private void ToBinary(float threshold)
    {
        var gray = _gray;
        var bin = _binary;
        var n = _workW * _workH;
        for (var i = 0; i < n; i++)
            bin[i] = gray[i] >= threshold ? (byte)1 : (byte)0;
    }

    // --------------------------------------------------------------------------------------------
    // Zhang-Suen thinning to a 1px skeleton. Border pixels are never deleted so content reaching
    // the image edge keeps its thickness there; interior strokes skeletonize fully.
    // --------------------------------------------------------------------------------------------

    private void Thin()
    {
        var m = _binary;
        var w = _workW;
        var h = _workH;
        var deletedAny = false;
        var iterations = 0;

        do
        {
            deletedAny = false;
            for (var pass = 0; pass < 2; pass++)
            {
                _toDelete.Clear();
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var i = y * w + x;
                        if (m[i] == 0)
                            continue;

                        var p2 = On(m, w, h, x, y - 1);
                        var p3 = On(m, w, h, x + 1, y - 1);
                        var p4 = On(m, w, h, x + 1, y);
                        var p5 = On(m, w, h, x + 1, y + 1);
                        var p6 = On(m, w, h, x, y + 1);
                        var p7 = On(m, w, h, x - 1, y + 1);
                        var p8 = On(m, w, h, x - 1, y);
                        var p9 = On(m, w, h, x - 1, y - 1);

                        var b = (p2 ? 1 : 0) + (p3 ? 1 : 0) + (p4 ? 1 : 0) + (p5 ? 1 : 0)
                                + (p6 ? 1 : 0) + (p7 ? 1 : 0) + (p8 ? 1 : 0) + (p9 ? 1 : 0);
                        if (b < 2 || b > 6)
                            continue;

                        var a = (p2 != p3 ? 1 : 0) + (p3 != p4 ? 1 : 0) + (p4 != p5 ? 1 : 0) + (p5 != p6 ? 1 : 0)
                                + (p6 != p7 ? 1 : 0) + (p7 != p8 ? 1 : 0) + (p8 != p9 ? 1 : 0) + (p9 != p2 ? 1 : 0);
                        if (a != 1)
                            continue;

                        bool condC, condD;
                        if (pass == 0)
                        {
                            condC = p2 && p4 && p6;
                            condD = p4 && p6 && p8;
                        }
                        else
                        {
                            condC = p2 && p4 && p8;
                            condD = p2 && p6 && p8;
                        }

                        if (condC || condD)
                            continue;

                        _toDelete.Add(i);
                    }
                }

                if (_toDelete.Count > 0)
                {
                    for (var k = 0; k < _toDelete.Count; k++)
                        m[_toDelete[k]] = 0;

                    deletedAny = true;
                }
            }
        }
        while (deletedAny && ++iterations < MaxThinIterations);
    }

    private static bool On(byte[] m, int w, int h, int x, int y)
        => x >= 0 && y >= 0 && x < w && y < h && m[y * w + x] != 0;

    // --------------------------------------------------------------------------------------------
    // Thin-path tracing: endpoints first, then loops/leftovers, preferring straight continuation.
    // --------------------------------------------------------------------------------------------

    /// <summary>Traces a thin (1px) map into polylines of pixel-center coordinates, feeding each
    /// completed path to <paramref name="pathCallback"/> immediately. Vertex coords are pixel
    /// indices (a pixel's center coordinate IS its index).</summary>
    public void TraceThinPaths(byte[] map, Action<List<float>, bool> pathCallback)
    {
        var w = _workW;
        var h = _workH;
        var visited = _visited;
        Array.Clear(visited, 0, w * h);

        // Endpoints (exactly one 8-neighbor) trace outward first...
        for (var i = 0; i < w * h; i++)
        {
            if (map[i] == 0 || visited[i] != 0)
                continue;

            if (NeighborCount(map, w, h, i % w, i / w) != 1)
                continue;

            TraceFrom(map, i, pathCallback);
        }

        // ...then whatever remains: closed loops and junction leftovers.
        for (var i = 0; i < w * h; i++)
        {
            if (map[i] == 0 || visited[i] != 0)
                continue;

            TraceFrom(map, i, pathCallback);
        }
    }

    private void TraceFrom(byte[] map, int start, Action<List<float>, bool> pathCallback)
    {
        var w = _workW;
        var h = _workH;
        var visited = _visited;
        var verts = _pathFloats;
        verts.Clear();
        verts.Add(start % w);
        verts.Add(start / w);

        var cur = start;
        visited[cur] = 1;
        var lastX = -1;
        var lastY = -1;

        while (true)
        {
            var cx = cur % w;
            var cy = cur / w;
            var bestNext = -1;
            var bestDot = -2f;
            var anyNext = -1;

            for (var d = 0; d < 8; d++)
            {
                var nx = cx + NeighborDx[d];
                var ny = cy + NeighborDy[d];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    continue;

                var ni = ny * w + nx;
                if (map[ni] == 0 || visited[ni] != 0)
                    continue;

                if (anyNext < 0)
                    anyNext = ni;

                var dot = 2f; // no direction yet: any neighbor is "straight"
                if (lastX >= 0)
                {
                    var dirX = cx - lastX;
                    var dirY = cy - lastY;
                    var cX = nx - cx;
                    var cY = ny - cy;
                    var lenProduct = MathF.Sqrt((dirX * dirX + dirY * dirY) * (cX * cX + cY * cY));
                    dot = lenProduct > 0f ? (dirX * cX + dirY * cY) / lenProduct : 0f;
                }

                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestNext = ni;
                }
            }

            if (bestNext < 0)
            {
                bestNext = anyNext;
            }

            if (bestNext < 0)
                break;

            lastX = cx;
            lastY = cy;
            visited[bestNext] = 1;
            cur = bestNext;
            verts.Add(cur % w);
            verts.Add(cur / w);
        }

        var closed = verts.Count >= 8 && IsAdjacent(w, cur, start);
        pathCallback(verts, closed);
    }

    private static readonly sbyte[] NeighborDx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly sbyte[] NeighborDy = [-1, -1, 0, 1, 1, 1, 0, -1];

    private static int NeighborCount(byte[] m, int w, int h, int x, int y)
    {
        var count = 0;
        for (var d = 0; d < 8; d++)
        {
            if (On(m, w, h, x + NeighborDx[d], y + NeighborDy[d]))
                count++;
        }

        return count;
    }

    private static bool IsAdjacent(int w, int a, int b)
    {
        var dx = Math.Abs(a % w - b % w);
        var dy = Math.Abs(a / w - b / w);
        return dx <= 1 && dy <= 1;
    }

    // --------------------------------------------------------------------------------------------
    // Marching squares: iso-contours of the gray field at the threshold. Segment endpoints are
    // keyed by the shared pixel edge they cross, so chains never need a saddle special case:
    // adjacent cells share the identical crossing point on their common edge.
    // --------------------------------------------------------------------------------------------

    private void TraceContoursMS(float iso)
    {
        var w = _workW;
        var h = _workH;
        var gray = _gray;
        _segCount = 0;

        for (var y = 0; y < h - 1; y++)
        {
            for (var x = 0; x < w - 1; x++)
            {
                var i = y * w + x;
                var gTL = gray[i];
                var gTR = gray[i + 1];
                var gBL = gray[i + w];
                var gBR = gray[i + w + 1];

                var caseId = (gTL >= iso ? 1 : 0) | (gTR >= iso ? 2 : 0) | (gBR >= iso ? 4 : 0) | (gBL >= iso ? 8 : 0);
                if (caseId == 0 || caseId == 15)
                    continue;

                float topX = 0, topY = 0, rightX = 0, rightY = 0, bottomX = 0, bottomY = 0, leftX = 0, leftY = 0;
                var hasTop = false;
                var hasRight = false;
                var hasBottom = false;
                var hasLeft = false;

                if ((gTL >= iso) != (gTR >= iso))
                {
                    topX = x + (iso - gTL) / (gTR - gTL);
                    topY = y;
                    hasTop = true;
                }

                if ((gTR >= iso) != (gBR >= iso))
                {
                    rightX = x + 1;
                    rightY = y + (iso - gTR) / (gBR - gTR);
                    hasRight = true;
                }

                if ((gBL >= iso) != (gBR >= iso))
                {
                    bottomX = x + (iso - gBL) / (gBR - gBL);
                    bottomY = y + 1;
                    hasBottom = true;
                }

                if ((gTL >= iso) != (gBL >= iso))
                {
                    leftX = x;
                    leftY = y + (iso - gTL) / (gBL - gTL);
                    hasLeft = true;
                }

                switch (caseId)
                {
                    case 1: Pair(leftX, leftY, KeyV(x, y), topX, topY, KeyH(x, y)); break;
                    case 2: Pair(topX, topY, KeyH(x, y), rightX, rightY, KeyV(x + 1, y)); break;
                    case 3: Pair(leftX, leftY, KeyV(x, y), rightX, rightY, KeyV(x + 1, y)); break;
                    case 4: Pair(rightX, rightY, KeyV(x + 1, y), bottomX, bottomY, KeyH(x, y + 1)); break;
                    case 5: Pair(leftX, leftY, KeyV(x, y), topX, topY, KeyH(x, y));
                            Pair(rightX, rightY, KeyV(x + 1, y), bottomX, bottomY, KeyH(x, y + 1)); break;
                    case 6: Pair(topX, topY, KeyH(x, y), bottomX, bottomY, KeyH(x, y + 1)); break;
                    case 7: Pair(leftX, leftY, KeyV(x, y), bottomX, bottomY, KeyH(x, y + 1)); break;
                    case 8: Pair(bottomX, bottomY, KeyH(x, y + 1), leftX, leftY, KeyV(x, y)); break;
                    case 9: Pair(topX, topY, KeyH(x, y), bottomX, bottomY, KeyH(x, y + 1)); break;
                    case 10: Pair(topX, topY, KeyH(x, y), rightX, rightY, KeyV(x + 1, y));
                             Pair(bottomX, bottomY, KeyH(x, y + 1), leftX, leftY, KeyV(x, y)); break;
                    case 11: Pair(rightX, rightY, KeyV(x + 1, y), bottomX, bottomY, KeyH(x, y + 1)); break;
                    case 12: Pair(leftX, leftY, KeyV(x, y), rightX, rightY, KeyV(x + 1, y)); break;
                    case 13: Pair(topX, topY, KeyH(x, y), rightX, rightY, KeyV(x + 1, y)); break;
                    case 14: Pair(bottomX, bottomY, KeyH(x, y + 1), leftX, leftY, KeyV(x, y)); break;
                }
            }
        }

        ChainSegments();
    }


    private int KeyH(int x, int y) => y * (_workW - 1) + x;
    private int KeyV(int x, int y) => _workW * (_workH - 1) + y * _workW + x;

    private void Pair(float ax, float ay, int keyA, float bx, float by, int keyB)
    {
        if (_segCount >= MaxSegments)
            return;

        if (_segK1 == null || _segK1.Length < _segCount + 1)
            GrowSegments();

        var s = _segCount;
        _segK1[s] = keyA;
        _segK2[s] = keyB;
        _segAx[s] = ax;
        _segAy[s] = ay;
        _segBx[s] = bx;
        _segBy[s] = by;
        _segCount++;
    }

    private void GrowSegments()
    {
        var len = _segK1?.Length ?? 0;
        var newSize = len == 0 ? 4096 : Math.Min(MaxSegments, len * 2);
        Array.Resize(ref _segK1, newSize);
        Array.Resize(ref _segK2, newSize);
        Array.Resize(ref _segAx, newSize);
        Array.Resize(ref _segAy, newSize);
        Array.Resize(ref _segBx, newSize);
        Array.Resize(ref _segBy, newSize);
    }

    // --------------------------------------------------------------------------------------------
    // Segment chaining: sort both endpoints of every segment by their edge key; runs of two equal
    // keys are a link between exactly two segments. Then walk the chains.
    // --------------------------------------------------------------------------------------------

    private void ChainSegments()
    {
        var n = _segCount;
        if (n == 0)
            return;

        if (_epKey == null || _epKey.Length < n * 2)
        {
            _epKey = new int[n * 2];
            _epSeg = new int[n * 2];
            _link1 = new int[n];
            _link2 = new int[n];
            _segUsed = new byte[n];
        }

        var k = 0;
        for (var s = 0; s < n; s++)
        {
            _epKey[k] = _segK1[s];
            _epSeg[k] = s;
            k++;
            _epKey[k] = _segK2[s];
            _epSeg[k] = s;
            k++;
        }

        Array.Sort(_epKey, _epSeg);

        Array.Fill(_link1, -1, 0, n);
        Array.Fill(_link2, -1, 0, n);

        var i = 0;
        while (i < n * 2)
        {
            var key = _epKey[i];
            var j = i;
            while (j < n * 2 && _epKey[j] == key)
                j++;

            var run = j - i;
            if (run == 2)
            {
                var a = _epSeg[i];
                var b = _epSeg[i + 1];
                if (a != b)
                {
                    if (_segK1[a] == key)
                        _link1[a] = b;
                    else
                        _link2[a] = b;

                    if (_segK1[b] == key)
                        _link1[b] = a;
                    else
                        _link2[b] = a;
                }
            }

            i = j;
        }

        Array.Clear(_segUsed, 0, n);

        for (var s = 0; s < n; s++)
        {
            if (_segUsed[s] != 0)
                continue;

            BuildChain(s, n);
        }
    }


    /// <summary>Walks one direction of the chain containing <paramref name="start"/>: appends the
    /// segment's position at <paramref name="fromKey"/>, then its exit position, then follows the
    /// links, appending each continuation's exit position. Returns true when the walk closed back
    /// onto the start segment.</summary>
    private bool WalkChain(int start, int fromKey, List<float> into)
    {
        var otherKey = fromKey == _segK1[start] ? _segK2[start] : _segK1[start];
        AppendPos(into, start, fromKey);
        AppendPos(into, start, otherKey);

        var cur = start;
        var curKey = otherKey;
        var closed = false;

        while (true)
        {
            var next = curKey == _segK1[cur] ? _link1[cur] : _link2[cur];
            if (next == start)
            {
                closed = true;
                break;
            }

            if (next < 0 || _segUsed[next] != 0)
                break;

            _segUsed[next] = 1;
            var entryIsK1 = curKey == _segK1[next];
            var exitKey = entryIsK1 ? _segK2[next] : _segK1[next];
            AppendPos(into, next, exitKey);
            curKey = exitKey;
            cur = next;
        }

        return closed;
    }

    private void AppendPos(List<float> into, int seg, int key)
    {
        if (key == _segK1[seg])
        {
            into.Add(_segAx[seg]);
            into.Add(_segAy[seg]);
        }
        else
        {
            into.Add(_segBx[seg]);
            into.Add(_segBy[seg]);
        }
    }

    /// <summary>Builds the polyline of the chain containing <paramref name="start"/> and emits it:
    /// forward walk, and - unless closed - a backward walk prepended in reverse.</summary>
    private void BuildChain(int start, int n)
    {
        _pathFloats.Clear();
        _pathBackward.Clear();
        _segUsed[start] = 1;

        var closed = WalkChain(start, _segK1[start], _pathFloats);
        if (!closed)
        {
            var closedBack = WalkChain(start, _segK2[start], _pathBackward);
            closed = closedBack;

            // backward = [B, A, p1, p2, ...]: prepend the tail reversed (skipping the start's own
            // two positions, already present in the forward list) ahead of it.
            if (_pathBackward.Count > 4)
            {
                _pathAssembled.Clear();
                for (var i = _pathBackward.Count - 2; i >= 2; i -= 2)
                {
                    _pathAssembled.Add(_pathBackward[i]);
                    _pathAssembled.Add(_pathBackward[i + 1]);
                }

                for (var i = 0; i < _pathFloats.Count; i++)
                    _pathAssembled.Add(_pathFloats[i]);

                (_pathFloats, _pathAssembled) = (_pathAssembled, _pathFloats);
            }
        }

        EmitPath(_pathFloats, closed);
    }

    // --------------------------------------------------------------------------------------------
    // Emit: polyline → blanked approach point + evenly spaced lit samples
    // --------------------------------------------------------------------------------------------

    /// <summary>Converts one polyline (working-pixel coordinates) into laser points: dropped when
    /// shorter than Denoise, capped by MaxPaths, resampled by PointSpacing, blanked approach point
    /// then lit samples with the nearest pixel's color, brightness applied.</summary>
    private void EmitPath(List<float> pairs, bool closed)
    {
        var vertexCount = pairs.Count / 2;
        if (vertexCount == 1)
        {
            EmitDot(pairs[0], pairs[1]);
            return;
        }

        if (vertexCount < 2)
            return;

        var p = _params;

        // Pixel length (wrapping when closed).
        var length = 0f;
        var lastSegment = closed ? vertexCount : vertexCount - 1;
        for (var i = 0; i < lastSegment; i++)
        {
            var vAx = pairs[2 * i];
            var vAy = pairs[2 * i + 1];
            var vB = 2 * ((i + 1) % vertexCount);
            var vBx = pairs[vB];
            var vBy = pairs[vB + 1];
            var dx = vBx - vAx;
            var dy = vBy - vAy;
            length += MathF.Sqrt(dx * dx + dy * dy);
        }

        if (length < p.Denoise)
        {
            Stats.ShapesDropped++;
            return;
        }

        // Path filtering: percent of the working image's larger dimension, the guide's convention.
        if (p.WorkingMaxDimension > 0f)
        {
            var lengthPct = length / p.WorkingMaxDimension * 100f;
            if (p.MinPathLengthPct > 0f && lengthPct < p.MinPathLengthPct
                || p.MaxPathLengthPct > 0f && lengthPct > p.MaxPathLengthPct)
            {
                Stats.ShapesDropped++;
                return;
            }
        }

        if (_shapes >= p.MaxPaths)
        {
            Stats.ShapesClipped++;
            return;
        }

        _shapes++;

        var spacing = MathF.Max(0.5f, p.PointSpacing);
        var steps = Math.Max(1, (int)MathF.Round(length / spacing));
        // In-fade ramps over the shape's first quarter, at least one sample; end repeats follow the last sample.
        var fadeSamples = Math.Clamp((int)MathF.Ceiling(steps * 0.25f), 1, steps);
        var endRepeat = Math.Clamp(p.EndRepeat, 0, 16);
        EnsureOut(_outCount + steps + 2 + endRepeat);

        // Blanked approach point at the polyline start.
        MapPixel(pairs[0], pairs[1], out var sx, out var sy);
        _out[_outCount++] = LaserPoint.CreateBlanked(sx, sy);

        // Lit samples at even arc-length steps; the last sample lands exactly on the end.
        var segIndex = 0;
        var segEnd = lastSegment;
        var aX = pairs[0];
        var aY = pairs[1];
        var bX = pairs[2];
        var bY = pairs[3];
        var segLen = SegmentLen(aX, aY, bX, bY);
        var segConsumed = 0f;
        float endX = 0f, endY = 0f;

        for (var s = 0; s <= steps; s++)
        {
            var target = s == steps ? length : s * (length / steps);
            while (segIndex < segEnd && segConsumed + segLen < target)
            {
                segConsumed += segLen;
                segIndex++;
                aX = pairs[2 * segIndex];
                aY = pairs[2 * segIndex + 1];
                var b2 = 2 * ((segIndex + 1) % vertexCount);
                bX = pairs[b2];
                bY = pairs[b2 + 1];
                segLen = SegmentLen(aX, aY, bX, bY);
            }

            var segT = segLen > 1e-6f ? (target - segConsumed) / segLen : 0f;
            var px = aX + segT * (bX - aX);
            var py = aY + segT * (bY - aY);
            // In fade: the scanner is still accelerating at a path's start, so a full-brightness
            // first sample blooms. Ramp from the configured dim level up to full.
            var level = s < fadeSamples && p.InFade > 0f
                            ? float.Lerp(Math.Clamp(p.InFade, 0f, 1f), 1f, (s + 1) / (float)(fadeSamples + 1))
                            : 1f;
            EmitLit(px, py, level);
            endX = px;
            endY = py;
        }

        // End repeats: the scanner never sees the frame's end, so hold the endpoint for a few
        // samples to make sure the shape is fully drawn. Out fade dims them so the hold doesn't
        // leave a bright dot of its own.
        if (endRepeat > 0)
        {
            var outFade = Math.Clamp(p.OutFade, 0f, 1f);
            for (var r = 1; r <= endRepeat; r++)
            {
                var level = outFade > 0f ? float.Lerp(1f, 1f - outFade, r / (float)(endRepeat + 1)) : 1f;
                EmitLit(endX, endY, level);
            }
        }
    }

    private static float SegmentLen(float aX, float aY, float bX, float bY)
        => MathF.Sqrt((bX - aX) * (bX - aX) + (bY - aY) * (bY - aY));

    /// <summary>One lit sample: color from the nearest working pixel, mapped into the field.
    /// <paramref name="level"/> is an extra 0..1 multiplier on top of Brightness - the in/out
    /// fade ramps pass their ramp position here.</summary>
    private void EmitLit(float x, float y, float level = 1f)
    {
        var p = _params;
        if (_outCount >= _out.Length)
            return;

        MapPixel(x, y, out var sx, out var sy);
        var nearest = PixelIndex(x, y);
        var packed = _color[nearest];
        var level_ = Math.Clamp(level, 0f, 1f);
        var r = (int)((packed >> 16 & 0xFF) * 257f * p.Brightness * level_);
        var g = (int)((packed >> 8 & 0xFF) * 257f * p.Brightness * level_);
        var b = (int)((packed & 0xFF) * 257f * p.Brightness * level_);
        _out[_outCount++] = new LaserPoint(sx, sy, r, g, b, 65535);
    }

    /// <summary>An isolated single-pixel path emits as a dwell dot (blanked jump + lit repeat).</summary>
    private void EmitDot(float x, float y)
    {
        var p = _params;
        if (p.Denoise >= 1 || p.MinPathLengthPct > 0f)
        {
            // A dot has zero length: shorter than any denoise threshold >= 1, and shorter than
            // any positive min-length filter.
            Stats.ShapesDropped++;
            return;
        }

        if (_shapes >= p.MaxPaths)
        {
            Stats.ShapesClipped++;
            return;
        }

        if (_outCount + 2 > _out.Length)
            return;

        _shapes++;
        MapPixel(x, y, out var sx, out var sy);
        _out[_outCount++] = LaserPoint.CreateBlanked(sx, sy);
        var nearest = PixelIndex(x, y);
        var packed = _color[nearest];
        var r = (int)((packed >> 16 & 0xFF) * 257f * p.Brightness);
        var g = (int)((packed >> 8 & 0xFF) * 257f * p.Brightness);
        var b = (int)((packed & 0xFF) * 257f * p.Brightness);
        // A dot is a zero-length path: the end repeats ARE its point intensity - how long the
        // beam holds on it.
        var dwell = 1 + Math.Clamp(p.EndRepeat, 0, 16);
        EnsureOut(_outCount + dwell);
        for (var d = 0; d < dwell && _outCount < _out.Length; d++)
            _out[_outCount++] = new LaserPoint(sx, sy, r, g, b, 65535);
    }

    /// <summary>Working pixel coordinate → ILDA field point: aspect-fit into the square field,
    /// Y flipped (texture rows go down, the scanner field goes up). Pixel centers sit at integer
    /// coordinates, so the grid spans -0.5..W-0.5 around its middle.</summary>
    private void MapPixel(float cx, float cy, out int fieldX, out int fieldY)
    {
        var scale = 2f * LaserSafetyTransforms.FieldMax / MathF.Max(_workW, _workH);
        var fx = (cx - (_workW - 1) * 0.5f) * scale;
        var fy = ((_workH - 1) * 0.5f - cy) * scale;
        fieldX = Math.Clamp((int)MathF.Round(fx), -(LaserSafetyTransforms.FieldMax - 1), LaserSafetyTransforms.FieldMax - 1);
        fieldY = Math.Clamp((int)MathF.Round(fy), -(LaserSafetyTransforms.FieldMax - 1), LaserSafetyTransforms.FieldMax - 1);
    }

    private int PixelIndex(float x, float y)
    {
        var ix = Math.Clamp((int)MathF.Round(x), 0, _workW - 1);
        var iy = Math.Clamp((int)MathF.Round(y), 0, _workH - 1);
        return iy * _workW + ix;
    }

    // --------------------------------------------------------------------------------------------
    // Canny: Gaussian blur → Sobel → non-max suppression → hysteresis. The edge map lands in
    // _binary; _mag doubles as the blur temp (blur finishes before Sobel writes it).
    // --------------------------------------------------------------------------------------------

    private void Canny(float threshold, int blurSize)
    {
        Blur(_gray, _blurred, blurSize);
        SobelNms(_blurred, _mag);
        NonMaxSuppress(_mag);
        Hysteresis(_mag, threshold);
    }

    /// <summary>Separable binomial blur; edge pixels clamp at the border.</summary>
    private void Blur(float[] src, float[] dst, int blurSize)
    {
        if (blurSize <= 0 || src.Length == 0)
        {
            Array.Copy(src, dst, src.Length);
            return;
        }

        ReadOnlySpan<float> kernel = blurSize switch
                                     {
                                         1 => [1f/4, 2f/4, 1f/4],
                                         2 => [1f/16, 4f/16, 6f/16, 4f/16, 1f/16],
                                         3 => [1f/64, 6f/64, 15f/64, 20f/64, 15f/64, 6f/64, 1f/64],
                                         4 => [1f/256, 8f/256, 28f/256, 56f/256, 70f/256, 56f/256, 28f/256, 8f/256, 1f/256],
                                         _ => [1f],
                                     };
        var k = kernel.Length;
        var half = k / 2;
        var w = _workW;
        var h = _workH;

        // Horizontal src → _mag (temp), vertical _mag → dst.
        var temp = _mag;
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                var acc = 0f;
                for (var t = 0; t < k; t++)
                    acc += src[row + Math.Clamp(x + t - half, 0, w - 1)] * kernel[t];

                temp[row + x] = acc;
            }
        }

        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                var acc = 0f;
                for (var t = 0; t < k; t++)
                    acc += temp[Math.Clamp(y + t - half, 0, h - 1) * w + x] * kernel[t];

                dst[row + x] = acc;
            }
        }
    }

    /// <summary>Sobel gradients + gradient-direction buckets in one pass. Magnitudes land in
    /// <paramref name="mag"/>, direction buckets in <see cref="_dirq"/>.</summary>
    private void SobelNms(float[] src, float[] mag)
    {
        var w = _workW;
        var h = _workH;

        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                var i = y * w + x;
                var tl = src[i - w - 1];
                var tc = src[i - w];
                var tr = src[i - w + 1];
                var ml = src[i - 1];
                var mr = src[i + 1];
                var bl = src[i + w - 1];
                var bc = src[i + w];
                var br = src[i + w + 1];

                var gx = (tr + 2 * mr + br) - (tl + 2 * ml + bl);
                var gy = (bl + 2 * bc + br) - (tl + 2 * tc + tr);
                var magI = MathF.Sqrt(gx * gx + gy * gy);
                mag[i] = magI;

                // Direction bucket: 0 = E/W neighbors, 1 = NE/SW, 2 = N/S, 3 = NW/SE.
                var aGX = MathF.Abs(gx);
                var aGY = MathF.Abs(gy);
                byte bucket;
                if (aGX > 2.414f * aGY)
                    bucket = 0;
                else if (aGY > 2.414f * aGX)
                    bucket = 2;
                else if ((gx > 0) == (gy > 0))
                    bucket = 1;
                else
                    bucket = 3;

                _dirq[i] = bucket;
            }
        }
    }

    /// <summary>Suppresses non-maxima: a candidate pixel survives when its gradient is at least as
    /// strong as both neighbors along the gradient direction. Survivors are flagged in _visited.</summary>
    private void NonMaxSuppress(float[] mag)
    {
        var w = _workW;
        var h = _workH;
        var keep = _visited;
        Array.Clear(keep, 0, w * h);

        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                var i = y * w + x;
                var m = mag[i];
                if (m <= 0f)
                    continue;

                (int dx, int dy) = _dirq[i] switch
                                   {
                                       0 => (1, 0),
                                       1 => (1, 1),
                                       2 => (0, 1),
                                       _ => (1, -1),
                                   };

                var n1 = mag[i + dx + dy * w];
                var n2 = mag[i - dx - dy * w];
                if (m >= n1 && m >= n2)
                    keep[i] = 1;
            }
        }
    }

    /// <summary>Hysteresis: seeds the edge map with strong candidates and floods weak ones
    /// connected to them. The edge map lands in _binary.</summary>
    private void Hysteresis(float[] mag, float threshold)
    {
        var w = _workW;
        var h = _workH;
        var keep = _visited;
        var bin = _binary;
        var n = w * h;
        Array.Clear(bin, 0, n);

        // Sobel magnitude scale: max |gx| or |gy| is 4, so Threshold 0..1 maps onto it.
        var hi = Math.Clamp(threshold, 0f, 1f) * 4f;
        var lo = hi * 0.4f;

        var stack = _toDelete;
        stack.Clear();

        for (var i = 0; i < n; i++)
        {
            if (keep[i] != 0 && mag[i] >= hi)
            {
                bin[i] = 1;
                stack.Add(i);
            }
        }

        while (stack.Count > 0)
        {
            var i = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            var x = i % w;
            var y = i / w;
            for (var d = 0; d < 8; d++)
            {
                var nx = x + NeighborDx[d];
                var ny = y + NeighborDy[d];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    continue;

                var ni = ny * w + nx;
                if (bin[ni] != 0 || keep[ni] == 0 || mag[ni] < lo)
                    continue;

                bin[ni] = 1;
                stack.Add(ni);
            }
        }
    }

    // --------------------------------------------------------------------------------------------
    // Fields
    // --------------------------------------------------------------------------------------------

    private int _workW, _workH;

    private float[] _gray = [];
    private uint[] _color = [];
    private byte[] _binary = [];
    private float[] _blurred = [];
    private float[] _mag = [];
    private byte[] _dirq = [];
    private byte[] _visited = [];

    private LaserPoint[] _out = [];
    private int _outCount;
    private int _shapes;
    private bool _overBudget;

    private readonly List<int> _toDelete = [];
    private List<float> _pathFloats = [];
    private readonly List<float> _pathBackward = [];
    private List<float> _pathAssembled = [];

    private int[] _segK1 = [];
    private int[] _segK2 = [];
    private float[] _segAx = [];
    private float[] _segAy = [];
    private float[] _segBx = [];
    private float[] _segBy = [];
    private int _segCount;
    private int[] _epKey = [];
    private int[] _epSeg = [];
    private int[] _link1 = [];
    private int[] _link2 = [];
    private byte[] _segUsed = [];

    /// <summary>Grows the output buffer so at least <paramref name="min"/> points fit.</summary>
    private void EnsureOut(int min)
    {
        if (_out.Length >= min)
            return;

        var newSize = Math.Max(min, Math.Max(1024, _out.Length * 2));
        Array.Resize(ref _out, newSize);
    }
}
