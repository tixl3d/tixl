extern alias skiasvg;
#nullable enable
using System;
using System.Collections.Generic;
using SkiaSharp;
using S = skiasvg::Svg;
using SP = skiasvg::Svg.Pathing;
using ST = skiasvg::Svg.Transforms;

namespace Lib.Utils;

/// <summary>
/// Turns SVG shapes into point lists without System.Drawing, which is Windows-only since .NET 7.
/// </summary>
/// <remarks>
/// The SVG library this uses (Svg.Custom) is a fork of the one the other SVG operators still use, and declares
/// the same types in the same namespace - hence the "skiasvg" alias, without which the two collide (CS0433).
/// The fork drops the GraphicsPath-based geometry API, so paths are assembled from the DOM's own segments.
/// </remarks>
public static class SkiaSvgGeometry
{
    /// <summary>How curves become points.</summary>
    public enum FlattenModes
    {
        /// <summary>Points where the curvature is, straight runs left cheap - what GraphicsPath.Flatten did.</summary>
        Adaptive = 0,

        /// <summary>Evenly spaced along the outline, which is usually what a point cloud wants.</summary>
        EvenSpacing = 1,
    }

    public static bool TryLoad(FileResource file, S.SvgDocument? currentValue,
                               [NotNullWhen(true)] out S.SvgDocument? newValue,
                               [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            newValue = S.SvgDocument.Open(file.AbsolutePath);
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            newValue = null;
            failureReason = "Failed to load svg file:" + e.Message;
            return false;
        }
    }

    /// <summary>The element's own geometry, already placed by its own and its ancestors' transforms.</summary>
    public static SKPath? TryBuildPath(S.SvgElement element)
    {
        var path = BuildLocalPath(element);
        if (path == null)
            return null;

        var matrix = AccumulatedTransform(element);
        if (!matrix.Equals(SKMatrix.Identity))
            path.Transform(matrix);

        return path;
    }

    private static SKPath? BuildLocalPath(S.SvgElement element)
    {
        var path = new SKPath();
        switch (element)
        {
            case S.SvgPath p:
                AppendSegments(path, p.PathData);
                break;

            // A glyph in an SVG font carries its outline the same way a path does.
            case S.SvgGlyph g:
                AppendSegments(path, g.PathData);
                break;

            case S.SvgRectangle r:
            {
                var rect = SKRect.Create(r.X.Value, r.Y.Value, r.Width.Value, r.Height.Value);
                var rx = r.CornerRadiusX.Value;
                var ry = r.CornerRadiusY.Value;
                if (rx > 0 || ry > 0)
                    path.AddRoundRect(rect, rx > 0 ? rx : ry, ry > 0 ? ry : rx);
                else
                    path.AddRect(rect);
                break;
            }

            case S.SvgCircle c:
                path.AddCircle(c.CenterX.Value, c.CenterY.Value, c.Radius.Value);
                break;

            case S.SvgEllipse e:
                path.AddOval(SKRect.Create(e.CenterX.Value - e.RadiusX.Value, e.CenterY.Value - e.RadiusY.Value,
                                           e.RadiusX.Value * 2, e.RadiusY.Value * 2));
                break;

            case S.SvgLine l:
                path.MoveTo(l.StartX.Value, l.StartY.Value);
                path.LineTo(l.EndX.Value, l.EndY.Value);
                break;

            // Polyline derives from Polygon, so it has to be matched first or it never is.
            case S.SvgPolyline pl:
                AppendPoly(path, pl.Points, close: false);
                break;

            case S.SvgPolygon pg:
                AppendPoly(path, pg.Points, close: true);
                break;

            default:
                path.Dispose();
                return null;
        }

        if (path.IsEmpty)
        {
            path.Dispose();
            return null;
        }

        return path;
    }

    private static void AppendSegments(SKPath path, SP.SvgPathSegmentList? data)
    {
        if (data == null)
            return;

        // H and V give only one coordinate and leave the other NaN, meaning "keep the current value". The
        // GraphicsPath API resolved that internally; here it has to be explicit, or half the geometry is lost.
        var current = new SKPoint(0, 0);

        foreach (var segment in data)
        {
            switch (segment)
            {
                // MoveTo derives from LineSegment, so it has to be matched first.
                case SP.SvgMoveToSegment m:
                    current = Resolve(m.End, current);
                    path.MoveTo(current);
                    break;

                case SP.SvgLineSegment l:
                    current = Resolve(l.End, current);
                    path.LineTo(current);
                    break;

                case SP.SvgCubicCurveSegment c:
                    current = Resolve(c.End, current);
                    path.CubicTo(c.FirstControlPoint.X, c.FirstControlPoint.Y,
                                 c.SecondControlPoint.X, c.SecondControlPoint.Y,
                                 current.X, current.Y);
                    break;

                case SP.SvgQuadraticCurveSegment q:
                    current = Resolve(q.End, current);
                    path.QuadTo(q.ControlPoint.X, q.ControlPoint.Y, current.X, current.Y);
                    break;

                case SP.SvgArcSegment a:
                    current = Resolve(a.End, current);
                    path.ArcTo(a.RadiusX, a.RadiusY, a.Angle,
                               a.Size == SP.SvgArcSize.Large ? SKPathArcSize.Large : SKPathArcSize.Small,
                               a.Sweep == SP.SvgArcSweep.Positive ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
                               current.X, current.Y);
                    break;

                case SP.SvgClosePathSegment:
                    path.Close();
                    break;
            }
        }
    }

    private static void AppendPoly(SKPath path, S.SvgPointCollection? points, bool close)
    {
        if (points == null)
            return;

        for (var i = 0; i + 1 < points.Count; i += 2)
        {
            if (i == 0)
                path.MoveTo(points[0].Value, points[1].Value);
            else
                path.LineTo(points[i].Value, points[i + 1].Value);
        }

        if (close)
            path.Close();
    }

    private static SKPoint Resolve(System.Drawing.PointF end, SKPoint current)
        => new(float.IsNaN(end.X) ? current.X : end.X,
               float.IsNaN(end.Y) ? current.Y : end.Y);

    /// <summary>The element's transform with every ancestor's applied outside it, outermost first.</summary>
    private static SKMatrix AccumulatedTransform(S.SvgElement element)
    {
        var matrix = SKMatrix.Identity;

        for (var node = element; node != null; node = node.Parent)
        {
            var local = LocalTransform(node);
            if (!local.Equals(SKMatrix.Identity))
                matrix = local.PreConcat(matrix);
        }

        return matrix;
    }

    private static SKMatrix LocalTransform(S.SvgElement element)
    {
        var transforms = element.Transforms;
        if (transforms == null || transforms.Count == 0)
            return SKMatrix.Identity;

        var matrix = SKMatrix.Identity;
        foreach (var transform in transforms)
        {
            var step = transform switch
                           {
                               ST.SvgTranslate t => SKMatrix.CreateTranslation(t.X, t.Y),
                               ST.SvgScale s     => SKMatrix.CreateScale(s.X, s.Y),
                               ST.SvgRotate r    => SKMatrix.CreateRotationDegrees(r.Angle, r.CenterX, r.CenterY),
                               ST.SvgSkew k      => SKMatrix.CreateSkew(MathF.Tan(k.AngleX * MathF.PI / 180f),
                                                                        MathF.Tan(k.AngleY * MathF.PI / 180f)),
                               _                 => SKMatrix.Identity,
                           };

            matrix = matrix.PreConcat(step);
        }

        return matrix;
    }

    /// <summary>
    /// Points for one path, one list per contour so the caller can keep sub-paths apart. A contour the path
    /// declares closed ends where it started, so callers do not have to join it up themselves.
    /// </summary>
    public static List<List<SKPoint>> Flatten(SKPath path, FlattenModes mode, float amount)
        => mode == FlattenModes.EvenSpacing ? FlattenEvenly(path, amount) : FlattenByCurvature(path, amount);

    private static List<List<SKPoint>> FlattenEvenly(SKPath path, float spacing)
    {
        var contours = new List<List<SKPoint>>();
        using var measure = new SKPathMeasure(path, false);

        do
        {
            var length = measure.Length;
            if (length <= 0)
                continue;

            var steps = Math.Max(1, (int)MathF.Ceiling(length / MathF.Max(0.001f, spacing)));
            var points = new List<SKPoint>(steps + 1);

            for (var i = 0; i <= steps; i++)
            {
                if (measure.GetPosition(length * i / steps, out var position))
                    points.Add(position);
            }

            if (points.Count > 0)
                contours.Add(points);
        }
        while (measure.NextContour());

        return contours;
    }

    private static List<List<SKPoint>> FlattenByCurvature(SKPath path, float tolerance)
    {
        var contours = new List<List<SKPoint>>();
        List<SKPoint>? current = null;

        using var iterator = path.CreateIterator(false);
        var points = new SKPoint[4];

        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    current = [points[0]];
                    contours.Add(current);
                    break;

                case SKPathVerb.Line:
                    current?.Add(points[1]);
                    break;

                case SKPathVerb.Quad:
                case SKPathVerb.Conic:
                    if (current != null)
                        SubdivideQuad(current, points[0], points[1], points[2], tolerance, 0);
                    break;

                case SKPathVerb.Cubic:
                    if (current != null)
                        SubdivideCubic(current, points[0], points[1], points[2], points[3], tolerance, 0);
                    break;

                // The iterator reports the close but not the segment it implies, so a closed outline would
                // otherwise come back a segment short - visible as a gap in every glyph that uses 'Z'.
                // SKPathMeasure, which the even-spacing mode uses, already walks that segment itself.
                case SKPathVerb.Close:
                    if (current is { Count: > 0 })
                        current.Add(current[0]);
                    break;
            }
        }

        contours.RemoveAll(c => c.Count < 2);
        return contours;
    }

    /// <summary>Deep enough for any sane curve; the guard only stops a degenerate one from recursing forever.</summary>
    private const int MaxSubdivision = 16;

    private static void SubdivideCubic(List<SKPoint> into, SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float tolerance, int depth)
    {
        if (depth >= MaxSubdivision || Flatness(p0, p1, p2, p3) <= tolerance)
        {
            into.Add(p3);
            return;
        }

        SKPoint m01 = Mid(p0, p1), m12 = Mid(p1, p2), m23 = Mid(p2, p3);
        SKPoint a = Mid(m01, m12), b = Mid(m12, m23), m = Mid(a, b);

        SubdivideCubic(into, p0, m01, a, m, tolerance, depth + 1);
        SubdivideCubic(into, m, b, m23, p3, tolerance, depth + 1);
    }

    private static void SubdivideQuad(List<SKPoint> into, SKPoint p0, SKPoint p1, SKPoint p2, float tolerance, int depth)
    {
        if (depth >= MaxSubdivision || Distance(p1, Mid(p0, p2)) <= tolerance)
        {
            into.Add(p2);
            return;
        }

        SKPoint m01 = Mid(p0, p1), m12 = Mid(p1, p2), m = Mid(m01, m12);

        SubdivideQuad(into, p0, m01, m, tolerance, depth + 1);
        SubdivideQuad(into, m, m12, p2, tolerance, depth + 1);
    }

    private static SKPoint Mid(SKPoint a, SKPoint b) => new((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);

    private static float Distance(SKPoint a, SKPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float Flatness(SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3)
    {
        var chord = Mid(p0, p3);
        return MathF.Max(Distance(p1, chord), Distance(p2, chord));
    }
}
