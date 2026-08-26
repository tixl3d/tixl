namespace Lib.numbers.@int.process;

[Guid("06b4728e-852c-491a-a89d-647f7e0b5415")]
internal sealed class FloatToInt : Instance<FloatToInt>
{
    [Output(Guid = "1EB7C5C4-0982-43F4-B14D-524571E3CDDA")]
    public readonly Slot<int> Integer = new();

    public FloatToInt()
    {
        Integer.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Integer.Value = (int)FloatValue.GetValue(context);
    }

    [Input(Guid = "AF866A6C-1AB0-43C0-9E8A-5D25C300E128")]
    public readonly InputSlot<float> FloatValue = new();
}