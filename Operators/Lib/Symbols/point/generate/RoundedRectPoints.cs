using T3.Core.Utils;

namespace Lib.point.generate;

[Guid("bc8375b9-3da2-4978-8da5-10623bba96d6")]
internal sealed class RoundedRectPoints : Instance<RoundedRectPoints>, ITransformable
{
    private enum CornerRadiusModes
    {
        Relative,
        Absolute,
    }

    [Output(Guid = "be858032-3446-4998-a1ed-512d54c350f9")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

    public RoundedRectPoints()
    {
        OutBuffer.TransformableOp = this;
        OutBuffer.UpdateAction = Update;
    }

    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    private void Update(EvaluationContext context)
    {
        var center = Center.GetValue(context);
        var size = Size.GetValue(context) * UniformScale.GetValue(context);
        var pivot = Pivot.GetValue(context);
        var origin = center + new Vector3(pivot.X * size.X, -pivot.Y * size.Y, 0);
        var cornerRadius = MathF.Max(CornerRadius.GetValue(context), 0);
        var cornerRadiusMode = (CornerRadiusModes)CornerRadiusMode.GetValue(context).Clamp(0, 1);
        var cornerPoints = CornerPoints.GetValue(context).Clamp(1, 1000);
        var strokeWidth = StrokeWidth.GetValue(context);
        var color = Color.GetValue(context);
        var subdivisionLength = SubdivisionLength.GetValue(context);
        var close = Close.GetValue(context);

        _roundCorners[0] = RoundTopLeft.GetValue(context);
        _roundCorners[1] = RoundTopRight.GetValue(context);
        _roundCorners[2] = RoundBottomRight.GetValue(context);
        _roundCorners[3] = RoundBottomLeft.GetValue(context);

        var halfWidth = MathF.Abs(size.X) / 2;
        var halfHeight = MathF.Abs(size.Y) / 2;
        var maxRadius = MathF.Min(halfWidth, halfHeight);
        var radius = cornerRadiusMode == CornerRadiusModes.Relative
                         ? MathF.Min(cornerRadius, 1) * maxRadius
                         : MathF.Min(cornerRadius, maxRadius);
        var canRound = cornerPoints > 1 && radius > 0;

        var outlineCount = 0;
        for (var cornerIndex = 0; cornerIndex < 4; cornerIndex++)
        {
            outlineCount += canRound && _roundCorners[cornerIndex] ? cornerPoints : 1;
        }

        if (_outline.Length != outlineCount)
            _outline = new Vector3[outlineCount];

        var outlineIndex = 0;
        for (var cornerIndex = 0; cornerIndex < 4; cornerIndex++)
        {
            var signX = cornerIndex is 1 or 2 ? 1f : -1f;
            var signY = cornerIndex < 2 ? 1f : -1f;

            if (!canRound || !_roundCorners[cornerIndex])
            {
                _outline[outlineIndex++] = new Vector3(signX * halfWidth, signY * halfHeight, 0);
                continue;
            }

            // Walk clockwise, starting at the edge that precedes this corner.
            var arcCenter = new Vector3(signX * (halfWidth - radius), signY * (halfHeight - radius), 0);
            var startAngle = MathF.PI - cornerIndex * MathF.PI / 2;
            for (var arcIndex = 0; arcIndex < cornerPoints; arcIndex++)
            {
                var angle = startAngle - (float)arcIndex / (cornerPoints - 1) * MathF.PI / 2;
                _outline[outlineIndex++] = arcCenter + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0) * radius;
            }
        }

        // An open outline skips the last segment; its final point ends the line, a closed one repeats the first point.
        var segmentCount = close ? outlineCount : outlineCount - 1;
        var lineCount = 1;
        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            lineCount += GetSubdivisionSteps(segmentIndex);
        }

        // A trailing separator keeps closed outlines from being joined when lists are combined.
        var pointCount = close ? lineCount + 1 : lineCount;

        if (_points.Length != pointCount)
            _points = new Point[pointCount];

        var scale = new Vector3(strokeWidth);
        var pointIndex = 0;
        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var from = _outline[segmentIndex];
            var to = _outline[(segmentIndex + 1) % outlineCount];
            var steps = GetSubdivisionSteps(segmentIndex);
            for (var stepIndex = 0; stepIndex < steps; stepIndex++)
            {
                SetPoint(pointIndex++, Vector3.Lerp(from, to, (float)stepIndex / steps));
            }
        }

        if (close)
        {
            SetPoint(pointIndex++, _outline[0]);
            _points[pointIndex] = Point.Separator();
        }
        else
        {
            SetPoint(pointIndex, _outline[outlineCount - 1]);
        }

        ResourceManager.SetupBufferWithViews(_points, ref _bufferWithViews);
        OutBuffer.Value = _bufferWithViews;
        return;

        int GetSubdivisionSteps(int segmentIndex)
        {
            if (subdivisionLength <= 0)
                return 1;

            var segmentLength = Vector3.Distance(_outline[segmentIndex], _outline[(segmentIndex + 1) % outlineCount]);
            return Math.Clamp((int)MathF.Ceiling(segmentLength / subdivisionLength), 1, MaxStepsPerSegment);
        }

        void SetPoint(int index, Vector3 position)
        {
            _points[index] = new Point
                                 {
                                     Position = origin + position,
                                     F1 = 1,
                                     Orientation = Quaternion.Identity,
                                     Color = color,
                                     Scale = scale,
                                     F2 = lineCount > 1 ? (float)index / (lineCount - 1) : 0,
                                 };
        }
    }

    /** Guards against a tiny SubdivisionLength exploding the buffer size. */
    private const int MaxStepsPerSegment = 10000;

    private Vector3[] _outline = [];
    private Point[] _points = [];
    private readonly bool[] _roundCorners = new bool[4];
    private BufferWithViews _bufferWithViews;

    [Input(Guid = "0e5dbfad-a2c5-4737-b347-28483179eb4b")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "983f91ab-442e-490e-a460-b79efc93d04c")]
    public readonly InputSlot<Vector2> Pivot = new();

    [Input(Guid = "d4142b7e-41ba-49ac-b370-3508b79df1b1")]
    public readonly InputSlot<Vector2> Size = new();

    [Input(Guid = "728562da-70d4-417f-9d7f-457231a306b8")]
    public readonly InputSlot<float> UniformScale = new();

    [Input(Guid = "7c9696a7-e289-4a66-a79e-064f6a56cf63")]
    public readonly InputSlot<float> CornerRadius = new();

    [Input(Guid = "a246b542-052e-43ec-a59e-a19c85ee899d", MappedType = typeof(CornerRadiusModes))]
    public readonly InputSlot<int> CornerRadiusMode = new();

    [Input(Guid = "e3e3b412-eeda-411a-8143-caf24bdf3b30")]
    public readonly InputSlot<int> CornerPoints = new();

    [Input(Guid = "482c822e-e0b9-4b1c-b8b4-e0e814c62bc8")]
    public readonly InputSlot<bool> RoundTopLeft = new();

    [Input(Guid = "bd4fcbb5-32b7-43d1-9e3d-5b3320ab86c9")]
    public readonly InputSlot<bool> RoundTopRight = new();

    [Input(Guid = "427d3439-6142-45d9-a2a3-bae60ed4a5c0")]
    public readonly InputSlot<bool> RoundBottomRight = new();

    [Input(Guid = "b1614dae-271c-4d85-bea4-d964159a9a77")]
    public readonly InputSlot<bool> RoundBottomLeft = new();

    [Input(Guid = "9137d371-142d-46fe-aea3-e983a87e9cce")]
    public readonly InputSlot<float> SubdivisionLength = new();

    [Input(Guid = "3ab6b4e9-b75e-4939-abe2-1b302b02d81c")]
    public readonly InputSlot<bool> Close = new();

    [Input(Guid = "20227bc3-a3c7-4174-bd94-5638b4824467")]
    public readonly InputSlot<float> StrokeWidth = new();

    [Input(Guid = "11553bae-3677-471e-9e4a-8adb372e34e7")]
    public readonly InputSlot<Vector4> Color = new();
}
