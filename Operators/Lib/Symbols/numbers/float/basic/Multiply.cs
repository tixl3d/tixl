namespace Lib.numbers.@float.basic;

[Guid("17b60044-9125-4961-8a79-ca94697b3726")]
internal sealed class Multiply : Instance<Multiply>
{
    [Output(Guid = "e011dd8c-1b9c-458f-8960-e6c38e83ca74")]
    public readonly Slot<float> Result = new();

    public Multiply()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = A.GetValue(context) * B.GetValue(context);
    }


    [Input(Guid = "372288fa-3794-47ba-9f91-59240513217a")]
    public readonly InputSlot<float> A = new();

    [Input(Guid = "5ae4bb07-4214-4ec3-a499-24d9f6d404a5")]
    public readonly InputSlot<float> B = new();
}