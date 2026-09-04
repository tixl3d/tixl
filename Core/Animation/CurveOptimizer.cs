using System;
using System.Collections.Generic;
using System.Linq;
using T3.Core.DataTypes;

namespace T3.Core.Animation;

public class CurveOptimizer
{
    public CurveOptimizer(List<Curve> curves)
    {
        _curves = curves;

        InitializePositions();
        InitializeCurveSampleBuffers();
        ResampleCurve(0, _positionsWithImpact.Count - 1);
    }

    private void InitializePositions()
    {
        // Gather all positions from the curves in a dictionary...
        var uniquePositions = new HashSet<double>();

        foreach (var k in _curves.SelectMany(curve => curve.GetVDefinitions()))
        {
            uniquePositions.Add(k.U);
        }

        _positionsWithImpact = new List<PositionWithImpact>(); ;
        foreach (var position in uniquePositions.OrderBy(o => o))
        {
            _positionsWithImpact.Add(new PositionWithImpact() { Position = position, Impact = double.NaN });
        }
    }


    /**
     * Sampling the curve is expensive. So we sample the state before the next optimization
     * for faster comparision. These curves are stored for each curve in an array with the
     * necessary size for the samplerate between min and max postiion.
     *
     * After removing keys these sections of the curve have to resampled.
     */
    private void InitializeCurveSampleBuffers()
    {
        _sampleStartPosition = _positionsWithImpact.First().Position;
        _sampleCount = (int)(Math.Abs(_positionsWithImpact.Last().Position - _sampleStartPosition) / SAMPLE_RATE);
        if (_sampleCount < 2)
            return;

        _samplesForCurves = new double[_curves.Count, _sampleCount + 1];

    }

    public void ResampleCurve(int startIndex, int endIndex)
    {
        for (var curveIndex = 0; curveIndex < _curves.Count; ++curveIndex)
        {
            var curve = _curves[curveIndex];
            for (var sampleIndex = startIndex; sampleIndex < _sampleCount && sampleIndex <= endIndex; ++sampleIndex)
            {
                var samplePos = _sampleStartPosition + sampleIndex * SAMPLE_RATE;
                _samplesForCurves[curveIndex, sampleIndex] = curve.GetSampledValue(samplePos);
            }
        }
    }


    public void OptimizeCurves(int maxPositionCount)
    {
        _maxPositionCount = maxPositionCount;

        while (_positionsWithImpact.Count > _maxPositionCount)
        {
            UpdatePositionImpacts();

            _leastImpactedPositionIndex = FindIndexWithMinimumImpact();
            var minImpactPosition = _positionsWithImpact[_leastImpactedPositionIndex].Position;

            // Remove keys from curve
            foreach (var curve in _curves)
            {
                if (curve.HasVAt(minImpactPosition))
                    curve.RemoveKeyframeAt(minImpactPosition);
            }

            // Remove position from list and invalidate neighbours
            var startSampleIndex = 0;
            var endSampleIndex = 0;

            if (_leastImpactedPositionIndex > 0)
            {
                _positionsWithImpact[_leastImpactedPositionIndex - 1].Impact = double.NaN;
                startSampleIndex = _leastImpactedPositionIndex - 1;
            }
            if (_leastImpactedPositionIndex > 1)
            {
                startSampleIndex = _leastImpactedPositionIndex - 2;
            }

            if (_leastImpactedPositionIndex + 2 < _positionsWithImpact.Count)
            {
                _positionsWithImpact[_leastImpactedPositionIndex + 1].Impact = double.NaN;
                endSampleIndex = _leastImpactedPositionIndex + 1;
            }

            if (_leastImpactedPositionIndex + 3 < _positionsWithImpact.Count)
            {
                endSampleIndex = _leastImpactedPositionIndex + 2;
            }

            _positionsWithImpact.RemoveAt(_leastImpactedPositionIndex);

            // Resample section of curve...
            ResampleCurve(IndexFromU(_positionsWithImpact[startSampleIndex].Position),
                          IndexFromU(_positionsWithImpact[endSampleIndex].Position));
        }
    }


    class PositionWithImpact
    {
        public double Position;
        public double Impact;
    }


    private int FindIndexWithMinimumImpact()
    {
        var minImpact = double.PositiveInfinity;
        var minImpactIndex = 0;
        var index = -1;
        foreach (var p in _positionsWithImpact)
        {
            index++;
            if (!(p.Impact < minImpact))
                continue;

            minImpact = p.Impact;
            minImpactIndex = index;
        }
        return minImpactIndex;
    }


    /**
     * Go through the current list of positions and update if necessary.
     * Invalidated positions are marked with an impact of double.NaN.
     */
    public void UpdatePositionImpacts()
    {
        if (_positionsWithImpact.Count <= 2)
            return;

        double u, nextU, previousU;

        // Yes, these are 3 nested loops. Don't ask.
        for (var keyIndex = 1; keyIndex < _positionsWithImpact.Count - 1; ++keyIndex)
        {
            var p = _positionsWithImpact[keyIndex];
            u = p.Position;
            nextU = _positionsWithImpact[keyIndex + 1].Position;
            previousU = _positionsWithImpact[keyIndex - 1].Position;
            var accumulatedImpact = 0.0;


            if (!double.IsNaN(p.Impact))
                return;

            for (var curveIndex = 0; curveIndex < _curves.Count; ++curveIndex)
            {
                var curve = _curves[curveIndex];
                var vDef = curve.GetV(u);
                if (vDef == null)
                    continue;

                curve.RemoveKeyframeAt(u);

                var startSampleIndex = IndexFromU(previousU);
                var endSampleIndex = IndexFromU(nextU) + 1;

                for (var sampleIndex = startSampleIndex; sampleIndex <= endSampleIndex && sampleIndex < _sampleCount; ++sampleIndex)
                {
                    var sampleU = _sampleStartPosition + sampleIndex * SAMPLE_RATE;
                    var originalValue = _samplesForCurves[curveIndex, sampleIndex];
                    var newValue = curve.GetSampledValue(sampleU);
                    accumulatedImpact += Math.Abs(originalValue - newValue);
                }
                curve.AddOrUpdateV(u, vDef);
            }
            p.Impact = accumulatedImpact;
        }
    }

    private int IndexFromU(double u)
    {
        var samplePosition = (SAMPLE_RATE * (int)(u / SAMPLE_RATE));
        return (int)((samplePosition - _sampleStartPosition) / SAMPLE_RATE);
    }

    private double _sampleStartPosition;
    private int _sampleCount;
    private List<Curve> _curves;
    private int _maxPositionCount;

    private List<PositionWithImpact> _positionsWithImpact = new();
    const double SAMPLE_RATE = 1 / 60.0;
    private double[,] _samplesForCurves;

    private int _leastImpactedPositionIndex = 0;
}