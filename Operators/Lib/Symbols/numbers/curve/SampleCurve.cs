namespace Lib.numbers.curve;

[Guid("b724ea74-d5d7-4928-9cd1-7a7850e4e179")]
public sealed class SampleCurve : Instance<SampleCurve>
{
    [Output(Guid = "fc51bee8-091c-4c66-a7df-12f6f69e3783")]
    public readonly Slot<float> Result = new();
        
    [Output(Guid = "1A20C791-A1FE-4A14-A9CF-615691948F2D")]
    public readonly Slot<Curve> CurveOutput = new();

    public SampleCurve()
    {
        Result.UpdateAction += Update;
        CurveOutput.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Curve == null)
            return;
            
        var u = U.GetValue(context);
        var c = Curve.GetValue(context);
            
        CurveOutput.Value = c;
            
        Result.DirtyFlag.Clear();
        CurveOutput.DirtyFlag.Clear();
            
        if (c == null)
            return;

        Result.Value = (float)c.GetSampledValue(u);
            
    }

    [Input(Guid = "108CB829-5F9E-4A45-BC6B-7CF40A0A0F89")]
    public readonly InputSlot<Curve> Curve = new();

    [Input(Guid = "2c24d4fe-6c96-4502-bf76-dac756a16215")]
    public readonly InputSlot<float> U = new();
}