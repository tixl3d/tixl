using T3.Core.Utils;

namespace Lib.numbers.vec3;

[Guid("baa8cd75-5621-42ba-a79c-b008b7caa141")]
internal sealed class RoundVec3 : Instance<RoundVec3>
{
    [Output(Guid = "610e09be-54e8-40a6-9cef-e4da953a4e78")]
    public readonly Slot<Vector3> Result = new();

    public RoundVec3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var precision = Precision.GetValue(context);

        var v = Value.GetValue(context);
        var result = Mode.GetEnumValue<Modes>(context) switch
                         {
                             Modes.Round => new Vector3(
                                                        MathF.Round(v.X * precision.X) / precision.X,
                                                        MathF.Round(v.Y * precision.Y) / precision.Y,
                                                        MathF.Round(v.Z * precision.Z) / precision.Z),
                             Modes.Floor => new Vector3(MathF.Floor(v.X * precision.X) / precision.X,
                                                        MathF.Floor(v.Y * precision.Y) / precision.Y,
                                                        MathF.Floor(v.Z * precision.Z) / precision.Z),
                             Modes.Ceiling => new Vector3(MathF.Ceiling(v.X * precision.X) / precision.X,
                                                          MathF.Ceiling(v.Y * precision.Y) / precision.Y,
                                                          MathF.Ceiling(v.Z * precision.Z) / precision.Z),
                             _ => Vector3.Zero
                         };

        Result.Value = result;
    }

    private enum Modes
    {
        Round,
        Floor,
        Ceiling,
    }

    [Input(Guid = "158ff4f8-7470-4402-b16e-54a3a252fe7a")]
    public readonly InputSlot<Vector3> Value = new();

    [Input(Guid = "93a69b1c-365f-495f-8cdf-7ca1e78407e2")]
    public readonly InputSlot<Vector3> Precision = new();

    [Input(Guid = "ACB38B7F-D466-40B4-9D59-201BABFF00AA", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();
}