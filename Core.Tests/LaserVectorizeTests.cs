using System;
using System.Collections.Generic;
using Lib.Utils;
using T3.Core.DataTypes;
using Xunit;
using Xunit.Abstractions;

namespace Core.Tests;

/// <summary>
/// CPU pipeline tests for <see cref="LaserVectorizeSession"/>: synthetic images in, shapes out.
/// Exercises max-pool downscale, skeleton path tracing, marching-squares contours, Canny, denoise
/// and the coordinate mapping - the stages that turn a texture into laser shapes.
/// </summary>
public sealed class LaserVectorizeTests
{
    private readonly ITestOutputHelper _output;

    public LaserVectorizeTests(ITestOutputHelper output)
    {
        _output = s_ ??= output;
    }

    private static ITestOutputHelper? s_;

    private static byte[] Image(int w, int h) => new byte[w * h * 4];

    private static void Line(byte[] img, int w, int y, int x0, int x1, byte v = 255)
    {
        for (var x = x0; x <= x1; x++)
        {
            var o = (y * w + x) * 4;
            img[o] = v; img[o + 1] = v; img[o + 2] = v; img[o + 3] = 255;
        }
    }

    private static void LineV(byte[] img, int w, int h, int x, int y0, int y1, byte v = 255)
    {
        for (var y = y0; y <= y1; y++)
        {
            var o = (y * w + x) * 4;
            img[o] = v;
            img[o + 1] = v;
            img[o + 2] = v;
            img[o + 3] = 255;
        }
    }

    private static void Dot(byte[] img, int w, int x, int y, byte v = 255)
    {
        var o = (y * w + x) * 4;
        img[o] = v; img[o + 1] = v; img[o + 2] = v; img[o + 3] = 255;
    }

    private static void FillCircle(byte[] img, int w, int h, int cx, int cy, int r, byte v = 255)
    {
        for (var y = cy - r; y <= cy + r; y++)
        {
            for (var x = cx - r; x <= cx + r; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                if (dx * dx + dy * dy <= r * r)
                {
                    var o = (y * w + x) * 4;
                    img[o] = v; img[o + 1] = v; img[o+2] = v; img[o + 3] = 255;
                }
            }
        }
    }

    /// <summary>Runs the session over a BGRA8 image and returns (session, list of emitted points).</summary>
    private static (LaserVectorizeSession Session, LaserPoint[] Points) RunImage(byte[] img, int w, int h,
        LaserVectorizeSession.DetectMode mode, float threshold = 0.2f, int thickness = 1,
        int denoise = 0, float spacing = 4f, int maxRes = 256)
    {
        var session = new LaserVectorizeSession();
        Assert.True(session.BeginCopy(w, h, maxRes));
        session.CopyFrame(img, w * 4, w, h, LaserVectorizeSession.FmtBgra8);
        session.Run(new LaserVectorizeSession.Params
                        {
                            Mode = mode,
                            Threshold = threshold,
                            Thickness = thickness,
                            BlurSize = 1,
                            Denoise = denoise,
                            MaxPaths = 512,
                            PointSpacing = spacing,
                            UseColor = false,
                            Brightness = 1f,
                            PointCap = 1 << 17,
                        });
        var points = new LaserPoint[session.Count];
        Array.Copy(session.Buffer, points, session.Count);
        return (session, points);
    }

    [Fact]
    public void EndRepeat_ExtendsShapeTail()
    {
        var w = 128;
        var h = 64;
        var img = Image(w, h);
        Line(img, w, 32, 8, 119);
        var (_, baseline) = RunImage(img, w, h, LaserVectorizeSession.DetectMode.FindPaths, maxRes: 64);

        var session = new LaserVectorizeSession();
        Assert.True(session.BeginCopy(w, h, 64));
        session.CopyFrame(img, w * 4, w, h, LaserVectorizeSession.FmtBgra8);
        session.Run(new LaserVectorizeSession.Params
                        {
                            Mode = LaserVectorizeSession.DetectMode.FindPaths,
                            Threshold = 0.2f,
                            BlurSize = 1,
                            MaxPaths = 512,
                            PointSpacing = 4f,
                            Brightness = 1f,
                            PointCap = 1 << 17,
                            EndRepeat = 4,
                        });
        var repeated = new LaserPoint[session.Count];
        Array.Copy(session.Buffer, repeated, session.Count);

        Assert.Equal(baseline.Length + 4, repeated.Length);
        // The tail holds the same endpoint four extra samples.
        Assert.Equal(baseline[^1], repeated[^1]);
        Assert.Equal(repeated[^2], repeated[^1]);
    }

    [Fact]
    public void InFade_DimsTheFirstSamples()
    {
        var w = 128;
        var h = 64;
        var img = Image(w, h);
        Line(img, w, 32, 8, 119);
        var session = new LaserVectorizeSession();
        Assert.True(session.BeginCopy(w, h, 64));
        session.CopyFrame(img, w * 4, w, h, LaserVectorizeSession.FmtBgra8);
        session.Run(new LaserVectorizeSession.Params
                        {
                            Mode = LaserVectorizeSession.DetectMode.FindPaths,
                            Threshold = 0.2f,
                            BlurSize = 1,
                            MaxPaths = 512,
                            PointSpacing = 4f,
                            Brightness = 1f,
                            PointCap = 1 << 17,
                            InFade = 0.5f,
                        });
        var points = new LaserPoint[session.Count];
        Array.Copy(session.Buffer, points, session.Count);

        // The first lit sample (right after the blanked approach) is dimmed; a later one is full.
        var firstLit = points[1];
        Assert.True(firstLit.I > 0);
        Assert.True(firstLit.R < 65535, "the in-faded start sample must be dimmer than full");
    }

    [Fact]
    public void PathFiltering_DropsShortAndLongShapes()
    {
        var w = 128;
        var h = 64;
        var img = Image(w, h);
        Line(img, w, 16, 8, 119);   // long horizontal line (~87% of the width)
        Dot(img, w, 10, 48);        // isolated dot (zero length)

        var (_, baseline) = RunImage(img, w, h, LaserVectorizeSession.DetectMode.FindPaths, denoise: 0, maxRes: 64);
        Assert.True(CountShapes(baseline) >= 1);

        // Min length 20% of the 64px working width = ~13px: the dot survives as a dwell dot but
        // shorter strokes would drop; a max length of 50% must drop the long line.
        var session = new LaserVectorizeSession();
        Assert.True(session.BeginCopy(w, h, 64));
        session.CopyFrame(img, w * 4, w, h, LaserVectorizeSession.FmtBgra8);
        session.Run(new LaserVectorizeSession.Params
                        {
                            Mode = LaserVectorizeSession.DetectMode.FindPaths,
                            Threshold = 0.2f,
                            BlurSize = 1,
                            MaxPaths = 512,
                            PointSpacing = 4f,
                            Brightness = 1f,
                            PointCap = 1 << 17,
                            MinPathLengthPct = 5f,
                            MaxPathLengthPct = 50f,
                        });
        var points = new LaserPoint[session.Count];
        Array.Copy(session.Buffer, points, session.Count);

        Assert.Equal(0, CountShapes(points));
        Assert.True(session.Stats.ShapesDropped >= 2, "the over-long line and the dot must both drop");
    }

    private static int CountShapes(LaserPoint[] points)
    {
        // Shapes = lit runs separated by blanked separators.
        var shapes = 0;
        var inLit = false;
        for (var i = 0; i < points.Length; i++)
        {
            if (points[i].I <= 0)
            {
                inLit = false;
                continue;
            }

            if (!inLit)
            {
                shapes++;
                inLit = true;
            }
        }

        return shapes;
    }

    [Fact]
    public void MaxPool_PreservesThinLine()
    
    {
        var w = 128;
        var h = 64;
        var img = Image(w, h);
        Line(img, w, 32, 8, 119);
        var (session, _) = RunImage(img, w, h, LaserVectorizeSession.DetectMode.FindPaths, maxRes: 64);
        Assert.Equal(64, session.WorkWidth);
        Assert.Equal(32, session.WorkHeight);
        Assert.True(session.Stats.Shapes > 0, "thin line must survive the downscale");
    }

    [Fact]
    public void FindPaths_CrossBecomesTwoStrokes()
    {
        var w = 256;
        var h = 256;
        var img = Image(w, h);
        Line(img, w, 128, 16, 239);
        LineV(img, w, h, 128, 16, 239);
        var (session, points) = RunImage(img, w, h, LaserVectorizeSession.DetectMode.FindPaths);
        Assert.True(session.Stats.Shapes >= 2, $"expected >= 2 shapes, got {session.Stats.Shapes}");
        Assert.True(CountShapes(points) >= 2);
        // A skeleton stroke is 1px wide: total points stay modest relative to spacing.
        Assert.True(session.Stats.Points < 800, $"point count blew up: {session.Stats.Points}");
    }

    [Fact]
    public void Denoise_DropsSpeck()
    
    {
        var w = 128;
        var h = 128;
        var img = Image(w, h);
        Line(img, w, 64, 8, 119);
        Dot(img, w, 100, 100);
        var withSpeck = RunImage(img, w, h, LaserVectorizeSession.DetectMode.FindPaths, denoise: 0);
        Assert.True(withSpeck.Session.Stats.Shapes >= 2, "dot counts as a shape when Denoise is 0");
        var cleaned = RunImage(img, w, h, LaserVectorizeSession.DetectMode.FindPaths, denoise: 20);
        Assert.Equal(1, cleaned.Session.Stats.Shapes);
    }

    [Fact]
    public void ThicknessAboveOne_TracesBlobOutline()
    {
        var w = 256;
        var h = 256;
        var img = Image(w, h);
        FillCircle(img, w, h, 128, 128, 60);
        var (_, points) = RunImage(img, w, h, LaserVectorizeSession.DetectMode.FindPaths, thickness: 8);
        Assert.True(CountShapes(points) >= 1, "blob must produce at least one outline shape");
        var firstBlank = Array.FindIndex(points, q => q.I <= 0);
        Assert.True(firstBlank >= 0, "outline emitted with blanked separator");
        var lit = points[(firstBlank + 1)..];
        var litCount = Array.FindIndex(lit, q => q.I <= 0);
        var n = litCount < 0 ? lit.Length : litCount;
        Assert.True(n > 40, $"circle outline should be dense, got {n} lit points");
    }

    [Fact]
    public void Canny_FindsSquareEdges()
    {
        var w = 256;
        var h = 256;
        var img = Image(w, h);
        FillCircle(img, w, h, 128, 128, 70);
        var (_, points) = RunImage(img, w, h, LaserVectorizeSession.DetectMode.FindContours, threshold: 0.2f);
        Assert.True(CountShapes(points) >= 1, "Canny must find the circle edge");
    }

    [Fact]
    public void FieldMapping_FlipsY()
    {
        var w = 128;
        var h = 128;
        var img = Image(w, h);
        Line(img, w, 16, 8, 119); // near the TOP row -> maps to positive field Y
        var (_, points) = RunImage(img, w, h, LaserVectorizeSession.DetectMode.FindPaths);
        var firstLit = Array.Find(points, q => q.I > 0);
        Assert.True(firstLit.Y > 0, $"top-row content must map to positive Y, got {firstLit.Y}");
        Assert.True(firstLit.X < 0, "left-side content maps to negative X");
    }
}
