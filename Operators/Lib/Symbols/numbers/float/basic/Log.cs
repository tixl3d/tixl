namespace Lib.numbers.@float.basic;

[Guid("ffbf3237-8f31-4d19-bc21-88f792a7df3e")]
internal sealed class Log : Instance<Log>
{
    [Output(Guid = "37256f00-8f0e-4252-928e-4e9264cbd9df")]
    public readonly Slot<float> Result = new();

    public Log()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var newBase = Base.GetValue(context);
        Result.Value = (float)Math.Log(v,newBase);
    }

    [Input(Guid = "d3a0a2fd-e108-4636-a375-092701d58e19")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "328f7f75-0414-40f9-ba21-8313838a8bd5")]
    public readonly InputSlot<float> Base = new();
}