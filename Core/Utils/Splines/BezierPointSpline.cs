using System.Runtime.CompilerServices;
using T3.Core.DataTypes;

namespace T3.Core.Utils.Splines;

public static class BezierPointSpline
{
    public static Point[] SamplePointsEvenly(int count, int preSampleSteps, float curvature, Vector3 upVector, ref Point[] sourcePoints)
    {
        count.Clamp(1, 1000);
        var result = new Point[count];

        if (_lengthList.Length < preSampleSteps)
        {
            _lengthList = new float[preSampleSteps];
        }

        // Pre-sample bezier curve for even distribution
        var totalLength = 0f;
        var lastPoint = SampleCubicBezier(0, curvature,  sourcePoints);
        for (var preSampleIndex = 1; preSampleIndex < preSampleSteps; preSampleIndex++)
        {
            var t = (float)preSampleIndex / preSampleSteps;
            var newPoint = SampleCubicBezier(t, curvature,  sourcePoints);
            var stepLength = Vector3.Distance(newPoint, lastPoint);
            lastPoint = newPoint;
            totalLength += stepLength;
            _lengthList[preSampleIndex] = totalLength;
        }

        var walkedIndex = 0;

        Vector3 lastPos = SampleCubicBezier(0, curvature,  sourcePoints);

        for (var index = 0; index < count; index++)
        {
            var wantedLength = totalLength * index / (count - 1) + 0.0002f;

            while (wantedLength > _lengthList[walkedIndex + 1] && walkedIndex < preSampleSteps - 2 && walkedIndex < _lengthList.Length-2)
            {
                walkedIndex++;
            }

            var l0 = _lengthList[walkedIndex];
            var l1 = _lengthList[walkedIndex + 1];

            var deltaL = (l1 - l0);

            var fraction = (wantedLength - l0) / (deltaL + 0.00001f);

            var t = (walkedIndex + fraction) / (preSampleSteps - 1);
            var pos = SampleCubicBezier(t - 0.0002f, curvature, sourcePoints);
            result[index].Position = pos;

            var d = pos - lastPos;
            lastPos = pos;

            result[index].F1 = 1;
            result[index].Orientation = MathUtils.LookAt(Vector3.Normalize(d), -upVector);
            result[index].Color = SampleLinearColors(t, ref sourcePoints);
            result[index].Scale = new Vector3(1.0f, 1.0f, 1.0f);
            result[index].F2 = 1;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 SampleCubicBezier2(float t, ref Point[] points)
    {
        int i;
        if (t >= 1f)
        {
            t = 1f;
            i = points.Length - 4;
        }
        else
        {
            t = t.Clamp(0, 1) * (points.Length - 1) / 3;
            i = (int)t;
            t -= i;
            i = (i * 3).Clamp(0, points.Length - 4);
        }

        return Bezier.GetPoint(points[i].Position, points[i + 1].Position, points[i + 2].Position, points[i + 3].Position, t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 SampleLinearColors(float t, ref Point[] points)
    {
        int i;

        if (t >= 1f)
        {
            t = 1f;
            i = points.Length - 2; // Adjusted index
        }
        else
        {
            var tt = t * (points.Length - 1);
            i = (int)tt;
            t = tt - i;
        }

        var pA = points[i].Color;
        var pB = points[i + 1].Color;

        // Perform linear interpolation between colors
        var interpolatedColor = pA + (pB - pA) * t;

        return interpolatedColor;
    }


    public static Vector3 SampleCubicBezier(float t, float curvature, Point[] points)
    {
        int i;

        if (t >= 1f)
        {
            t = 1f;
            i = points.Length - 2;
        }
        else
        {
            float tt = t * (points.Length - 2);
            i = (int)tt;
            t = tt - i;
        }

        var pA = points[i].Position;
        var pB = points[i + 1].Position;

        var pNext = pB;
        var pLast = pA;

        if (i > 0)
        {
            pLast = points[i - 1].Position;
        }

        if (i < points.Length - 2)
        {
            pNext = points[i + 2].Position;
        }

        return Bezier.GetPoint(pA,
                               pA - (pLast - pB) / curvature * points[i].F1,
                               pB + (pA - pNext) / curvature * points[i + 1].F1,
                               pB,
                               t);
    }

    private static float[] _lengthList = new float[10];

    public static Point[] SamplePoints(int count, ref Point[] sourcePoints)
    {
        count.Clamp(1, 1000);
        var result = new Point[count];
        for (var index = 0; index < count; index++)
        {
            var t = (float)index / count;

            result[index].Position = SampleCubicBezier(t, 4, sourcePoints);
            result[index].F1 = 1;
        }

        return result;
    }
}