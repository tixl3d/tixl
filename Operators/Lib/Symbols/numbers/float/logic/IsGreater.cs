namespace Lib.numbers.@float.logic;

[Guid("52c92cd8-241e-4d79-aebc-b60b092f7941")]
internal sealed class IsGreater : Instance<IsGreater>
{
    [Output(Guid = "67e68f72-9bcb-4012-91f3-47d16a8efbaf")]
    public readonly Slot<bool> Result = new();
        

    public IsGreater()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var t = Threshold.GetValue(context);
            
        var result = v > t;

        if (result == _lastResult)
            return;

        Result.Value = result;
        _lastResult = result;
    }

    private bool _lastResult;

    [Input(Guid = "0cca00d1-ebad-4bef-9d87-b40be2568b61")]
    public readonly InputSlot<float> Value = new();
        
    [Input(Guid = "0FED5B94-0284-419D-A53A-0600B3B9B62D")]
    public readonly InputSlot<float> Threshold = new();

}