using Lib.Utils;

namespace Lib.geometry;

/// <summary>
/// Scatters points deterministically inside a box volume or a MeshGeometry.
/// An optional ScalarField modulates the density via rejection sampling
/// (values clamped to 0..1), so dense and sparse regions can be sculpted.
/// </summary>
[Guid("9a51e3c7-4d08-4b62-bf35-1c7a8e9d0f46")]
internal sealed class ScatterPointsInVolume : Instance<ScatterPointsInVolume>, ITransformable
{
    [Output(Guid = "5c0b7d29-8ae4-4f13-96d7-e2a94c6b8503")]
    public readonly TransformCallbackSlot<StructuredList> ResultList = new();

    public ScatterPointsInVolume()
    {
        ResultList.TransformableOp = this;
        ResultList.UpdateAction = Update;
    }

    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => Size;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    private void Update(EvaluationContext context)
    {
        var count = Math.Clamp(Count.GetValue(context), 0, 1_000_000);
        var center = Center.GetValue(context);
        var size = Size.GetValue(context);
        var density = Density.GetValue(context);
        var seed = Seed.GetValue(context);
        var geometry = Geometry.GetValue(context);

        var boundsMin = center - size * 0.5f;
        var boundsMax = center + size * 0.5f;
        MeshInsideTester? insideTester = null;
        if (geometry != null && geometry.Positions.Length > 0)
        {
            // Rebuild the ray grid only when the mesh actually changed
            if (_insideTester == null || _insideTesterGeometry != geometry || _insideTesterVersion != geometry.Version)
            {
                _insideTester = new MeshInsideTester(geometry);
                _insideTesterGeometry = geometry;
                _insideTesterVersion = geometry.Version;
            }

            insideTester = _insideTester;
            boundsMin = insideTester.Min;
            boundsMax = insideTester.Max;
        }

        var extent = boundsMax - boundsMin;
        var rngState = (uint)(seed * 747796405 + 2891336453);

        var accepted = 0;
        var maxAttempts = (long)count * 50 + 100;
        EnsureCapacity(count);

        for (long attempt = 0; attempt < maxAttempts && accepted < count; attempt++)
        {
            var position = boundsMin + new Vector3(NextFloat(ref rngState) * extent.X,
                                                   NextFloat(ref rngState) * extent.Y,
                                                   NextFloat(ref rngState) * extent.Z);

            if (insideTester != null && !insideTester.IsInside(position))
                continue;

            if (density != null)
            {
                var acceptance = Math.Clamp(density.Sample(position), 0f, 1f);
                if (NextFloat(ref rngState) >= acceptance)
                    continue;
            }

            _pointList[accepted++] = new Point
                                         {
                                             Position = position,
                                             F1 = 1,
                                             F2 = 0,
                                             Scale = Vector3.One,
                                             Orientation = Quaternion.Identity,
                                             Color = Vector4.One,
                                         };
        }

        if (_pointList.NumElements != accepted)
            _pointList.SetLength(accepted);

        ResultList.Value = _pointList;
    }

    private void EnsureCapacity(int count)
    {
        if (_pointList.NumElements != count)
            _pointList.SetLength(count);
    }

    private static float NextFloat(ref uint state)
    {
        // PCG-style output permutation on an LCG state
        state = state * 747796405 + 2891336453;
        var word = ((state >> (int)((state >> 28) + 4)) ^ state) * 277803737;
        word = (word >> 22) ^ word;
        return word / 4294967296f;
    }

    private readonly StructuredList<Point> _pointList = new(16);
    private MeshInsideTester? _insideTester;
    private MeshGeometry? _insideTesterGeometry;
    private int _insideTesterVersion;

    [Input(Guid = "e17d4a92-6b58-4c03-a9f1-84c5d2e7b360")]
    public readonly InputSlot<int> Count = new();

    [Input(Guid = "3f82c6b1-9e07-45da-b824-56a1f9d3c708")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "a94b0e58-27c3-4f61-90ad-c8e52b7f1d39")]
    public readonly InputSlot<Vector3> Size = new();

    [Input(Guid = "58c1f7a3-b2d9-4e06-85cb-7f4a90e6d221")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "c6a35d18-40fb-4972-bde6-19f8c2a7e054")]
    public readonly InputSlot<ScalarField> Density = new();

    [Input(Guid = "12e9b8c4-75a0-4d36-9f82-3dc6e1b0a597")]
    public readonly InputSlot<int> Seed = new();
}
