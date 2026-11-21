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

/// Changes a volume's voxel size, resampling values trilinearly (OpenVDB's resampleToMatch). Smaller voxels = finer detail and more memory; larger = cheaper.
[Guid("5c4dc3a7-fc82-59c1-afde-daf023bad7c9")]
internal sealed class VolumeResample : Instance<VolumeResample>
{
    private readonly record struct Key(VdbVolume Volume, float VoxelSize);

    [Output(Guid = "def20e90-909a-5405-b7ad-0e6a953727db")]
    public readonly Slot<VdbVolume> Result = new();

    public VolumeResample()
    {
        Result.UpdateAction = Update;
        _job = new BackgroundJob<Key, VdbVolume>(this, nameof(VolumeResample), Compute, Result);
    }

    private void Update(EvaluationContext context)
    {
        var volume = Volume.GetValue(context);
        var voxelSize = VoxelSize.GetValue(context);

        var grid = volume?.GetFloatGrid();
        if (grid == null || !(voxelSize > 0) || !float.IsFinite(voxelSize))
        {
            _job.Reset();
            Result.Value = volume;
            return;
        }

        // The core rejects results that would exhaust memory; the job reports that once instead of every frame.
        var key = new Key(volume, voxelSize);
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
        => new(T3.Core.DataTypes.Volume.Tools.VolumeDense.Resample(key.Volume.GetFloatGrid(), key.VoxelSize));

    private readonly BackgroundJob<Key, VdbVolume> _job;

    [Input(Guid = "3b90bbbd-0607-5c2b-8e65-61c8a7e04030")]
    public readonly InputSlot<VdbVolume> Volume = new();

    [Input(Guid = "d21b910d-a7d9-507f-b7f6-4053dbdee86a")]
    public readonly InputSlot<float> VoxelSize = new();

}