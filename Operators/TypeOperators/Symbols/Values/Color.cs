namespace Types.Values;

[Guid("70176d3c-825c-40b3-8121-a465735518fe")]
public sealed class Color : Instance<Color>//, IExtractedInput<System.Numerics.Vector4> // todo: allow more than one type to be extracted to a given type? or have colors operate nicer with vecs
{
    [Output(Guid = "fae78369-9db9-4b00-94f2-89e7581db426")]
    public readonly Slot<System.Numerics.Vector4> Output = new();

    public Color()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Output.Value = RGBA.GetValue(context);
    }
        
    [Input(Guid = "03dc1ef1-d75a-4f65-a607-d5dc4de56a2c")]
    public readonly InputSlot<System.Numerics.Vector4> RGBA = new();

    public Slot<System.Numerics.Vector4> OutputSlot => Output;

    public void SetTypedInputValuesTo(System.Numerics.Vector4 value, out IEnumerable<IInputSlot> inputs)
    {
        inputs = [RGBA];
            
        var vec = new System.Numerics.Vector4(value.X, value.Y, value.Z, value.W);
        RGBA.TypedInputValue.Value = vec;
    }
}