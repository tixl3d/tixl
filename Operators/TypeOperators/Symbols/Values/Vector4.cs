namespace Types.Values;

[Guid("f2e323bd-f881-41a8-81e2-e8f2ac1984dc")]
public sealed class Vector4 : Instance<Vector4>, IExtractedInput<System.Numerics.Vector4>
{
    [Output(Guid = "14CDC3DD-F229-4F8F-B953-4F9D587D6F58")]
    public readonly Slot<System.Numerics.Vector4> Result = new();

    public Vector4()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = new System.Numerics.Vector4(X.GetValue(context), Y.GetValue(context), Z.GetValue(context), W.GetValue(context));
    }
        
    [Input(Guid = "bdd35cdd-2220-4c58-9ec8-a5e48d7aaf7e")]
    public readonly InputSlot<float> X = new();

    [Input(Guid = "46a4ee87-ab2b-406b-a9ae-c000f887d99f")]
    public readonly InputSlot<float> Y = new();
        
    [Input(Guid = "59908ecc-1822-4aba-a2d9-cfe97168b3b3")]
    public readonly InputSlot<float> Z = new();
        
    [Input(Guid = "6CE53000-34D6-4D9A-AEF3-164FD223F6D2")]
    public readonly InputSlot<float> W = new();
        

    public Slot<System.Numerics.Vector4> OutputSlot => Result;

    public void SetTypedInputValuesTo(System.Numerics.Vector4 value, out IEnumerable<IInputSlot> inputs)
    {
        inputs = new[] { X, Y, Z, W };
            
        X.TypedInputValue.Value = value.X;
        Y.TypedInputValue.Value = value.Y;
        Z.TypedInputValue.Value = value.Z;
        W.TypedInputValue.Value = value.W;
    }
}