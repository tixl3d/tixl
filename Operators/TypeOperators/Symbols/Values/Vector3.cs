namespace Types.Values;

[Guid("94a5de3b-ee6a-43d3-8d21-7b8fe94b042b")]
public sealed class Vector3 : Instance<Vector3>, IExtractedInput<System.Numerics.Vector3>
{
    [Output(Guid = "AEDAEAD8-CCF0-43F0-9188-A79AF8D45250")]
    public readonly Slot<System.Numerics.Vector3> Result = new();

    public Vector3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = new System.Numerics.Vector3(X.GetValue(context), Y.GetValue(context), Z.GetValue(context));
    }
        
    [Input(Guid = "084D5D0D-8FD4-431D-BF6C-8F082CCE1D3F")]
    public readonly InputSlot<float> X = new();

    [Input(Guid = "458891B9-0244-401A-B0A5-3A7EE365E7CB")]
    public readonly InputSlot<float> Y = new();
        
    [Input(Guid = "627F766E-056C-413E-8530-838D673BD031")]
    public readonly InputSlot<float> Z = new();
        
    public Slot<System.Numerics.Vector3> OutputSlot => Result;

    public void SetTypedInputValuesTo(System.Numerics.Vector3 value, out IEnumerable<IInputSlot> changedInputs)
    {
        changedInputs = new[] { X, Y, Z };
        X.TypedInputValue.Value = value.X;
        Y.TypedInputValue.Value = value.Y;
        Z.TypedInputValue.Value = value.Z;
    }
}