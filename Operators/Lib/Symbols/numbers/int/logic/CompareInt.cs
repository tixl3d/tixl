using T3.Core.Utils;

namespace Lib.numbers.@int.logic;

[Guid("05cf9ea7-045d-421f-8ed3-2c2f6b325a46")]
internal sealed class CompareInt : Instance<CompareInt>
{
    [Output(Guid = "ff14eb99-aafd-46e1-9d24-ca6647f700d1")]
    public readonly Slot<bool> IsTrue = new();

    [Output(Guid = "B8D8D223-B914-4D00-B438-E286CA97707F")]
    public readonly Slot<int> ResultValue = new();

    public CompareInt()
    {
        IsTrue.UpdateAction += Update;
        ResultValue.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var test = TestValue.GetValue(context);
            
            
        var result = false;
        switch ((Modes)Mode.GetValue(context).Clamp(0, Enum.GetValues(typeof(Modes)).Length -1))
        {
            case Modes.IsSmaller:
                result =  v < test;
                break;
            case Modes.IsEqual:
                result =  v == test;
                break;
            case Modes.IsLarger:
                result =  v > test;
                break;
            case Modes.IsNotEqual:
                result =  v != test;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        IsTrue.Value = result;
        var valueForTrue = ResultForTrue.GetValue(context);
        var valueForFalse = ResultForFalse.GetValue(context);
        ResultValue.Value = result
                                ? valueForTrue
                                : valueForFalse;
            
        ResultValue.DirtyFlag.Clear();
        IsTrue.DirtyFlag.Clear();
    }

    public enum Modes
    {
        IsSmaller,
        IsEqual,
        IsLarger,
        IsNotEqual,
    }
        
    [Input(Guid = "3B6CA34B-4A64-458A-874F-A0AA094FC278")]
    public readonly InputSlot<int> Value = new();

    [Input(Guid = "A2E3A00E-10E4-4F52-B923-5E09CC0BFC08")]
    public readonly InputSlot<int> TestValue = new();
        
    [Input(Guid = "5bf37ae4-bb84-42ee-96f9-52c2adefa669", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();
        
    [Input(Guid = "BFA7D45A-0F98-4016-AE34-D5F653E821D6")]
    public readonly InputSlot<int> ResultForTrue = new();

    [Input(Guid = "158E2790-1244-4509-8911-B850FFCEE29F")]
    public readonly InputSlot<int> ResultForFalse = new();


}