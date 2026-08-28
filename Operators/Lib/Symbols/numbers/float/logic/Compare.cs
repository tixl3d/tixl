using T3.Core.Utils;

namespace Lib.numbers.@float.logic;

[Guid("026869ee-b62f-481e-aadf-f8a1db77fe65")]
internal sealed class Compare : Instance<Compare>
{
    [Output(Guid = "7149C7D2-242F-4D57-AC21-19E86700708A")]
    public readonly Slot<bool> IsTrue = new();

    public Compare()
    {
        IsTrue.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var test = TestValue.GetValue(context);
        //var mod = Mod.GetValue(context);
        switch ((Modes)Mode.GetValue(context).Clamp(0, Enum.GetValues(typeof(Modes)).Length -1))
        {
            case Modes.IsSmaller:
                IsTrue.Value =  v < test;
                break;
            case Modes.IsEqual:
                IsTrue.Value = Math.Abs(v-test) < Precision.GetValue(context);
                break;
            case Modes.IsLarger:
                IsTrue.Value =  v > test;
                break;
            case Modes.IsNotEqual:
                IsTrue.Value = Math.Abs(v-test) >= Precision.GetValue(context);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public enum Modes
    {
        IsSmaller,
        IsEqual,
        IsLarger,
        IsNotEqual,
    }
        
    [Input(Guid = "8d98d88c-7a0e-4282-823e-4889ef286e5a")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "5A39F9AD-F447-493E-94F1-9D2CA7627420")]
    public readonly InputSlot<float> TestValue = new();
        
    [Input(Guid = "f1537faa-1bd2-44c9-b0ae-d06c5af5cdef", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();
        
    [Input(Guid = "B37CE031-92CD-4A18-8FF2-3F79DCCDFE9F")]
    public readonly InputSlot<float> Precision = new(0.001f);

}