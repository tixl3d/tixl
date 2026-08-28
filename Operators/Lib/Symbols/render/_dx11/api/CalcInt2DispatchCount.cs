namespace Lib.render._dx11.api;

[Guid("cc11774e-82dd-409f-97fb-5be3f2746f9d")]
internal sealed class CalcInt2DispatchCount : Instance<CalcInt2DispatchCount>
{
    [Output(Guid = "3b0f7d82-3254-4b4d-baea-bc9aa003768a")]
    public readonly Slot<Int3> DispatchCount = new();

    public CalcInt2DispatchCount()
    {
        DispatchCount.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Int2 size = Size.GetValue(context);
        Int3 threadGroups = ThreadGroups.GetValue(context);
        if (threadGroups.X == 0 || threadGroups.Y == 0)
            return;

        DispatchCount.Value = new Int3(size.Width / threadGroups.X + 1, 
                                       size.Height / threadGroups.Y + 1, 1);
    }

    [Input(Guid = "71fe6847-b8e3-4cc7-895c-b10db0136e1c")]
    public readonly InputSlot<Int3> ThreadGroups = new();
    
    [Input(Guid = "714e7c0d-0137-4bc6-9e5b-93386b2efe13")]
    public readonly InputSlot<Int2> Size = new();

}