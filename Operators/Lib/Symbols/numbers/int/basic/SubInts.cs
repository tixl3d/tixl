namespace Lib.numbers.@int.basic;

[Guid("7934f11d-c95c-41ae-b282-6e568b9c5f30")]
internal sealed class SubInts : Instance<SubInts>
{
    [Output(Guid = "e66c9218-06dc-4e8d-9f30-9d81f22e39ba")]
    public readonly Slot<int> Result = new();

    public SubInts()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = Input1.GetValue(context) - Input2.GetValue(context);
    }


    [Input(Guid = "944152fb-3786-4474-90ea-c292b5dde801")]
    public readonly InputSlot<int> Input1 = new();

    [Input(Guid = "b1a34f4a-7be5-4c6b-84f1-0bb04cc80d0d")]
    public readonly InputSlot<int> Input2 = new();
}