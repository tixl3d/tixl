using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using T3.Core.DataTypes;

namespace T3.Core.Animation;

/// <summary>
/// Caches a minimal polyline of (time, value) sample points for a curve.
/// Point density adapts per segment: linear/constant segments use 2 points,
/// non-linear segments use zoom-dependent density.
/// </summary>
public sealed class CurveSampleCache
{
    /// <summary>
    /// Target pixel spacing between sample points for non-linear segments.
    /// </summary>
    private const float TargetPixelSpacing = 5f;

    /// <summary>
    /// Margin factor: cache covers this multiple of the requested range
    /// to reduce rebuilds during small pans.
    /// </summary>
    private const double RangeMarginFactor = 1.5;

    /// <summary>
    /// Minimum zoom scale ratio change to trigger a rebuild.
    /// Avoids rebuilding on tiny zoom changes.
    /// </summary>
    private const double ZoomChangeThreshold = 1.3;

    /// <summary>
    /// Updates the cache if needed and returns the full cached point list.
    /// Callers should use <see cref="GetPointsInRange"/> for visible-range slicing.
    /// </summary>
    public void Update(Curve curve, double visibleStartU, double visibleEndU, double screenScaleX)
    {
        var revision = curve.ChangeCount;

        if (IsValid(curve, revision, visibleStartU, visibleEndU, screenScaleX))
            return;

        Rebuild(curve, visibleStartU, visibleEndU, screenScaleX, revision);
    }

    /// <summary>
    /// Returns a span of cached points that fall within the given time range.
    /// Points are sorted by time (X component).
    /// </summary>
    public ReadOnlySpan<Vector2> GetPointsInRange(double startU, double endU)
    {
        if (_points.Count == 0)
            return ReadOnlySpan<Vector2>.Empty;

        var startFloat = (float)startU;
        var endFloat = (float)endU;

        // Binary search for first point >= startU
        int lo = 0, hi = _points.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_points[mid].X < startFloat)
                lo = mid + 1;
            else
                hi = mid;
        }

        var first = lo;

        // Find first point > endU
        hi = _points.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_points[mid].X <= endFloat)
                lo = mid + 1;
            else
                hi = mid;
        }

        var last = lo;

        if (last <= first)
            return ReadOnlySpan<Vector2>.Empty;

        var span = CollectionsMarshal.AsSpan(_points);
        return span.Slice(first, last - first);
    }

    /// <summary>
    /// Number of cached sample points.
    /// </summary>
    public int PointCount => _points.Count;

    /// <summary>Time of the first keyframe. Points before this are the pre-region.</summary>
    public double FirstKeyU { get; private set; } = double.NaN;

    /// <summary>Time of the last keyframe. Points after this are the post-region.</summary>
    public double LastKeyU { get; private set; } = double.NaN;

    private bool IsValid(Curve curve, int revision, double visibleStartU, double visibleEndU, double screenScaleX)
    {
        if (curve != _cachedCurve || revision != _revision)
            return false;

        if (_points.Count == 0)
            return false;

        // Check if visible range is covered
        if (visibleStartU < _cachedStartU || visibleEndU > _cachedEndU)
            return false;

        // Check if zoom changed significantly
        if (_screenScaleX <= 0)
            return false;

        var zoomRatio = screenScaleX / _screenScaleX;
        if (zoomRatio > ZoomChangeThreshold || zoomRatio < 1.0 / ZoomChangeThreshold)
            return false;

        return true;
    }

    private void Rebuild(Curve curve, double visibleStartU, double visibleEndU, double screenScaleX, int revision)
    {
        _points.Clear();
        _cachedCurve = curve;
        _revision = revision;
        _screenScaleX = screenScaleX;

        var visibleWidth = visibleEndU - visibleStartU;
        var margin = visibleWidth * (RangeMarginFactor - 1.0) / 2.0;
        _cachedStartU = visibleStartU - margin;
        _cachedEndU = visibleEndU + margin;
        _visibleStartU = visibleStartU;
        _visibleEndU = visibleEndU;

        var table = curve.Table;
        if (table.Count == 0)
        {
            FirstKeyU = double.NaN;
            LastKeyU = double.NaN;
            return;
        }

        var keys = table.Keys;
        var values = table.Values;
        var firstKeyU = keys[0];
        var lastKeyU = keys[table.Count - 1];
        FirstKeyU = firstKeyU;
        LastKeyU = lastKeyU;

        // Pre-region: before first key
        if (_cachedStartU < firstKeyU)
        {
            SamplePreRegion(curve, _cachedStartU, firstKeyU, screenScaleX);
        }

        // Body: segments between adjacent keys
        for (var i = 0; i < table.Count - 1; i++)
        {
            var aU = keys[i];
            var aDef = values[i];
            var bU = keys[i + 1];
            var bDef = values[i + 1];

            // Skip segments entirely outside cached range
            if (bU < _cachedStartU || aU > _cachedEndU)
                continue;

            SampleSegment(curve, aU, aDef, bU, bDef, screenScaleX);
        }

        // Add last keyframe point if in range
        if (table.Count > 0 && lastKeyU >= _cachedStartU && lastKeyU <= _cachedEndU)
        {
            var lastDef = values[table.Count - 1];
            AddPointIfNew(lastKeyU, lastDef.Value);
        }

        // Post-region: after last key
        if (_cachedEndU > lastKeyU)
        {
            SamplePostRegion(curve, lastKeyU, _cachedEndU, screenScaleX);
        }
    }

    private void SampleSegment(Curve curve, double aU, VDefinition aDef, double bU, VDefinition bDef, double screenScaleX)
    {
        // Constant: hold value with vertical step at boundary
        if (aDef.OutInterpolation == VDefinition.KeyInterpolation.Constant)
        {
            AddPointIfNew(aU, aDef.Value);
            // Two points at bU: held value then new value, forming the vertical step
            AddPoint(bU, aDef.Value);
            AddPoint(bU, bDef.Value);
            return;
        }

        // Linear (both sides): 2 endpoints only
        if (aDef.OutInterpolation == VDefinition.KeyInterpolation.Linear
            && bDef.InInterpolation == VDefinition.KeyInterpolation.Linear)
        {
            AddPointIfNew(aU, aDef.Value);
            // bU endpoint will be added as start of next segment or as last key
            return;
        }

        // Non-linear: adaptive density based on screen-space pixel spacing
        var segmentScreenWidth = (bU - aU) * screenScaleX;
        var stepCount = Math.Max(2, (int)(segmentScreenWidth / TargetPixelSpacing));
        var stepU = (bU - aU) / stepCount;

        for (var s = 0; s < stepCount; s++)
        {
            var u = aU + s * stepU;
            var value = curve.GetSampledValue(u);
            AddPointIfNew(u, value);
        }
        // bU endpoint will be added as start of next segment or as last key
    }

    private void SamplePreRegion(Curve curve, double startU, double firstKeyU, double screenScaleX)
    {
        // Ensure the pre-region covers from the cache start to at least the visible start
        startU = Math.Min(startU, _visibleStartU);
        // Clamp the end of pre-region sampling to where it's actually needed for display
        var sampleEndU = Math.Min(firstKeyU, _visibleEndU + (_visibleEndU - _visibleStartU) * 0.25);

        if (curve.PreCurveMapping == CurveUtils.OutsideCurveBehavior.Constant)
        {
            var value = curve.GetSampledValue(startU);
            AddPoint(startU, value);
            if (sampleEndU > startU + 1e-6)
                AddPoint(sampleEndU, value);
            return;
        }

        // Non-constant pre-mapping (Cycle, Oscillate, etc.) — sample at zoom-appropriate density
        var regionScreenWidth = (sampleEndU - startU) * screenScaleX;
        var stepCount = Math.Max(2, (int)(regionScreenWidth / TargetPixelSpacing));
        var stepU = (sampleEndU - startU) / stepCount;

        for (var s = 0; s <= stepCount; s++)
        {
            var u = startU + s * stepU;
            var value = curve.GetSampledValue(u);
            AddPointIfNew(u, value);
        }
    }

    private void SamplePostRegion(Curve curve, double lastKeyU, double endU, double screenScaleX)
    {
        // Ensure the post-region covers from the keys to at least the visible end
        endU = Math.Max(endU, _visibleEndU);
        // Clamp the start of post-region sampling to where it's needed
        var sampleStartU = Math.Max(lastKeyU, _visibleStartU - (_visibleEndU - _visibleStartU) * 0.25);

        if (curve.PostCurveMapping == CurveUtils.OutsideCurveBehavior.Constant)
        {
            var value = curve.GetSampledValue(endU);
            AddPointIfNew(sampleStartU, value);
            if (endU > sampleStartU + 1e-6)
                AddPoint(endU, value);
            return;
        }

        // Non-constant post-mapping — sample at zoom-appropriate density
        var regionScreenWidth = (endU - sampleStartU) * screenScaleX;
        var stepCount = Math.Max(2, (int)(regionScreenWidth / TargetPixelSpacing));
        var stepU = (endU - sampleStartU) / stepCount;

        for (var s = 0; s <= stepCount; s++)
        {
            var u = sampleStartU + s * stepU;
            var value = curve.GetSampledValue(u);
            AddPointIfNew(u, value);
        }
    }

    private void AddPoint(double u, double value)
    {
        _points.Add(new Vector2((float)u, (float)value));
    }

    private void AddPointIfNew(double u, double value)
    {
        if (_points.Count > 0)
        {
            var last = _points[_points.Count - 1];
            if (Math.Abs(last.X - (float)u) < 1e-6f)
                return;
        }

        _points.Add(new Vector2((float)u, (float)value));
    }

    private readonly List<Vector2> _points = new(256);
    private Curve _cachedCurve;
    private double _cachedStartU;
    private double _cachedEndU;
    private double _visibleStartU;
    private double _visibleEndU;
    private double _screenScaleX;
    private int _revision = -1;
}
