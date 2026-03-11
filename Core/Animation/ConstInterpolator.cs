using System;
using System.Collections.Generic;

namespace T3.Core.Animation;

internal static class ConstInterpolator
{
    public static void UpdateTangents(List<KeyValuePair<double, VDefinition>> curveElements) { }

    internal static double Interpolate(KeyValuePair<double, VDefinition> a, KeyValuePair<double, VDefinition> b, double u)
    {
        return Math.Abs(u- b.Key) < 0.0001 ? b.Value.Value : a.Value.Value;
    }
};