#nullable enable
using System;
using System.Collections.Generic;
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.geometry.transform;

/// <summary>
/// Scales, rotates and translates a Geometry, either as a whole (Object space) or each point in its own orientation
/// (Point space: X along a curve, Z along its normal). Every attribute is transformed according to its type info:
/// positions like points, vectors such as curve handles by rotation and scale, normals by the inverse transpose,
/// quaternions by rotation. A float selection weights the transform per point (by default the flowing Selection, if
/// any); vertex, primitive and part selections apply to their points. Topology and value attributes are shared.
/// </summary>
[Guid("634da8e3-0772-4f5a-816b-35b492e50938")]
internal sealed class TransformGeometry : Instance<TransformGeometry>, ITransformable, IStatusProvider, ICustomDropdownHolder
{
    [Output(Guid = "895d2fd4-c40f-4200-89af-395253275b0a")]
    public readonly TransformCallbackSlot<Geometry?> Result = new();

    public TransformGeometry()
    {
        Result.TransformableOp = this;
        Result.UpdateAction = Update;
    }

    IInputSlot ITransformable.TranslationInput => Translation;
    IInputSlot ITransformable.RotationInput => Rotation;
    IInputSlot ITransformable.ScaleInput => Scale;

    public Action<Instance, EvaluationContext>? TransformCallback { get; set; }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var space = (Spaces)Space.GetValue(context).Clamp(0, 1);
        var selectionName = GeometryAttributeChoices.ToSelectionValue(Selection.GetValue(context) ?? string.Empty);
        _lastSource = source;
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
        var localTransform = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);

        var pointCount = source.PointCount;
        _statusMessage = string.Empty;
        // Without a name the flowing Selection applies when there is one, so SelectGeometry -> TransformGeometry just works;
        // <All> ignores any selection
        var ignoresSelection = GeometryAttributeChoices.IsAll(selectionName);
        var isNamed = !ignoresSelection && !string.IsNullOrWhiteSpace(selectionName);
        var hasSelection = !ignoresSelection && TryCollectPointWeights(source, isNamed ? selectionName! : GeometryAttributeNames.Selection);
        if (isNamed && !hasSelection)
        {
            // A named selection that doesn't exist selects nothing; the weights are all zero
            _statusMessage = $"No float attribute '{selectionName}' on points, vertices, primitives or parts; nothing is transformed.";
            hasSelection = true;
        }

        if (space == Spaces.Point)
            _orientations.Compute(source);

        if (_pointTransforms.Length != pointCount)
        {
            _pointTransforms = new Matrix4x4[pointCount];
            _pointRotations = new Quaternion[pointCount];
        }

        for (var point = 0; point < pointCount; point++)
        {
            if (space == Spaces.Object)
            {
                _pointTransforms[point] = localTransform;
                _pointRotations[point] = rotation;
                continue;
            }

            // Into the point's local space, transform there, and back out
            var orientation = _orientations.Rotations[point];
            var localToWorld = Matrix4x4.CreateFromQuaternion(orientation) * Matrix4x4.CreateTranslation(source.Positions[point]);
            Matrix4x4.Invert(localToWorld, out var worldToLocal);
            _pointTransforms[point] = worldToLocal * localTransform * localToWorld;
            _pointRotations[point] = Quaternion.Concatenate(Quaternion.Concatenate(Quaternion.Conjugate(orientation), rotation), orientation);
        }

        WriteOutput(source, hasSelection, space == Spaces.Object && !hasSelection, localTransform, rotation);
        Result.Value = _output;
    }

    private void WriteOutput(Geometry source, bool hasSelection, bool isWholeGeometry, Matrix4x4 localTransform, Quaternion rotation)
    {
        var pointCount = source.PointCount;
        if (_positions.Length != pointCount)
            _positions = new Vector3[pointCount];

        for (var point = 0; point < pointCount; point++)
        {
            var original = source.Positions[point];
            var transformed = Vector3.Transform(original, _pointTransforms[point]);
            _positions[point] = hasSelection ? Vector3.Lerp(original, transformed, _pointWeights[point]) : transformed;
        }

        _output.Positions = _positions;
        _output.PrimitiveVertexOffsets = source.PrimitiveVertexOffsets;
        _output.VertexPoints = source.VertexPoints;
        _output.PrimitiveKind = source.PrimitiveKind;
        _output.PrimitiveClosed = source.PrimitiveClosed;
        _output.Parts = isWholeGeometry ? TransformParts(source.Parts, localTransform) : source.Parts;

        _output.Attributes.Clear();
        var attributes = source.Attributes;
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            var transformed = attribute.TypeInfo switch
                                  {
                                      AttributeTypeInfos.Position or AttributeTypeInfos.Vector or AttributeTypeInfos.Normal
                                          when attribute is GeometryAttribute<Vector3> vectors => TransformVectors(source, vectors, hasSelection, isWholeGeometry, localTransform),
                                      AttributeTypeInfos.Quaternion
                                          when attribute is GeometryAttribute<Vector4> quaternions => TransformQuaternions(source, quaternions, hasSelection, isWholeGeometry, rotation),
                                      _ => null,
                                  };
            _output.Attributes.Add(transformed ?? attribute);
        }

        // Topology is shared from the input, which may have changed since the last update
        _output.InvalidateTopologyCaches();
    }

    /// <returns>Null when the attribute's domain isn't transformed (primitive and part values under a selection or in point space).</returns>
    private GeometryAttribute? TransformVectors(Geometry source, GeometryAttribute<Vector3> attribute, bool hasSelection, bool isWholeGeometry,
                                               Matrix4x4 localTransform)
    {
        var isPerPoint = attribute.Domain is AttributeDomain.Point or AttributeDomain.Vertex;
        if (!isPerPoint && !isWholeGeometry)
            return null;

        var target = GetOwnedAttribute<Vector3>(attribute);
        var values = attribute.Values;
        for (var i = 0; i < values.Length; i++)
        {
            var point = PointOf(source, attribute.Domain, i);
            var matrix = isPerPoint ? _pointTransforms[point] : localTransform;
            var transformed = TransformByTypeInfo(values[i], matrix, attribute.TypeInfo);
            target.Values[i] = hasSelection && isPerPoint ? BlendByTypeInfo(values[i], transformed, _pointWeights[point], attribute.TypeInfo) : transformed;
        }

        return target;
    }

    private GeometryAttribute? TransformQuaternions(Geometry source, GeometryAttribute<Vector4> attribute, bool hasSelection, bool isWholeGeometry,
                                                   Quaternion rotation)
    {
        var isPerPoint = attribute.Domain is AttributeDomain.Point or AttributeDomain.Vertex;
        if (!isPerPoint && !isWholeGeometry)
            return null;

        var target = GetOwnedAttribute<Vector4>(attribute);
        var values = attribute.Values;
        for (var i = 0; i < values.Length; i++)
        {
            var point = PointOf(source, attribute.Domain, i);
            var original = new Quaternion(values[i].X, values[i].Y, values[i].Z, values[i].W);
            var transformed = Quaternion.Concatenate(original, isPerPoint ? _pointRotations[point] : rotation);
            if (hasSelection && isPerPoint)
                transformed = Quaternion.Slerp(original, transformed, _pointWeights[point]);

            target.Values[i] = new Vector4(transformed.X, transformed.Y, transformed.Z, transformed.W);
        }

        return target;
    }

    private static Vector3 TransformByTypeInfo(Vector3 value, Matrix4x4 matrix, AttributeTypeInfos typeInfo)
    {
        switch (typeInfo)
        {
            case AttributeTypeInfos.Position:
                return Vector3.Transform(value, matrix);
            case AttributeTypeInfos.Normal:
                // Normals stay perpendicular under non-uniform scale with the inverse transpose
                if (!Matrix4x4.Invert(matrix, out var inverse))
                    return value;

                var normal = Vector3.TransformNormal(value, Matrix4x4.Transpose(inverse));
                return normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : value;
            default:
                return Vector3.TransformNormal(value, matrix);
        }
    }

    private static Vector3 BlendByTypeInfo(Vector3 original, Vector3 transformed, float weight, AttributeTypeInfos typeInfo)
    {
        var blended = Vector3.Lerp(original, transformed, weight);
        return typeInfo == AttributeTypeInfos.Normal && blended.LengthSquared() > 1e-20f ? Vector3.Normalize(blended) : blended;
    }

    private static int PointOf(Geometry geometry, AttributeDomain domain, int index)
    {
        return domain == AttributeDomain.Vertex ? geometry.VertexPoints[index] : index;
    }

    private PrimitivePart[] TransformParts(PrimitivePart[] parts, Matrix4x4 transform)
    {
        if (parts.Length == 0)
            return parts;

        if (_parts.Length != parts.Length)
            _parts = new PrimitivePart[parts.Length];

        for (var i = 0; i < parts.Length; i++)
            _parts[i] = parts[i] with { Pivot = Vector3.Transform(parts[i].Pivot, transform) };

        return _parts;
    }

    private bool TryCollectPointWeights(Geometry geometry, string name)
    {
        if (_pointWeights.Length != geometry.PointCount)
            _pointWeights = new float[geometry.PointCount];

        return geometry.TryGetPointWeights(name, _pointWeights);
    }

    /** This op's own buffer for a transformed attribute, reused across updates; never the input's. */
    private GeometryAttribute<T> GetOwnedAttribute<T>(GeometryAttribute<T> source) where T : unmanaged
    {
        foreach (var owned in _ownedAttributes)
        {
            if (owned is GeometryAttribute<T> typed && owned.Domain == source.Domain && owned.TypeInfo == source.TypeInfo
                && string.Equals(owned.Name, source.Name, StringComparison.Ordinal))
            {
                typed.Resize(source.Values.Length);
                return typed;
            }
        }

        var created = new GeometryAttribute<T>(source.Name, source.Domain, source.Values.Length, source.TypeInfo);
        _ownedAttributes.Add(created);
        return created;
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    private enum Spaces
    {
        Object,
        Point,
    }

    #region ICustomDropdownHolder - the float attributes of the last input as selections
    string ICustomDropdownHolder.GetValueForInput(Guid inputId) => GeometryAttributeChoices.GetSelectionDisplayValue(Selection.Value, _lastSource);

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId) => GeometryAttributeChoices.GetSelectionOptions(_lastSource);

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (inputId == Selection.Input.InputDefinition.Id && selected != null)
            Selection.SetTypedInputValue(GeometryAttributeChoices.ToSelectionValue(selected));
    }
    #endregion

    private Geometry? _lastSource;
    private readonly Geometry _output = new();
    private readonly GeometryPointOrientations _orientations = new();
    private readonly List<GeometryAttribute> _ownedAttributes = [];
    private Vector3[] _positions = [];
    private Matrix4x4[] _pointTransforms = [];
    private Quaternion[] _pointRotations = [];
    private float[] _pointWeights = [];
    private PrimitivePart[] _parts = [];
    private string _statusMessage = string.Empty;

    [Input(Guid = "90343fe1-bee7-4ec5-a29d-c44b68a3a37c")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "3497c025-efaf-4191-8621-ade40d377ee9")]
    public readonly InputSlot<Vector3> Translation = new();

    [Input(Guid = "b478cdd9-14c5-486b-be7b-4c70bd8f667c")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "80aa5810-3fbd-4d95-96d1-72647d8af302")]
    public readonly InputSlot<Vector3> Scale = new();

    [Input(Guid = "a3096995-909c-42f4-9519-99b25f1c330e")]
    public readonly InputSlot<float> UniformScale = new();

    [Input(Guid = "86c1f762-9d7d-4f9c-bfb3-fc0d75bedf03", MappedType = typeof(Spaces))]
    public readonly InputSlot<int> Space = new();

    [Input(Guid = "85c720c4-8d9e-46a8-aae7-eb77fc581d3a")]
    public readonly InputSlot<string> Selection = new();
}
