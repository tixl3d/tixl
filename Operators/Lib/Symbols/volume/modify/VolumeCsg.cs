using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;
using Lib.Utils;

namespace Lib.volume.modify;

/// Constructive solid geometry on level-set volumes (OpenVDB's csgUnion/Intersection/Difference): Union merges, Intersection keeps the overlap, Difference carves B out of A. The result is a fresh level set; feed it to [VolumeToMesh] or keep combining.
[Guid("49449447-78bd-525f-96ad-83759321316d")]
internal sealed class VolumeCsg : Instance<VolumeCsg>
{
    private readonly record struct Key(VdbVolume A, VdbVolume B, T3.Core.DataTypes.Volume.Tools.VolumeCsg.Operation Operation);

    [Output(Guid = "e7711aec-252e-5e6c-bcea-a772dbd93e2c")]
    public readonly Slot<VdbVolume> Volume = new();

    private enum Operations
    {
        Union,
        Intersection,
        Difference,
    }

    public VolumeCsg()
    {
        Volume.UpdateAction = Update;
        _job = new BackgroundJob<Key, VdbVolume>(this, nameof(VolumeCsg), Compute, Volume);
    }

    private void Update(EvaluationContext context)
    {
        var a = VolumeA.GetValue(context);
        var b = VolumeB.GetValue(context);
        var operation = (T3.Core.DataTypes.Volume.Tools.VolumeCsg.Operation)Operation.GetValue(context);

        if (a?.GetFloatGrid() == null || b?.GetFloatGrid() == null)
        {
            _job.Reset();
            Volume.Value = null;
            return;
        }

        var key = new Key(a, b, operation);
        switch (_job.Poll(context, key, out var result))
        {
            case BackgroundJobOutcome.Ready:
                Volume.Value = result;
                break;
            case BackgroundJobOutcome.Failed:
                // A broken input volume degrades to an empty output.
                Volume.Value = null;
                break;
        }
    }

    private static VdbVolume Compute(Key key)
        => new(T3.Core.DataTypes.Volume.Tools.VolumeCsg.Combine(key.A.GetFloatGrid(), key.B.GetFloatGrid(), key.Operation));

    private readonly BackgroundJob<Key, VdbVolume> _job;

    [Input(Guid = "c8eda1a2-1379-572e-92d1-ea640a6b9e61")]
    public readonly InputSlot<VdbVolume> VolumeA = new();

    [Input(Guid = "91c95ddc-98c5-5e99-947e-a9f94cbaf1de")]
    public readonly InputSlot<VdbVolume> VolumeB = new();

    [Input(Guid = "e0616bd7-2a4a-5953-95a5-a11929871bd9", MappedType = typeof(Operations))]
    public readonly InputSlot<int> Operation = new();

}