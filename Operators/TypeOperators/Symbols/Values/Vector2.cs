namespace Types.Values;

[Guid("926ab3fd-fbaf-4c4b-91bc-af277000dcb8")]
public sealed class Vector2 : Instance<Vector2>, IExtractedInput<System.Numerics.Vector2>
{
    [Output(Guid = "6276597C-580F-4AA4-B066-2735C415FD7C")]
    public readonly Slot<System.Numerics.Vector2> Result = new();

    public Vector2()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = new System.Numerics.Vector2(X.GetValue(context), Y.GetValue(context));
    }

    [Input(Guid = "6b9d0106-78f9-4507-a0f6-234c5dfb0f85")]
    public readonly InputSlot<float> X = new();

    [Input(Guid = "2d9c040d-5244-40ac-8090-d8d57323487b")]
    public readonly InputSlot<float> Y = new();

    public Slot<System.Numerics.Vector2> OutputSlot => Result;

    public void SetTypedInputValuesTo(System.Numerics.Vector2 value, out IEnumerable<IInputSlot> changedInputs)
    {
        changedInputs = new[] { X, Y };
        X.TypedInputValue.Value = value.X;
        Y.TypedInputValue.Value = value.Y;
    }
}