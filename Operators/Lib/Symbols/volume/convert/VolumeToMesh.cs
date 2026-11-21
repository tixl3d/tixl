using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;
using Lib.Utils;

namespace Lib.volume.convert;

/// Extracts an isosurface from a volume as a triangle mesh (OpenVDB's volumeToMesh, marching cubes with shared vertices and gradient normals). Isovalue 0 extracts a level set's surface; for fog volumes pick your density cutoff.
/// 
/// Render with [DrawMesh] after [GeometryToMesh].
[Guid("5bc7d656-8a0f-5834-88da-14c3048d760c")]
internal sealed class VolumeToMesh : Instance<VolumeToMesh>
{
    private readonly record struct Key(VdbVolume Volume, float Isovalue);

    [Output(Guid = "362c33b2-88c6-537e-ad87-035c85afd29d")]
    public readonly Slot<MeshGeometry> Geometry = new();

    public VolumeToMesh()
    {
        Geometry.UpdateAction = Update;
        _job = new BackgroundJob<Key, MeshGeometry>(this, nameof(VolumeToMesh), Compute, Geometry);
    }

    private void Update(EvaluationContext context)
    {
        var volume = Volume.GetValue(context);
        var isovalue = Isovalue.GetValue(context);

        if (volume?.GetFloatGrid() == null || !float.IsFinite(isovalue))
        {
            _job.Reset();
            Geometry.Value = null;
            return;
        }

        // Marching cubes over a large band is the slowest step of most volume chains; it runs on a worker.
        var key = new Key(volume, isovalue);
        switch (_job.Poll(context, key, out var result))
        {
            case BackgroundJobOutcome.Ready:
                Geometry.Value = result;
                break;
            case BackgroundJobOutcome.Failed:
                Geometry.Value = null;
                break;
        }
    }

    private static MeshGeometry Compute(Key key)
        => T3.Core.DataTypes.Volume.VolumeToMesh.Extract(key.Volume.GetFloatGrid(), key.Isovalue);

    private readonly BackgroundJob<Key, MeshGeometry> _job;

    [Input(Guid = "19c6ca3f-d7ff-507f-8d28-2490f0edb66e")]
    public readonly InputSlot<VdbVolume> Volume = new();

    [Input(Guid = "345f946b-8c36-5548-a569-4123e0544b8f")]
    public readonly InputSlot<float> Isovalue = new();

}