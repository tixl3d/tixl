namespace Types.Color;

[Guid("c5c3232c-70f8-4d1d-847d-6b6b25f3d9d6")]
public sealed class Gradient :Instance<Gradient?>
{
    [Output(Guid = "631db643-e8af-44c5-be36-2e7c4c818015")]
    public readonly Slot<T3.Core.DataTypes.Gradient?> OutGradient = new();
    
    public Gradient()
    {
        OutGradient.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        OutGradient.Value = GradientInput.GetValue(context);
    }
    
    [Input(Guid = "b129b4d8-7c1b-47e4-b156-b24d6632b13a")]
    public readonly InputSlot<T3.Core.DataTypes.Gradient?> GradientInput = new();
    
    public void SetTypedInputValuesTo(T3.Core.DataTypes.Gradient? value, out IEnumerable<IInputSlot> changedInputs)
    {
        changedInputs = new[] { GradientInput };
        GradientInput.TypedInputValue.Value = value;
    }
}