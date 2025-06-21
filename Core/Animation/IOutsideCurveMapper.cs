using System.Collections.Generic;

namespace T3.Core.Animation;

internal interface IOutsideCurveMapper
{
    void Calc(double u, SortedList<double, VDefinition> curveElements, out double newU, out double offset);
}