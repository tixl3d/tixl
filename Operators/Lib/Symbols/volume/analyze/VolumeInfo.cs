using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;

namespace Lib.volume.analyze;

/// Reports volume statistics: active voxels, memory footprint, value range and NaN/Inf corruption counts (OpenVDB diagnostics). Use it to catch broken volumes early in a graph.
[Guid("0cf85d34-2a41-534c-af7c-bdc6d7e64ecb")]
internal sealed class VolumeInfo : Instance<VolumeInfo>
{
    [Output(Guid = "83364604-237b-5983-866b-d24c2b342aac")]
    public readonly Slot<float> ActiveVoxels = new();

    [Output(Guid = "de88bdf5-4ceb-5a38-99fc-cc3c9e6275fa")]
    public readonly Slot<float> MemoryMB = new();

    [Output(Guid = "5ef2ebae-279f-51ab-95f1-afd2f912d408")]
    public readonly Slot<bool> HasNaN = new();

    [Output(Guid = "78a262b1-42e9-5ead-86e5-182f28a31c5a")]
    public readonly Slot<float> Min = new();

    [Output(Guid = "4eff0f63-d9a5-50b9-a60f-f00a5a593cf2")]
    public readonly Slot<float> Max = new();

    [Output(Guid = "fb4a6683-b5db-5e6d-981e-248d8b715c0c")]
    public readonly Slot<float> Average = new();


    public VolumeInfo()
    {
        // Any output can be the one a consumer pulls, so each must trigger Update.
        ActiveVoxels.UpdateAction = Update;
        MemoryMB.UpdateAction = Update;
        HasNaN.UpdateAction = Update;
        Min.UpdateAction = Update;
        Max.UpdateAction = Update;
        Average.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var volume = Volume.GetValue(context);
        var logToConsole = LogToConsole.GetValue(context);

        var grid = volume?.GetFloatGrid();
        if (grid == null)
        {
            ActiveVoxels.Value = 0;
            MemoryMB.Value = 0;
            HasNaN.Value = false;
            Min.Value = 0;
            Max.Value = 0;
            Average.Value = 0;
            _lastVolume = null;
            return;
        }

        if (ReferenceEquals(volume, _lastVolume))
            return;

        var report = VolumeDiagnose.Check(grid);
        ActiveVoxels.Value = report.ActiveVoxels;
        MemoryMB.Value = (float)(report.MemoryBytes / (1024.0 * 1024.0));
        HasNaN.Value = report.HasErrors;
        Min.Value = report.MinValue;
        Max.Value = report.MaxValue;
        Average.Value = (float)report.Average;

        if (logToConsole)
        {
            Log.Debug($"Volume '{grid.Name}': {report.ActiveVoxels} active voxels, {report.LeafCount} leaves, "
                      + $"{report.MemoryBytes / (1024.0 * 1024.0):F2} MB, range [{report.MinValue:G3} .. {report.MaxValue:G3}], "
                      + (report.HasErrors ? $"INVALID ({report.NanCount} NaN, {report.InfCount} Inf)" : "valid"), this);
        }

        _lastVolume = volume;
    }

    private VdbVolume _lastVolume;

    [Input(Guid = "9de3a513-9065-5207-a4ae-3780bf007dd3")]
    public readonly InputSlot<VdbVolume> Volume = new();

    [Input(Guid = "dbfceb27-904a-55ff-b0ed-3a3707d795d6")]
    public readonly InputSlot<bool> LogToConsole = new();

}