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

/// Writes the volume's grids to an OpenVDB .vdb file, readable by Houdini, Blender, Maya and TiXL. Saves once whenever Save switches from off to on.
[Guid("df34163c-3dca-5de3-aabf-225dc08162a9")]
internal sealed class SaveVdb : Instance<SaveVdb>
{
    [Output(Guid = "08124f7f-e719-5ce0-8f61-92e9cceb340c")]
    public readonly Slot<bool> Saved = new();


    public SaveVdb()
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
        var zip = ZipCompression.GetValue(context);

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

        if (volume == null || volume.Grids.Count == 0 || string.IsNullOrEmpty(path))
            return;

        if (_saveTask != null)
        {
            Log.Warning("SaveVdb: the previous save is still running; trigger again when it is done.", this);
            return;
        }

        // Volumes are immutable, so serializing one on a worker thread is safe and keeps a large
        // write from stalling the render thread.
        _saveTask = Task.Run(() =>
                                 {
                                     SaveResult result;
                                     try
                                     {
                                         var directory = System.IO.Path.GetDirectoryName(path);
                                         if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                                             Directory.CreateDirectory(directory);

                                         VdbFile.Save(volume, path, zip);
                                         result = new SaveResult { GridCount = volume.Grids.Count, Path = path };
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
            Log.Warning($"SaveVdb failed for {result.Path}: {result.Error}", this);
            Saved.Value = false;
            return true;
        }

        Log.Debug($"Saved {result.GridCount} grid(s) to {result.Path}", this);
        Saved.Value = true;
        return true;
    }

    private sealed class SaveResult
    {
        public int GridCount;
        public string Error;
        public string Path = string.Empty;
    }

    private bool _wasSave;
    private Task<SaveResult> _saveTask;

    [Input(Guid = "6db4a395-2245-58f4-9db8-3e6e5ae814e1")]
    public readonly InputSlot<VdbVolume> Volume = new();

    [Input(Guid = "21cb11b5-601d-5925-a11e-59a788f05fc0")]
    private readonly InputSlot<string> PathSaved = new();

    [Input(Guid = "0266ff85-52d7-59fb-a4e1-6b8c0557a3b9")]
    public readonly InputSlot<bool> Save = new();

    [Input(Guid = "48b7436c-1126-5b8f-af8b-d87880bc8077")]
    public readonly InputSlot<bool> ZipCompression = new();

}