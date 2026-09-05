namespace Lib.render._dx11.api;

[Guid("eb68addb-ec59-416f-8608-ff9d2319f3a3")]
internal sealed class CalcDispatchCount : Instance<CalcDispatchCount>
{
    [Output(Guid = "35c0e513-812f-49e2-96fa-17541751c19b")]
    public readonly Slot<Int3> DispatchCount = new();

    public CalcDispatchCount()
    {
        DispatchCount.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        int count = Count.GetValue(context);
        Int3 groupSize = ThreadGroupSize.GetValue(context);
        DispatchCount.Value = (groupSize.X > 0) ? new Int3(count / groupSize.X+1, 1, 1) : Int3.Zero;
    }

    [Input(Guid = "3979e440-7888-4249-9975-74b21c6b813c")]
    public readonly InputSlot<Int3> ThreadGroupSize = new();

    [Input(Guid = "f79ccc37-05fd-4f81-97d6-6c1cafca180c")]
    public readonly InputSlot<int> Count = new();
}