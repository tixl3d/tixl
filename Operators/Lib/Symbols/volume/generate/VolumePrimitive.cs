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

/// Generates an analytic primitive as a narrow-band level set volume (OpenVDB's LevelSetSphere and friends). Primitive volumes are exact SDFs, ready for [VolumeCsg], [VolumeNoise] and [VolumeToMesh].
[Guid("6583bddb-034b-5ff0-b12e-6e5c38bdba64")]
internal sealed class VolumePrimitive : Instance<VolumePrimitive>
{
    private readonly record struct Key(VolumePrimitives.Shape Shape, Vector3 Size, Vector3 Center, float VoxelSize, int Band);

    [Output(Guid = "1c2a036f-72e1-5cb0-a787-f8a5e06defe7")]
    public readonly Slot<VdbVolume> Volume = new();

    private enum Shapes
    {
        Sphere,
        Box,
        RoundedBox,
        Torus,
        Capsule,
        Cylinder,
        Cone,
        Octahedron,
        Plane,
    }

    public VolumePrimitive()
    {
        Volume.UpdateAction = Update;
        _job = new BackgroundJob<Key, VdbVolume>(this, nameof(VolumePrimitive), Compute, Volume);
    }

    private void Update(EvaluationContext context)
    {
        var shape = (VolumePrimitives.Shape)Shape.GetValue(context);
        var size = Size.GetValue(context);
        var center = Center.GetValue(context);
        var voxelSize = VoxelSize.GetValue(context);
        var band = (int)BandWidth.GetValue(context);

        // Negated comparisons so NaN inputs are rejected too.
        if (!(size.X > 0) || !(size.Y > 0) || !(size.Z > 0) || !(voxelSize > 0)
            || !float.IsFinite(size.X + size.Y + size.Z + voxelSize) || !float.IsFinite(center.X + center.Y + center.Z))
        {
            _job.Reset();
            Volume.Value = null;
            return;
        }

        // The core rejects primitives too large for the voxel size; that error is reported once per input change.
        var key = new Key(shape, size, center, voxelSize, band);
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
        => new(VolumePrimitives.Create(key.Shape, key.Size, key.VoxelSize, key.Center, key.Band));

    private readonly BackgroundJob<Key, VdbVolume> _job;

    [Input(Guid = "d2cf0b36-bd51-5f39-9ae4-ea24893f1222", MappedType = typeof(Shapes))]
    public readonly InputSlot<int> Shape = new();

    [Input(Guid = "1465ff4d-d5af-5bf8-8c87-10c6ab682967")]
    public readonly InputSlot<Vector3> Size = new();

    [Input(Guid = "bc2ac4ee-9a07-5229-87e0-9b527ee316c6")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "89ac8fe1-dfda-5b97-8d05-1c5378f896b4")]
    public readonly InputSlot<float> VoxelSize = new();

    [Input(Guid = "91618a1f-bc81-581a-99eb-77714007172d")]
    public readonly InputSlot<float> BandWidth = new();

}