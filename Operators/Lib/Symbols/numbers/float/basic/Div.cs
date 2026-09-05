namespace Lib.numbers.@float.basic;

[Guid("15fb88b2-81a1-43b8-97ba-41221293bb07")]
internal sealed class Div : Instance<Div>
{
    [Output(Guid = "866642e7-17dd-4375-9d5e-2e3747a554c2")]
    public readonly Slot<float> Result = new();

    public Div()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var b = B.GetValue(context);
        Result.Value = b== 0 
                           ? float.NaN 
                           : A.GetValue(context) / b;
    }


    [Input(Guid = "70460191-7573-400f-ba88-11878ecc917c")]
    public readonly InputSlot<float> A = new();

    [Input(Guid = "a79a2f16-7a4e-464d-8af4-3e3029ae853e")]
    public readonly InputSlot<float> B = new();
}