namespace Lib.numbers.@float.trigonometry;

[Guid("61c70843-08ea-4249-ba90-9971493e45d1")]
internal sealed class Cos : Instance<Cos>
{
    [Output(Guid = "4480F970-9E51-456A-8D66-D501FCA2C15B")]
    public readonly Slot<float> Result = new();

    public Cos()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = (float)Math.Cos(Input.GetValue(context));
    }
        
    [Input(Guid = "9764EFB1-57A8-48DA-B82E-4DCC2C3CB10A")]
    public readonly InputSlot<float> Input = new();
}