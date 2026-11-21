using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;
using Lib.Utils;

namespace Lib.volume.generate;

/// Splats a point list into a sparse density volume (OpenVDB's particles-to-volume): every point contributes a normalized falloff kernel over its radius. Use it to volumize particles, or points scattered from geometry with [ScatterPointsInVolume].
[Guid("c698dc60-e135-5cc0-8346-31eb7ee77637")]
internal sealed class VolumeFromPoints : Instance<VolumeFromPoints>
{
    /// <summary>Identifies the points by count and position hash; the copied positions are payload, not identity.</summary>
    private readonly record struct Key(int Count, int PositionsHash, float VoxelSize, float Radius, float Density,
                                       PointsToVolume.Falloff Falloff, Vector3[] Positions)
    {
        public bool Equals(Key other)
            => Count == other.Count && PositionsHash == other.PositionsHash && VoxelSize.Equals(other.VoxelSize)
               && Radius.Equals(other.Radius) && Density.Equals(other.Density) && Falloff == other.Falloff;

        public override int GetHashCode() => HashCode.Combine(Count, PositionsHash, VoxelSize, Radius, Density, Falloff);
    }

    [Output(Guid = "d6f70e3d-84d5-57da-b94d-ffc284fca668")]
    public readonly Slot<VdbVolume> Volume = new();

    private enum Falloffs
    {
        Gaussian,
        Smoothstep,
        Constant,
    }

    public VolumeFromPoints()
    {
        Volume.UpdateAction = Update;
        _job = new BackgroundJob<Key, VdbVolume>(this, nameof(VolumeFromPoints), Compute, Volume);
    }

    private void Update(EvaluationContext context)
    {
        var points = Points.GetValue(context) as StructuredList<Point>;
        var voxelSize = VoxelSize.GetValue(context);
        var radius = Radius.GetValue(context);
        var density = Density.GetValue(context);
        var falloff = (PointsToVolume.Falloff)Falloff.GetValue(context);

        if (points == null || points.NumElements == 0
            || !(voxelSize > 0) || !float.IsFinite(voxelSize) || !float.IsFinite(radius) || !float.IsFinite(density))
        {
            _job.Reset();
            Volume.Value = null;
            return;
        }

        // The list is reused in place by upstream operators, so identity alone cannot tell if the points moved.
        var count = points.NumElements;
        var positionsHash = HashPositions(points);
        var key = new Key(count, positionsHash, voxelSize, radius, density, falloff, null);
        if (_job.NeedsStart(context, key))
        {
            // The worker reads a copy: upstream may rewrite the list while the splat runs.
            var positions = new Vector3[count];
            var elements = points.TypedElements;
            for (var i = 0; i < count; i++)
                positions[i] = elements[i].Position;
            key = key with { Positions = positions };
        }

        switch (_job.Poll(context, key, out var result))
        {
            case BackgroundJobOutcome.Ready:
                Volume.Value = result;
                break;
            case BackgroundJobOutcome.Failed:
                Volume.Value = null;
                break;
        }
    }

    private static VdbVolume Compute(Key key)
        => new(PointsToVolume.Splat(key.Positions, null, key.VoxelSize, key.Radius, key.Density, key.Falloff, seed: 0));

    private static int HashPositions(StructuredList<Point> points)
    {
        var hash = 17;
        var elements = points.TypedElements;
        for (var i = 0; i < points.NumElements; i++)
        {
            var position = elements[i].Position;
            hash = hash * 31 + BitConverter.SingleToInt32Bits(position.X);
            hash = hash * 31 + BitConverter.SingleToInt32Bits(position.Y);
            hash = hash * 31 + BitConverter.SingleToInt32Bits(position.Z);
        }

        return hash;
    }

    private readonly BackgroundJob<Key, VdbVolume> _job;

    [Input(Guid = "51935275-be34-555d-a9dc-7fbc6a55db39")]
    public readonly InputSlot<StructuredList> Points = new();

    [Input(Guid = "234a9c82-72f4-5388-9473-8bff628ba0fe")]
    public readonly InputSlot<float> VoxelSize = new();

    [Input(Guid = "8a3c8412-7266-50ce-83ba-7841bb431863")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "5f863764-57b0-5e98-b6ac-4c621fcb5380")]
    public readonly InputSlot<float> Density = new();

    [Input(Guid = "5fbe41e2-1376-5ead-b9aa-97c925092e26", MappedType = typeof(Falloffs))]
    public readonly InputSlot<int> Falloff = new();

}