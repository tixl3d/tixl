namespace Lib.numbers.vec4;

[Guid("82a5f040-926b-4970-9da2-aa36de94b436")]
internal sealed class RgbaToColor : Instance<RgbaToColor>
{
    [Output(Guid = "ce1c3293-99ed-4309-b040-92931ee706d1")]
    public readonly Slot<Vector4> Result = new();

    public RgbaToColor()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = new Vector4(R.GetValue(context), G.GetValue(context), B.GetValue(context), A.GetValue(context));
    }
        
    [Input(Guid = "5f2de1d3-6803-4f56-aaf1-9e29b4d81eb8")]
    public readonly InputSlot<float> R = new();

    [Input(Guid = "0742e707-1557-4f66-98d9-3f277416b7a3")]
    public readonly InputSlot<float> G = new();
        
    [Input(Guid = "80bc1511-7210-4710-a45f-ac961f1d672d")]
    public readonly InputSlot<float> B = new();
        
    [Input(Guid = "f2fd304f-11e7-466f-b594-b1c3af901852")]
    public readonly InputSlot<float> A = new();
}