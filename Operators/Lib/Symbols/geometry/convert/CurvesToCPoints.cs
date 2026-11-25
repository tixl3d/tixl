using Lib.Utils;

namespace Lib.geometry.convert;

/// <summary>
/// Flattens the curve primitives of a Geometry into a CPU point list for the CPoints ops; [CurvesToPoints] does the
/// same and uploads the result to the GPU.
/// </summary>
[Guid("6a1d8c3f-2b57-4e90-a4f6-c8e2d5b7f019")]
internal sealed class CurvesToCPoints : Instance<CurvesToCPoints>
{
    [Output(Guid = "e3c5a7f1-9d24-4b68-8f0e-a1b3c6d9e274")]
    public readonly Slot<StructuredList> Points = new();

    public CurvesToCPoints()
    {
        Points.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        Points.Value = _sampler.Sample(Curves.GetValue(context),
                                       Tolerance.GetValue(context),
                                       CloseLoops.GetValue(context),
                                       (CurvePointSampler.Samplings)Math.Clamp(Sampling.GetValue(context), 0, 2),
                                       (CurvePointSampler.PolygonEdgeModes)Math.Clamp(PolygonEdges.GetValue(context), 0, 2),
                                       SharpAngle.GetValue(context));
    }

    private readonly CurvePointSampler _sampler = new();

    [Input(Guid = "b7f2d9c4-5a13-4e86-9d0b-3c6e8a1f5d27")]
    public readonly InputSlot<Geometry> Curves = new();

    [Input(Guid = "4e9a6c1b-d872-4f35-b0e8-7a2d5f9c3e61")]
    public readonly InputSlot<float> Tolerance = new();

    [Input(Guid = "d2c8e5a3-1f64-4b97-8e3a-c5b0d7f4a916")]
    public readonly InputSlot<bool> CloseLoops = new();

    [Input(Guid = "f9897a6d-acb2-4291-a3e0-aa2fee90cfd7", MappedType = typeof(CurvePointSampler.Samplings))]
    public readonly InputSlot<int> Sampling = new();

    [Input(Guid = "91c96818-ba39-4cec-af54-ae005c8610a5", MappedType = typeof(CurvePointSampler.PolygonEdgeModes))]
    public readonly InputSlot<int> PolygonEdges = new();

    [Input(Guid = "246c6a25-be64-4d99-8452-b0fd9ac66f2f")]
    public readonly InputSlot<float> SharpAngle = new();
}
