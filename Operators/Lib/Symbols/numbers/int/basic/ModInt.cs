namespace Lib.numbers.@int.basic;

[Guid("cf3268d7-4f3d-47bd-8cb5-0214c75432ec")]
internal sealed class ModInt : Instance<ModInt>
{
    [Output(Guid = "8FED46E4-B9DF-4D56-B098-9C9A17775139")]
    public readonly Slot<int> Result = new();

    public ModInt()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var mod = Mod.GetValue(context);
        if (mod == 0)
            return;
            
        Result.Value = v % mod;
    }
        
    [Input(Guid = "3528F4D3-3529-4551-9DC1-E1DAFE6B0669")]
    public readonly InputSlot<int> Value = new();

    [Input(Guid = "CCDEA113-C046-4EC2-B1FB-30D6E15DB7CE")]
    public readonly InputSlot<int> Mod = new();
}