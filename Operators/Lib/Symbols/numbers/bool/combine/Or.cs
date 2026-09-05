namespace Lib.numbers.@bool.combine;

[Guid("9786dec1-b0fc-49d7-bf79-c9a1d457f386")]
internal sealed class Or : Instance<Or>
{
    [Output(Guid = "FC297EE7-2B25-4C34-98A4-3F2058040FF7")]
    public readonly Slot<bool> Result = new();

    public Or()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // evaluate both to clear dirty flag
        var a = A.GetValue(context);
        var b = B.GetValue(context);

        var resultValue = a || b;
        Result.Value = resultValue;
    }
        
    [Input(Guid = "50ED5538-8134-4D8D-AEDC-61F19F60446E")]
    public readonly InputSlot<bool> A = new();

    [Input(Guid = "95CE0046-DBAD-444E-BD31-0E34F00A62B9")]
    public readonly InputSlot<bool> B = new();
}