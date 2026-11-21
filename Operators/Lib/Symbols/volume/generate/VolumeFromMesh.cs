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

/// Converts a mesh into an OpenVDB-style sparse volume: either a signed distance field (SDF, negative inside the surface) or a fog volume (density 0..1 falling off from the surface). Distances are exact within a narrow band around the surface (BandWidth voxels).
/// 
/// Convert back with [VolumeToMesh], raymarch via [VolumeToTexture3D] + [VolumeField], or export with [SaveVdb].
[Guid("1cd14db1-d084-5d7b-935e-0c8cac5b9bd6")]
internal sealed class VolumeFromMesh : Instance<VolumeFromMesh>
{
    /// <summary>Identifies the inputs by mesh identity and version; the copied arrays are payload, not identity.</summary>
    private readonly record struct Key(MeshGeometry Mesh, int MeshVersion, float VoxelSize, int BandWidth, VolumeModes Mode,
                                       Vector3[] Positions, int[] FaceCornerOffsets, int[] CornerPointIndices)
    {
        public bool Equals(Key other)
            => ReferenceEquals(Mesh, other.Mesh) && MeshVersion == other.MeshVersion && VoxelSize.Equals(other.VoxelSize)
               && BandWidth == other.BandWidth && Mode == other.Mode;

        public override int GetHashCode() => HashCode.Combine(Mesh, MeshVersion, VoxelSize, BandWidth, Mode);
    }

    [Output(Guid = "09b1560a-62c1-5824-965e-c99ca55057de")]
    public readonly Slot<VdbVolume> Volume = new();

    private enum VolumeModes
    {
        Sdf,
        Fog,
    }

    public VolumeFromMesh()
    {
        Volume.UpdateAction = Update;
        _job = new BackgroundJob<Key, VdbVolume>(this, nameof(VolumeFromMesh), Compute, Snapshot, Volume);
    }

    private void Update(EvaluationContext context)
    {
        var mesh = Geometry.GetValue(context);
        var voxelSize = VoxelSize.GetValue(context);
        var bandWidth = (int)BandWidth.GetValue(context);
        var mode = (VolumeModes)Mode.GetValue(context);

        if (mesh == null || mesh.PointCount == 0 || mesh.FaceCount == 0 || !(voxelSize > 0) || !float.IsFinite(voxelSize))
        {
            _job.Reset();
            Volume.Value = null;
            return;
        }

        // Rebuild only when the mesh, its topology version or parameters changed. Rasterizing runs on a
        // worker; upstream operators edit their mesh in place, so the worker reads a copy taken when it starts.
        var key = new Key(mesh, mesh.Version, voxelSize, bandWidth, mode, null, null, null);
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

    private static Key Snapshot(Key key)
        => key with
               {
                   Positions = (Vector3[])key.Mesh.Positions.Clone(),
                   FaceCornerOffsets = (int[])key.Mesh.FaceCornerOffsets.Clone(),
                   CornerPointIndices = (int[])key.Mesh.CornerPointIndices.Clone(),
               };

    private static VdbVolume Compute(Key key)
    {
        var grid = MeshToVolume.ConvertGeometry(key.Positions, key.FaceCornerOffsets, key.CornerPointIndices,
                                                key.VoxelSize, key.BandWidth,
                                                key.Mode == VolumeModes.Sdf ? VdbVolumeType.Sdf : VdbVolumeType.Fog);
        return new VdbVolume(grid);
    }

    private readonly BackgroundJob<Key, VdbVolume> _job;

    [Input(Guid = "e4c8434c-5d4a-57b7-996e-5006785e8cf8")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "5dc83de1-24e8-52c8-9ef4-2ad2aafab51f")]
    public readonly InputSlot<float> VoxelSize = new();

    [Input(Guid = "780d9a0e-ef3e-5db7-a842-3aeeef647cad")]
    public readonly InputSlot<float> BandWidth = new();

    [Input(Guid = "674cd211-f65b-5e98-bca5-0a78c68e770a", MappedType = typeof(VolumeModes))]
    public readonly InputSlot<int> Mode = new();

}