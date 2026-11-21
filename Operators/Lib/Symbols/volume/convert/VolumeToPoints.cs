using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;
using Lib.Utils;
using T3.Core.Resource;

namespace Lib.volume.convert;

/// Converts volume voxels into a point list: either the active voxels above a threshold (highest values first), or a uniform random subset of them. Points feed the point/particle operators ([PointsToCurve], point renderers) or [VolumeFromPoints] again after editing.
[Guid("9741d4b6-e9cb-5ea4-ab4c-fdee86312243")]
internal sealed class VolumeToPoints : Instance<VolumeToPoints>
{
    private readonly record struct Key(VdbVolume Volume, Modes Mode, float Isovalue, int MaxPoints, int Seed);

    [Output(Guid = "f0b7798d-04d4-5339-b4ae-9ac1701cb114")]
    public readonly Slot<StructuredList> Points = new();

    /// <summary>The same points uploaded to a GPU structured buffer for point renderers like [DrawBillboards].</summary>
    [Output(Guid = "82b8dcdf-9ae0-5c62-8cde-c14eae985660")]
    public readonly Slot<BufferWithViews> GPoints = new();

    private enum Modes
    {
        ActiveVoxels,
        Scatter,
    }

    public VolumeToPoints()
    {
        Points.UpdateAction = Update;
        GPoints.UpdateAction = Update;   // consumers may only pull the GPU buffer
        _job = new BackgroundJob<Key, Point[]>(this, nameof(VolumeToPoints), Compute, Points, GPoints);
    }

    private void Update(EvaluationContext context)
    {
        var volume = Volume.GetValue(context);
        var mode = (Modes)Mode.GetValue(context);
        var isovalue = Isovalue.GetValue(context);
        var maxPoints = Math.Clamp((int)MaxPoints.GetValue(context), 0, 2_000_000);
        var seed = Seed.GetValue(context);

        if (volume?.GetFloatGrid() == null || float.IsNaN(isovalue))
        {
            _job.Reset();
            _hasPoints = false;
            Points.Value = null;
            GPoints.Value = null;
            return;
        }

        // Walking every active voxel runs on a worker; the GPU upload of a finished list stays on this thread.
        var key = new Key(volume, mode, isovalue, maxPoints, seed);
        switch (_job.Poll(context, key, out var points))
        {
            case BackgroundJobOutcome.Ready:
                Publish(points);
                break;
            case BackgroundJobOutcome.Failed:
                _hasPoints = false;
                break;
        }

        Points.Value = _hasPoints ? _pointList : null;
        GPoints.Value = _hasPoints ? _pointsBuffer : null;
    }

    private void Publish(Point[] points)
    {
        _hasPoints = points is { Length: > 0 };
        if (!_hasPoints)
            return;

        if (_pointList == null)
            _pointList = new StructuredList<Point>(points.Length);
        if (_pointList.NumElements != points.Length)
            _pointList.SetLength(points.Length);

        Array.Copy(points, _pointList.TypedElements, points.Length);

        try
        {
            ResourceManager.SetupBufferWithViews(_pointList.TypedElements, ref _pointsBuffer);
        }
        catch (Exception e)
        {
            // A failed upload (device lost, out of video memory) must not escape the update.
            Log.Warning($"VolumeToPoints upload failed: {e.Message}", this);
            _hasPoints = false;
        }
    }

    /// <summary>
    /// Scatter keeps a uniform random subset (reservoir sampling) so the whole volume is covered;
    /// ActiveVoxels keeps the highest values in a min-heap. Both stay bounded by MaxPoints.
    /// </summary>
    private static Point[] Compute(Key key)
    {
        var grid = key.Volume.GetFloatGrid();
        var maxPoints = key.MaxPoints;
        var isovalue = key.Isovalue;
        var candidates = new List<(Vector3 World, float Value)>();
        var highest = key.Mode == Modes.Scatter ? null : new PriorityQueue<(Vector3 World, float Value), float>();
        var random = new Random(key.Seed);
        var seen = 0L;
        foreach (var leaf in grid.Tree.Leaves)
        {
            foreach (var offset in leaf.ValueMask.OnBits())
            {
                var value = leaf.Values[offset];
                if (!(value > isovalue))
                    continue;

                var candidate = (grid.Transform.IndexToWorld(leaf.Origin + VdbCoord.LocalFromOffset(offset)), value);
                if (highest != null)
                {
                    if (highest.Count < maxPoints)
                    {
                        highest.Enqueue(candidate, value);
                    }
                    else if (highest.TryPeek(out _, out var lowestKept) && value > lowestKept)
                    {
                        highest.EnqueueDequeue(candidate, value);
                    }

                    continue;
                }

                seen++;
                if (candidates.Count < maxPoints)
                {
                    candidates.Add(candidate);
                }
                else
                {
                    var slot = random.NextInt64(seen);
                    if (slot < maxPoints)
                        candidates[(int)slot] = candidate;
                }
            }
        }

        if (highest != null)
        {
            // The heap pops the lowest value first; the list holds the highest values first.
            while (highest.TryDequeue(out var kept, out _))
                candidates.Add(kept);

            candidates.Reverse();
        }

        var points = new Point[candidates.Count];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = new Point
                            {
                                Position = candidates[i].World,
                                F1 = candidates[i].Value,
                                F2 = 0,
                                Scale = Vector3.One,
                                Orientation = Quaternion.Identity,
                                Color = Vector4.One,
                            };
        }

        return points;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _job.Reset();
            _pointsBuffer?.Dispose();
            _pointsBuffer = null;
        }

        base.Dispose(disposing);
    }

    private readonly BackgroundJob<Key, Point[]> _job;
    private bool _hasPoints;
    private StructuredList<Point> _pointList;
    private BufferWithViews _pointsBuffer;

    [Input(Guid = "a82acc1d-bacb-5839-90b5-53b2ae36ca7d")]
    public readonly InputSlot<VdbVolume> Volume = new();

    [Input(Guid = "0369a449-b250-53bc-89c5-85e1c2288b80", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "3c3f2652-7c22-5b1c-9984-d9182f3d2652")]
    public readonly InputSlot<float> Isovalue = new();

    [Input(Guid = "bd7e2b17-0c96-52d3-a674-68d58855057a")]
    public readonly InputSlot<int> MaxPoints = new();

    [Input(Guid = "3e761d43-45d2-5c07-af9d-50b71cc341c1")]
    public readonly InputSlot<int> Seed = new();

}