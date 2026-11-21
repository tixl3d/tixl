using T3.Core.DataTypes.Volume.Tools;

namespace Lib.geometry;

/// <summary>
/// Meshes a signed-distance field from samples: a lattice of points that carry the field's distance in F1, which is
/// what [SampleFieldPoints] writes. It is the way from the GPU field graph to geometry, and with that to everything
/// that works on the CPU - a [RigidBody] collider included.
/// </summary>
[Guid("648ba407-e15a-4010-b6ba-a4a7a7e211f4")]
internal sealed class SdfPointsToGeometry : Instance<SdfPointsToGeometry>
{
    [Output(Guid = "85944599-8077-465d-afff-3b38ee7d9b34")]
    public readonly Slot<MeshGeometry> Result = new();

    private enum Sources
    {
        F1,
        F2,
    }

    public SdfPointsToGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var points = Points.GetValue(context) as StructuredList<Point>;
        var isovalue = Isovalue.GetValue(context);
        var voxelSize = VoxelSize.GetValue(context);
        var useF2 = ValueFrom.GetValue(context) == (int)Sources.F2;
        if (points == null || points.NumElements < 8)
        {
            Result.Value = null;
            _lastKey = default;
            return;
        }

        // The list is reused in place by its producer, so only its content says whether the samples changed.
        var hash = new HashCode();
        hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(points.TypedElements.AsSpan()));
        var key = (hash.ToHashCode(), points.NumElements, isovalue, voxelSize, useF2);
        if (key == _lastKey)
            return;

        _lastKey = key;
        try
        {
            var grid = SdfSamples.ToGrid(points.TypedElements, voxelSize, useF2);
            var geometry = grid == null ? null : T3.Core.DataTypes.Volume.VolumeToMesh.Extract(grid, float.IsFinite(isovalue) ? isovalue : 0f);
            Result.Value = geometry != null && geometry.FaceCount > 0 ? geometry : null;
            _lastError = null;
        }
        catch (Exception e)
        {
            // An exception escaping an update is fatal to the editor; degrade to an empty output.
            Result.Value = null;
            if (_lastError != e.Message)
                Log.Warning($"SdfPointsToGeometry failed: {e.Message}", this);
            _lastError = e.Message;
        }
    }

    private (int, int, float, float, bool) _lastKey;
    private string _lastError;

    [Input(Guid = "0531842f-2afc-4513-80aa-d2ba502d618d")]
    public readonly InputSlot<StructuredList> Points = new();

    [Input(Guid = "42b9049d-204e-477c-a5a3-0c11a6814ed1")]
    public readonly InputSlot<float> Isovalue = new();

    [Input(Guid = "76dbaf32-e7aa-42fe-95c5-59baa5f036f7")]
    public readonly InputSlot<float> VoxelSize = new();

    [Input(Guid = "9a121e94-3ce5-4804-b26a-ccc177c8cc90", MappedType = typeof(Sources))]
    public readonly InputSlot<int> ValueFrom = new();
}
