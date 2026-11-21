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

namespace Lib.volume.io;

/// Loads grids from an OpenVDB .vdb file or a numbered sequence of them (reads uncompressed, zip and blosc-compressed data as written by Houdini, Blender, EmberGen or TiXL). Float grids become the volume's primary data; vector grids (velocity, color) ride along in the same set. Reloads when the file changes.
[Guid("70b83837-48fa-53ad-8b68-504bceff2848")]
internal sealed class LoadVdb : Instance<LoadVdb>
{
    private readonly record struct Key(string Path, string GridName, long WriteTimeTicks, float Scale, bool ZUp, Vector3 Translation);

    [Output(Guid = "f25cded9-8ca1-5431-9399-8d1285e00703")]
    public readonly Slot<VdbVolume> Volume = new();


    public LoadVdb()
    {
        Volume.UpdateAction = Update;
        _job = new BackgroundJob<Key, VdbVolume>(this, nameof(LoadVdb), Load, Volume);
    }

    private void Update(EvaluationContext context)
    {
        var address = Path.GetValue(context)?.Trim();
        var gridName = GridName.GetValue(context)?.Trim() ?? string.Empty;
        var frame = Frame.GetValue(context);
        var scale = Scale.GetValue(context);
        var zUp = ZUp.GetValue(context);
        var translation = Translation.GetValue(context);
        if (!(scale > 0) || !float.IsFinite(scale) || !float.IsFinite(translation.X + translation.Y + translation.Z))
        {
            _job.Reset();
            Volume.Value = null;
            return;
        }

        var path = ResolvePath(VdbSequence.Expand(address, frame));
        if (path == null)
        {
            _job.Reset();
            Volume.Value = null;
            return;
        }

        // Parsing a large file takes seconds; it runs on a worker. The write time makes a file saved again
        // reload, and a sequence played faster than its frames load shows the newest one that finished.
        var key = new Key(path, gridName, File.GetLastWriteTimeUtc(path).Ticks, scale, zUp, translation);
        switch (_job.Poll(context, key, out var volume))
        {
            case BackgroundJobOutcome.Ready:
                Volume.Value = volume;
                break;
            case BackgroundJobOutcome.Failed:
                Volume.Value = null;
                break;
        }
    }

    /// <summary>A disk path or a package address ("Examples:volumes/bunny.vdb") of an existing file, or null.</summary>
    private string ResolvePath(string address)
    {
        if (string.IsNullOrEmpty(address))
            return null;

        if (File.Exists(address))
            return address;

        // Windows drive paths are plain disk paths, never package addresses.
        if (address.Length >= 2 && char.IsLetter(address[0]) && address[1] == ':')
            return null;

        return FileResource.TryGetFileResource(address, this, out var fileResource) && File.Exists(fileResource.AbsolutePath)
                   ? fileResource.AbsolutePath
                   : null;
    }

    private static VdbVolume Load(Key key)
    {
        var parsed = VdbFile.Load(key.Path);
        var volume = parsed;
        if (key.GridName.Length > 0)
        {
            volume = new VdbVolume();
            var match = parsed.GetGrid(key.GridName);
            if (match != null)
                volume.Grids.Add(match);
        }

        if (volume.Grids.Count > 0)
            return Place(volume, key);

        // Say what was in the file rather than just that nothing usable came out: a Houdini point cache is a
        // perfectly valid file that this reader cannot represent.
        var skipped = parsed.SkippedGrids.Count > 0
                          ? $" (skipped unsupported grids: {string.Join(", ", parsed.SkippedGrids)})"
                          : string.Empty;
        var wanted = key.GridName.Length > 0 ? $" named '{key.GridName}'" : string.Empty;
        throw new InvalidDataException($"no supported grid{wanted} in {key.Path}{skipped}");
    }

    /** The grids moved into the scene; vector grids turn with the float grids they belong to. */
    private static VdbVolume Place(VdbVolume volume, Key key)
    {
        if (key.Scale == 1f && !key.ZUp && key.Translation == Vector3.Zero)
            return volume;

        var placed = new VdbVolume();
        placed.SkippedGrids.AddRange(volume.SkippedGrids);
        foreach (var grid in volume.Grids)
        {
            placed.Grids.Add(grid switch
                                 {
                                     VdbGrid<float> scalar => VolumePlacement.Apply(scalar, key.Scale, key.ZUp, key.Translation),
                                     VdbGrid<Vector3> vector => VolumePlacement.Apply(vector, key.Scale, key.ZUp, key.Translation),
                                     _ => throw new NotSupportedException($"grid '{grid.Name}' of type {grid.TypeName} cannot be placed"),
                                 });
        }

        return placed;
    }

    private readonly BackgroundJob<Key, VdbVolume> _job;

    [Input(Guid = "e16c0dad-a228-5a6f-964b-82986746836e")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "1e3dcc23-489a-5a54-b1af-c04d85ac3b8a")]
    public readonly InputSlot<string> GridName = new();

    [Input(Guid = "c5a3b6f4-6e0d-4b8a-9a63-2f1d7e4c9b05")]
    public readonly InputSlot<int> Frame = new();

    [Input(Guid = "a53afcdd-64d2-474b-8a3d-e437f9681de4")]
    public readonly InputSlot<float> Scale = new(1f);

    [Input(Guid = "f32c0f35-8925-491c-ad36-757c30c37142")]
    public readonly InputSlot<bool> ZUp = new();

    [Input(Guid = "6b2d3103-e07f-4364-ae0f-572e22ff767a")]
    public readonly InputSlot<Vector3> Translation = new();

}