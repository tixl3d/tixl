namespace Lib.numbers.color;

[Guid("8211249d-7a26-4ad0-8d84-56da72a5c536")]
public sealed class SampleGradient : Instance<SampleGradient>, IExtractedInput<Gradient>
{
        
    [Output(Guid = "963611E7-F55E-4C94-96E6-34E195558A2B")]
    public readonly Slot<Vector4> Color = new();

        
    [Output(Guid = "9F3D0701-86E8-436E-8652-918BA23B2CEF")]
    public readonly Slot<Gradient> OutGradient = new();


    public SampleGradient()
    {
        Color.UpdateAction += Update;
        OutGradient.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var t = SamplePos.GetValue(context);
        var gradient = Gradient.GetValue(context);
        
        if (gradient == null)
            return;
        
        var overrideInterpolation = OverrideInterpolation.GetValue(context);
        if (overrideInterpolation)
        {
            var interpolation = (Gradient.Interpolations) Interpolation.GetValue(context);
            gradient.Interpolation = interpolation;
        }
        Color.Value = gradient.Sample(t);
        OutGradient.Value = gradient;
    }

    [Input(Guid = "a4527e01-f19a-4200-85e5-00144f3ce061")]
    public readonly InputSlot<float> SamplePos = new();
        
    [Input(Guid = "EFF10FAD-CF95-4133-91DB-EFC41258CD1B")]
    public readonly InputSlot<Gradient> Gradient = new();

    [Input(Guid = "11657D72-6AD9-41D9-AB5A-DE7177AF5BE4")]
    public readonly InputSlot<bool> OverrideInterpolation = new();
    
    [Input(Guid = "76CF4A72-2D25-48CB-A1EC-08D0DDABB053", MappedType = typeof(Gradient.Interpolations))]
    public readonly InputSlot<int> Interpolation = new();

    public Slot<Gradient> OutputSlot => OutGradient;

    public void SetTypedInputValuesTo(Gradient value, out IEnumerable<IInputSlot> changedInputs)
    {
        changedInputs = new[] { Gradient };
        Gradient.TypedInputValue.Value = value;
    }
}