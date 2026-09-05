namespace Lib.numbers.vec4;

[Guid("0acc2bdb-f8e6-4280-aa89-702f1cb7722b")]
internal sealed class DotVec4 : Instance<DotVec4>
{
    [Input(Guid = "cf1ec3c4-8b42-4c12-ba10-ca1db88c1a97")]
    public readonly InputSlot<Vector4> Input1 = new();
    
    [Input(Guid = "063073d6-7ef7-4409-99b7-68f5a40a0d83")]
    public readonly InputSlot<Vector4> Input2 = new();

    [Output(Guid = "498882f3-3e13-43dd-9758-962c3f3d1408")]
    public readonly Slot<float> Result = new();

    public DotVec4()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = Vector4.Dot(Input1.GetValue(context), Input2.GetValue(context));
    }
}