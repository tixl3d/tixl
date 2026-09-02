using System;
using T3.Core.Utils;

namespace Lib.geometry;

[Guid("634da8e3-0772-4f5a-816b-35b492e50938")]
internal sealed class TransformGeometry : Instance<TransformGeometry>
{
    [Output(Guid = "895d2fd4-c40f-4200-89af-395253275b0a")]
    public readonly Slot<MeshGeometry> Result = new();

    public TransformGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        if (source == null)
        {
            Result.Value = null;
            return;
        }

        var translation = Translation.GetValue(context);
        var rotationDegrees = Rotation.GetValue(context);
        var scale = Scale.GetValue(context) * UniformScale.GetValue(context);

        var rotation = Quaternion.CreateFromYawPitchRoll(rotationDegrees.Y * MathUtils.ToRad,
                                                         rotationDegrees.X * MathUtils.ToRad,
                                                         rotationDegrees.Z * MathUtils.ToRad);

        // Topology and untouched attributes are shared by reference (geometry flowing
        // through the graph is immutable by convention); positions and normals get
        // their own transformed buffers.
        _output.FaceCornerOffsets = source.FaceCornerOffsets;
        _output.CornerPointIndices = source.CornerPointIndices;
        _output.Parts = source.Parts;

        if (_output.Positions.Length != source.Positions.Length)
            _output.Positions = new Vector3[source.Positions.Length];

        for (var i = 0; i < source.Positions.Length; i++)
        {
            _output.Positions[i] = Vector3.Transform(source.Positions[i] * scale, rotation) + translation;
        }

        _output.Attributes.Clear();
        foreach (var attribute in source.Attributes)
        {
            if (attribute is GeometryAttribute<Vector3> vectors
                && string.Equals(attribute.Name, GeometryAttributeNames.Normal, StringComparison.OrdinalIgnoreCase))
            {
                var transformed = _output.Attributes.GetOrCreate<Vector3>(attribute.Name, attribute.Domain, vectors.Values.Length);

                // Normals under non-uniform scale need the inverse scale before rotating
                var inverseScale = new Vector3(scale.X != 0 ? 1f / scale.X : 0,
                                               scale.Y != 0 ? 1f / scale.Y : 0,
                                               scale.Z != 0 ? 1f / scale.Z : 0);
                for (var i = 0; i < vectors.Values.Length; i++)
                {
                    var scaled = vectors.Values[i] * inverseScale;
                    var length = scaled.Length();
                    transformed.Values[i] = Vector3.Transform(length > 1e-10f ? scaled / length : vectors.Values[i], rotation);
                }
            }
            else
            {
                _outputSharedAttributes.Add(attribute);
            }
        }

        foreach (var shared in _outputSharedAttributes)
        {
            _output.Attributes.Add(shared);
        }

        _outputSharedAttributes.Clear();
        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    private readonly MeshGeometry _output = new();
    private readonly System.Collections.Generic.List<GeometryAttribute> _outputSharedAttributes = [];

    [Input(Guid = "90343fe1-bee7-4ec5-a29d-c44b68a3a37c")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "3497c025-efaf-4191-8621-ade40d377ee9")]
    public readonly InputSlot<Vector3> Translation = new();

    [Input(Guid = "b478cdd9-14c5-486b-be7b-4c70bd8f667c")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "80aa5810-3fbd-4d95-96d1-72647d8af302")]
    public readonly InputSlot<Vector3> Scale = new();

    [Input(Guid = "a3096995-909c-42f4-9519-99b25f1c330e")]
    public readonly InputSlot<float> UniformScale = new();
}
