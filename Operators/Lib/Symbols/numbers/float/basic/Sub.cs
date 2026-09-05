namespace Lib.numbers.@float.basic;

[Guid("3ba13faf-ea80-44ec-948a-02ed3d653a20")]
internal sealed class Sub : Instance<Sub>
{
    [Output(Guid = "eb4fce05-0667-43fe-a7bb-4fb21fe891bc")]
    public readonly Slot<float> Result = new();

    public Sub()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = Input1.GetValue(context) - Input2.GetValue(context);
    }


    [Input(Guid = "4889e720-e47c-4617-8353-06acf0af5283")]
    public readonly InputSlot<float> Input1 = new();

    [Input(Guid = "49ea5e01-cc8f-47a0-8988-3de2adb1805c")]
    public readonly InputSlot<float> Input2 = new();
}