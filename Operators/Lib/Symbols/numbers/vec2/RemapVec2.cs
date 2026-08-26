using T3.Core.Utils;

namespace Lib.numbers.vec2;

[Guid("03c157ef-f0bc-4a2e-b862-775c6e886997")]
internal sealed class RemapVec2 : Instance<RemapVec2>
{
    [Output(Guid = "101E81C8-0A9A-4BEC-9BE6-ACA50C8468D3")]
    public readonly Slot<Vector2> Result = new();

    public RemapVec2()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var value = Value.GetValue(context);
        var inMin = RangeInMin.GetValue(context);
        var inMax = RangeInMax.GetValue(context);
        var outMin = RangeOutMin.GetValue(context);
        var outMax = RangeOutMax.GetValue(context);

        var factor = (value - inMin) / (inMax - inMin);
        var v = factor * (outMax - outMin) + outMin;

        switch ((Modes)Mode.GetValue(context))
        {
            case Modes.Clamped:
            {
                v = MathUtils.Clamp(v, outMin, outMax);
                break;
            }
            case Modes.Modulo:
            {
                var delta = outMax - outMin;
                v = new Vector2(MathUtils.Fmod(v.X, delta.X),
                                MathUtils.Fmod(v.Y, delta.Y));
            }
                break;
        }

        Result.Value = v;
    }

    private enum Modes
    {
        Normal,
        Clamped,
        Modulo,
    }

    [Input(Guid = "CFC5B5E0-1A4A-4CCB-AFEB-2C13D9889FDD")]
    public readonly InputSlot<Vector2> Value = new();

    [Input(Guid = "517DBBB0-6205-4D87-A84F-1E2C69CDABBE")]
    public readonly InputSlot<Vector2> RangeInMin = new();

    [Input(Guid = "80A87FE2-9CCC-4A3D-AE39-44BA4431CB9B")]
    public readonly InputSlot<Vector2> RangeInMax = new();

    [Input(Guid = "0E1C8A24-5183-4DCA-9726-39368AC4A6C4")]
    public readonly InputSlot<Vector2> RangeOutMin = new();

    [Input(Guid = "CF991EF5-976D-4536-9A69-E6277A1A914E")]
    public readonly InputSlot<Vector2> RangeOutMax = new();
        
    [Input(Guid = "3F108A20-0551-45FC-97C1-4E85E92A4DDE", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();         
}