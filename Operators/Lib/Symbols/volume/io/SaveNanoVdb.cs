using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;
using Lib.Utils;

namespace Lib.volume.io;

/// Writes the volume as a NanoVDB .nvdb file: the GPU-flattened grid format used by CUDA / renderers (and readable by the reference NanoVDB library).
[Guid("e90edc95-86bb-5508-a36c-fc77f5d14459")]
internal sealed class SaveNanoVdb : Instance<SaveNanoVdb>
{
    [Output(Guid = "4ce764eb-1a7a-5d04-96e9-4ddeba3804d9")]
    public readonly Slot<bool> Saved = new();


    public SaveNanoVdb()
    {
        Saved.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        UpdateCore(context);
        BackgroundPolling.Set(_saveTask != null, Saved);
    }

    private void UpdateCore(EvaluationContext context)
    {
        var volume = Volume.GetValue(context);
        var path = PathSaved.GetValue(context)?.Trim();
        var save = Save.GetValue(context);

        // Report a finished write first, so its one-frame Saved pulse is not cleared below.
        if (PublishCompletedSave())
            return;

        if (!save)
        {
            _wasSave = false;
            Saved.Value = false;
            return;
        }

        if (_wasSave)
        {
            Saved.Value = false;
            return;
        }

        _wasSave = true;
        Saved.Value = false;

        if (volume == null || string.IsNullOrEmpty(path))
            return;

        if (_saveTask != null)
        {
            Log.Warning("SaveNanoVdb: the previous save is still running; trigger again when it is done.", this);
            return;
        }

        // Flattening and writing can take a while for a dense grid; both only read the volume.
        _saveTask = Task.Run(() =>
                                 {
                                     SaveResult result;
                                     try
                                     {
                                         NanoVdbBuffer buffer = volume.GetFloatGrid() is { } floatGrid ? NanoVdbBuffer.FromGrid(floatGrid)
                                                                     : volume.GetVectorGrid() is { } vecGrid ? NanoVdbBuffer.FromGrid(vecGrid)
                                                                     : null;

                                         if (buffer == null)
                                         {
                                             result = new SaveResult { Error = "no float or vector grid to save", Path = path };
                                         }
                                         else
                                         {
                                             var directory = System.IO.Path.GetDirectoryName(path);
                                             if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                                                 Directory.CreateDirectory(directory);

                                             buffer.Save(path);
                                             var summary = buffer.ReadSummary();
                                             result = new SaveResult { VoxelCount = summary.VoxelCount, Path = path };
                                         }
                                     }
                                     catch (Exception e)
                                     {
                                         result = new SaveResult { Error = e.Message, Path = path };
                                     }

                                     // Bump the version atomically rather than calling Invalidate():
                                     // see LoadVdb for why the pass-scoped tick must stay on the
                                     // evaluation thread.
                                     Interlocked.Increment(ref Saved.DirtyFlag.SourceVersion);
                                     return result;
                                 });
    }

    private bool PublishCompletedSave()
    {
        if (_saveTask is not { IsCompleted: true })
            return false;

        var task = _saveTask;
        _saveTask = null;

        var result = task.Result;
        if (result.Error != null)
        {
            Log.Warning($"SaveNanoVdb failed for {result.Path}: {result.Error}", this);
            Saved.Value = false;
            return true;
        }

        Log.Debug($"Saved NanoVDB ({result.VoxelCount} active voxels) to {result.Path}", this);
        Saved.Value = true;
        return true;
    }

    private sealed class SaveResult
    {
        public long VoxelCount;
        public string Error;
        public string Path = string.Empty;
    }

    private bool _wasSave;
    private Task<SaveResult> _saveTask;

    [Input(Guid = "7ddbba10-4371-5f6e-ba1f-ce3f26c47d6e")]
    public readonly InputSlot<VdbVolume> Volume = new();

    [Input(Guid = "413cb751-966b-5e5e-b8ee-e6c10a0db501")]
    private readonly InputSlot<string> PathSaved = new();

    [Input(Guid = "c7946670-7bed-50c6-bd36-59b69be6e85e")]
    public readonly InputSlot<bool> Save = new();

}