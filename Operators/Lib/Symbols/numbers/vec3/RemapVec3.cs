using T3.Core.Utils;

namespace Lib.numbers.vec3;

[Guid("dc9c4099-1c84-484d-abee-1d0fb4da6292")]
internal sealed class RemapVec3 :Instance<RemapVec3>{
    [Output(Guid = "C1FF4F4B-0FDF-484A-848F-17B07B6306D2")]
    public readonly Slot<Vector3> Result = new();

    public RemapVec3()
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
                v = new Vector3(MathUtils.Fmod(v.X, delta.X),
                                MathUtils.Fmod(v.Y, delta.Y),
                                MathUtils.Fmod(v.Z, delta.Z)
                                );
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

    [Input(Guid = "A341C041-A993-4E71-92EF-8239DB7327F6")]
    public readonly InputSlot<Vector3> Value = new();

    [Input(Guid = "DB864BC9-F34C-49F5-AC38-FBA1D4DF3F58")]
    public readonly InputSlot<Vector3> RangeInMin = new();

    [Input(Guid = "07308CD1-FE2C-47B2-A9C3-A8EA2B1F20D5")]
    public readonly InputSlot<Vector3> RangeInMax = new();

    [Input(Guid = "1EB622B5-4D72-4F25-AB13-6D7413036218")]
    public readonly InputSlot<Vector3> RangeOutMin = new();

    [Input(Guid = "17627C2E-35D6-45EB-A681-42BABD4602E6")]
    public readonly InputSlot<Vector3> RangeOutMax = new();
        
    [Input(Guid = "36e8692a-6b24-488b-a1da-2372bd4a074b", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();         

}