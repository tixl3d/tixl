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

/// Morphological dilate/erode on volumes. Level sets offset the surface outward (Dilate) or inward (Erode); Open and Close apply both to remove thin features or pinholes. Fog volumes use min/max stencil filtering instead.
[Guid("f3b93568-90a7-5119-a0a9-fcd9f1ee60e0")]
internal sealed class VolumeMorphology : Instance<VolumeMorphology>
{
    private readonly record struct Key(VdbVolume Volume, T3.Core.DataTypes.Volume.Tools.VolumeMorphology.Operation Operation, float Radius);

    [Output(Guid = "e385bebb-b9e7-576f-9c3a-f794d84d5d48")]
    public readonly Slot<VdbVolume> Result = new();

    private enum Operations
    {
        Dilate,
        Erode,
        Open,
        Close,
    }

    public VolumeMorphology()
    {
        Result.UpdateAction = Update;
        _job = new BackgroundJob<Key, VdbVolume>(this, nameof(VolumeMorphology), Compute, Result);
    }

    private void Update(EvaluationContext context)
    {
        var volume = Volume.GetValue(context);
        var operation = (T3.Core.DataTypes.Volume.Tools.VolumeMorphology.Operation)Operation.GetValue(context);
        var radius = Radius.GetValue(context);

        var grid = volume?.GetFloatGrid();
        // Written as a negated comparison so a NaN radius passes the volume through instead of computing.
        if (grid == null || !(radius > 0) || !float.IsFinite(radius))
        {
            _job.Reset();
            Result.Value = volume;
            return;
        }

        var key = new Key(volume, operation, radius);
        switch (_job.Poll(context, key, out var result))
        {
            case BackgroundJobOutcome.Ready:
                Result.Value = result;
                break;
            case BackgroundJobOutcome.Failed:
                Result.Value = null;
                break;
        }
    }

    private static VdbVolume Compute(Key key)
        => new(T3.Core.DataTypes.Volume.Tools.VolumeMorphology.Apply(key.Volume.GetFloatGrid(), key.Operation, key.Radius));

    private readonly BackgroundJob<Key, VdbVolume> _job;

    [Input(Guid = "f762b0d3-0f82-5660-9169-c94524c5a414")]
    public readonly InputSlot<VdbVolume> Volume = new();

    [Input(Guid = "2ad87fa8-ac60-5972-a42e-38a4a224c4f1", MappedType = typeof(Operations))]
    public readonly InputSlot<int> Operation = new();

    [Input(Guid = "afb22fff-6a74-5f28-893e-950eb71eba4d")]
    public readonly InputSlot<float> Radius = new();

}