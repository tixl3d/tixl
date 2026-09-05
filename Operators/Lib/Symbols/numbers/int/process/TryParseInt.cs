namespace Lib.numbers.@int.process;

[Guid("f833d070-97d7-4c2b-974a-fd116c88ec38")]
internal sealed class TryParseInt : Instance<TryParseInt>
{
    [Output(Guid = "b494af0a-0010-4729-8b7e-cb9e8922545a")]
    public readonly Slot<int> Result = new();

    [Input(Guid = "f8ce4dca-088f-48de-8613-5b83caeb8c2f")]
    public readonly InputSlot<string> String = new InputSlot<string>();

    [Input(Guid = "45002dd0-6035-4126-847f-d588b31389c5")]
    public readonly InputSlot<int> Default = new InputSlot<int>();

    public TryParseInt()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
            
        if (int.TryParse(String.GetValue(context), result: out var result))
        {
            Result.Value = result;
        }
        else 
        {
            Result.Value = Default.GetValue(context);
        }
    }
}