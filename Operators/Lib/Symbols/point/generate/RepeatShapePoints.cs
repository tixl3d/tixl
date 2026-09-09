using SharpDX.Direct3D11;

namespace Lib.point.generate;

[Guid("BE4931F8-9816-48E5-A0A2-6AE8BC2DD390")]
internal sealed class RepeatShapePoints : Instance<RepeatShapePoints>
{
    [Output(Guid = "64248B6B-C738-4D02-850C-9765CD0616D8")]
    public readonly Slot<BufferWithViews> Points = new();

    [Output(Guid = "B559D2B6-9A76-47C6-A0C6-2BB159B4C5FD")]
    public readonly Slot<int> ShapeSize = new();

    public RepeatShapePoints()
    {
        Points.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var shape = Shape.GetValue(context);
        var count = Math.Max(1, Count.GetValue(context));
        var offsetPerCopy = OffsetPerCopy.GetValue(context);
        var rotatePerCopy = RotatePerCopy.GetValue(context);
        var scalePerCopy = ScalePerCopy.GetValue(context);

        if (shape == null || shape.NumElements == 0)
        {
            Log.Warning("RepeatShapePoints: No shape points connected");
            return;
        }

        if (shape is not StructuredList<Point> typedShape)
        {
            Log.Error("RepeatShapePoints: Shape is not a StructuredList<Point>");
            return;
        }

        var shapePoints = new List<Point>(typedShape.NumElements);
        foreach (var p in typedShape.TypedElements)
        {
            if (!Point.IsSeparator(in p))
                shapePoints.Add(p);
        }

        var shapePointCount = shapePoints.Count;
        if (shapePointCount < 3)
        {
            Log.Warning("RepeatShapePoints: Shape needs at least 3 usable points");
            return;
        }

        ShapeSize.Value = shapePointCount;

        // The rotation pivot and repeat reference point of the base shape
        var centroid = Vector3.Zero;
        foreach (var p in shapePoints)
            centroid += p.Position;
        centroid /= shapePointCount;

        var rotation = new Vector3(
            rotatePerCopy.X * MathF.PI / 180f,
            rotatePerCopy.Y * MathF.PI / 180f,
            rotatePerCopy.Z * MathF.PI / 180f);

        var totalPointCount = shapePointCount * count;
        if (_points.Length != totalPointCount)
            _points = new Point[totalPointCount];

        var writeIndex = 0;
        for (var copyIndex = 0; copyIndex < count; copyIndex++)
        {
            var scale = 1f + scalePerCopy * copyIndex;
            var copyQuaternion = Quaternion.CreateFromYawPitchRoll(
                rotation.Y * copyIndex,
                rotation.X * copyIndex,
                rotation.Z * copyIndex);
            var copyOffset = offsetPerCopy * copyIndex;

            for (var pointIndex = 0; pointIndex < shapePointCount; pointIndex++)
            {
                var delta = shapePoints[pointIndex].Position - centroid;
                var point = shapePoints[pointIndex];
                point.Position = Vector3.Transform(delta * scale, copyQuaternion) + centroid + copyOffset;
                point.F1 = copyIndex;
                point.Orientation = Quaternion.Normalize(copyQuaternion * point.Orientation);
                _points[writeIndex++] = point;
            }
        }

        ResourceManager.SetupStructuredBuffer(_points, Point.Stride * totalPointCount, Point.Stride, ref _gpuBuffer);
        ResourceManager.CreateStructuredBufferSrv(_gpuBuffer, ref _srv);
        ResourceManager.CreateStructuredBufferUav(_gpuBuffer, UnorderedAccessViewBufferFlags.None, ref _uav);

        _bufferWithViews.Buffer = _gpuBuffer;
        _bufferWithViews.Srv = _srv;
        _bufferWithViews.Uav = _uav;
        Points.Value = _bufferWithViews;
    }

    private Point[] _points = Array.Empty<Point>();
    private Buffer _gpuBuffer;
    private ShaderResourceView _srv;
    private UnorderedAccessView _uav;
    private readonly BufferWithViews _bufferWithViews = new();

    [Input(Guid = "A1AC028E-ADB7-46FC-814C-66551773AFBC")]
    public readonly InputSlot<int> Count = new();

    [Input(Guid = "6FAA2E91-082F-4656-8997-CEF767A7FBF2")]
    public readonly InputSlot<Vector3> OffsetPerCopy = new();

    [Input(Guid = "2E5DA9D4-0E82-4A93-84F8-BEE94DF58EBA")]
    public readonly InputSlot<Vector3> RotatePerCopy = new();

    [Input(Guid = "CD071504-AC40-4D03-9740-136B1C8AAE19")]
    public readonly InputSlot<float> ScalePerCopy = new();

    [Input(Guid = "3CA75113-5CD1-4FF5-8C4C-B8B22D60020D")]
    public readonly InputSlot<StructuredList> Shape = new();
}