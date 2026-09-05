namespace Types.Values;

[Guid("f1218934-f874-4f70-a077-0ebe7d12104d")]
public sealed class Int2 : Instance<Int2>, IExtractedInput<T3.Core.DataTypes.Vector.Int2>
{
    [Output(Guid = "3265FF5F-9D8D-48D5-A6F8-9085B4F19A78")]
    public readonly Slot<T3.Core.DataTypes.Vector.Int2> Result = new();

    [Output(Guid = "F5912E37-AF78-49C2-91D0-0675A8845F00")]
    public readonly Slot<int> Capacity = new();
        
    public Int2()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var s = new T3.Core.DataTypes.Vector.Int2(X.GetValue(context), Y.GetValue(context));
        Result.Value = s;
        Capacity.Value = s.Width * s.Height;
    }
        
    [Input(Guid = "579E72D6-638E-4B17-BB4E-88A55E3A1D4D")]
    public readonly InputSlot<int> X = new();
        
    [Input(Guid = "53602AF2-48D9-42AB-80C3-AE1F1E600D28")]
    public readonly InputSlot<int> Y = new();

    public Slot<T3.Core.DataTypes.Vector.Int2> OutputSlot => Result;
    public void SetTypedInputValuesTo(T3.Core.DataTypes.Vector.Int2 value, out IEnumerable<IInputSlot> changedInputs)
    {
        changedInputs = [X, Y];
            
        X.TypedInputValue.Value = value.X;
        Y.TypedInputValue.Value = value.Y;
    }
}