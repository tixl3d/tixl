using System;
using System.Collections.Generic;

namespace T3.Core.Animation;

internal static class SplineInterpolator
{
    internal static void UpdateTangents(List<KeyValuePair<double, VDefinition>> curveElements)
    {
        if (curveElements.Count <= 1)
        {
            return;
        }
        else
        {  // more than 2 points

            //calculate the tangents for each point
            var cur = curveElements[0];
            var next = curveElements[1];
            cur.Value.OutTangentAngle = CalcStartTangent(cur, next);
            cur.Value.InTangentAngle = cur.Value.OutTangentAngle - Math.PI;

            var prev = new KeyValuePair<double, VDefinition>();
            for (int i = 1; i < curveElements.Count - 1; ++i)
            {
                prev = curveElements[i - 1];
                cur = curveElements[i];
                next = curveElements[i + 1];
                cur.Value.InTangentAngle = CalcInTangent(prev, cur, next);
                cur.Value.OutTangentAngle = CalcOutTangent(prev, cur, next);
            }
            prev = cur;
            cur = next;

            cur.Value.InTangentAngle = CalcEndTangent(prev, cur);
            cur.Value.OutTangentAngle = cur.Value.InTangentAngle - Math.PI;
        }
    }


    /**
     * see http://tooll.framefield.com/500 and http://en.wikipedia.org/wiki/Monotone_cubic_interpolation
     */
    public static double Interpolate(KeyValuePair<double, VDefinition> a, KeyValuePair<double, VDefinition> b, double u)
    {
        double t = (u - a.Key) / (b.Key - a.Key);

        double tangentLength = (b.Key - a.Key);
        var p0 = a.Value.Value;
        var m0 = Math.Tan(a.Value.OutTangentAngle) * tangentLength;
        var p1 = b.Value.Value;
        var m1 = Math.Tan(b.Value.InTangentAngle) * tangentLength;

        var t2 = t * t;
        var t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
    }


    private static double CalcStartTangent(KeyValuePair<double, VDefinition> a, KeyValuePair<double, VDefinition> b)
    {
        switch (a.Value.OutEditMode)
        {
            case VDefinition.EditMode.Tangent:
                return a.Value.OutTangentAngle;

            case VDefinition.EditMode.Linear:
                var angle = Math.PI / 2 - Math.Atan2(a.Key - b.Key, a.Value.Value - b.Value.Value);
                return angle;
            default:
                return Math.PI;
        }
    }

    private static double CalcEndTangent(KeyValuePair<double, VDefinition> a, KeyValuePair<double, VDefinition> b)
    {
        switch (b.Value.InEditMode)
        {
            case VDefinition.EditMode.Tangent:
                return b.Value.InTangentAngle;

            case VDefinition.EditMode.Linear:
                var angle = Math.PI / 2 - Math.Atan2(b.Key - a.Key, b.Value.Value - a.Value.Value);
                return angle;
            default:
                return 0;
        }
    }

    private const double TANGENT_CLAMP_RATIO = 1.5;    // This is a adjusted to avoid avoid-shooting for default cubic spline blending

    private static double CalcInTangent(KeyValuePair<double, VDefinition> prev, KeyValuePair<double, VDefinition> cur, KeyValuePair<double, VDefinition> next)
    {
        switch (cur.Value.InEditMode)
        {
            case VDefinition.EditMode.Tangent:
                return cur.Value.InTangentAngle;
            case VDefinition.EditMode.Smooth:
                var angle = Math.PI / 2 - Math.Atan2(next.Key - prev.Key, next.Value.Value - prev.Value.Value);

                double thirdToPrev = (prev.Key - cur.Key) / TANGENT_CLAMP_RATIO;
                double thirdToNext = (next.Key - cur.Key) / TANGENT_CLAMP_RATIO;

                // Synced to OutTangent and avoid overshooting
                if (prev.Value.Value > next.Value.Value && (cur.Value.Value + Math.Tan(angle) * thirdToNext) < next.Value.Value)
                {
                    angle = Math.PI + Math.PI / 2 - Math.Atan2(-thirdToNext, Math.Max(0, cur.Value.Value - next.Value.Value));
                }
                else if (prev.Value.Value < next.Value.Value && (cur.Value.Value + Math.Tan(angle) * thirdToNext) > next.Value.Value)
                {
                    angle = Math.PI + Math.PI / 2 - Math.Atan2(-thirdToNext, Math.Min(0, cur.Value.Value - next.Value.Value));
                }

                // Avoid Overshooting to previous keyframe                    
                else if (prev.Value.Value > next.Value.Value && (cur.Value.Value + Math.Tan(angle) * thirdToPrev) > prev.Value.Value)
                {
                    angle = Math.PI + Math.PI / 2 - Math.Atan2(thirdToPrev, Math.Max(0, -cur.Value.Value + prev.Value.Value));
                }
                else if (prev.Value.Value < next.Value.Value && (cur.Value.Value + Math.Tan(angle) * thirdToPrev) < prev.Value.Value)
                {
                    angle = Math.PI + Math.PI / 2 - Math.Atan2(thirdToPrev, Math.Min(0, -cur.Value.Value + prev.Value.Value));
                }
                return angle;

            case VDefinition.EditMode.Cubic:
                return Math.PI / 2 - Math.Atan2(next.Key - prev.Key, next.Value.Value - prev.Value.Value);

            case VDefinition.EditMode.Linear:
                return Math.PI / 2 - Math.Atan2(cur.Key - prev.Key, cur.Value.Value - prev.Value.Value);

            case VDefinition.EditMode.Horizontal:
            default:
                return 0;
        }
    }

    private static double CalcOutTangent(KeyValuePair<double, VDefinition> prev, KeyValuePair<double, VDefinition> cur, KeyValuePair<double, VDefinition> next)
    {
        switch (cur.Value.OutEditMode)
        {
            case VDefinition.EditMode.Tangent:
                return cur.Value.OutTangentAngle;
            case VDefinition.EditMode.Smooth:
                double thirdToNext = (next.Key - cur.Key) / TANGENT_CLAMP_RATIO;
                double thirdToPrev = (prev.Key - cur.Key) / TANGENT_CLAMP_RATIO;

                var angle = Math.PI / 2 - Math.Atan2(prev.Key - next.Key, prev.Value.Value - next.Value.Value);

                //// Avoid Overshoot to next keyframe
                if (prev.Value.Value > next.Value.Value && (cur.Value.Value + Math.Tan(angle) * thirdToNext) < next.Value.Value)
                {
                    angle = Math.PI / 2 - Math.Atan2(-thirdToNext, Math.Max(0, cur.Value.Value - next.Value.Value));
                }
                else if (prev.Value.Value < next.Value.Value && (cur.Value.Value + Math.Tan(angle) * thirdToNext) > next.Value.Value)
                {
                    angle = Math.PI / 2 - Math.Atan2(-thirdToNext, Math.Min(0, cur.Value.Value - next.Value.Value));
                }
                //// Avoid Overshooting to prev keyframe                    
                else if (prev.Value.Value > next.Value.Value && (cur.Value.Value + Math.Tan(angle) * thirdToPrev) > prev.Value.Value)
                {
                    angle = Math.PI / 2 - Math.Atan2(thirdToPrev, Math.Max(0, -cur.Value.Value + prev.Value.Value));
                }
                else if (prev.Value.Value < next.Value.Value && (cur.Value.Value + Math.Tan(angle) * thirdToPrev) < prev.Value.Value)
                {
                    angle = Math.PI / 2 - Math.Atan2(thirdToPrev, Math.Min(0, -cur.Value.Value + prev.Value.Value));
                }
                return angle;

            case VDefinition.EditMode.Cubic:
                return Math.PI / 2 - Math.Atan2(prev.Key - next.Key, prev.Value.Value - next.Value.Value);

            case VDefinition.EditMode.Linear:
                return Math.PI / 2 - Math.Atan2(cur.Key - next.Key, cur.Value.Value - next.Value.Value);

            case VDefinition.EditMode.Horizontal:
            default:
                return Math.PI;
        }
    }

};