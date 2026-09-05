namespace Lib.numbers.int2.basic;

[Guid("40f0b1c9-9f87-489c-a6b9-ff9a5bd263ec")]
internal sealed class AddInt2 : Instance<AddInt2>
{
    [Output(Guid = "951EE7B3-ABC2-47EE-93A6-717C624D49E2")]
    public readonly Slot<Int2> Result = new();


    public AddInt2()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var s1 = Input1.GetValue(context);
        var s2 = Input2.GetValue(context);
        Result.Value = new Int2(s1.Width + s2.Width, s1.Height + s2.Height);
    }
        
    [Input(Guid = "C14E4756-C8CC-42FC-AF70-3473D9C6C861")]
    public readonly InputSlot<Int2> Input1 = new();

    [Input(Guid = "9B56CA60-D55C-44FF-BBBB-726D4BEB60A8")]
    public readonly InputSlot<Int2> Input2 = new();

}