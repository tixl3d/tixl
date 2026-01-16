using T3.Core.Utils;

namespace Lib.numbers.vec2;

[Guid("5454ce3e-8dc4-4058-9a17-6300d51c142f")]
internal sealed class PadVec2Range : Instance<PadVec2Range>
{
    [Output(Guid = "fada5053-8424-4b18-ae65-9f12416bd65f")]
    public readonly Slot<Vector2> Result = new();

    public PadVec2Range()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var range = A.GetValue(context);
        
        var min = range.X;
        var max = range.Y;

        var guaranteedRange = GuaranteedRange.GetValue(context);
        if (guaranteedRange != Vector2.Zero)
        {
            min = MathF.Min(min,guaranteedRange.X);
            max = MathF.Max(max,guaranteedRange.Y);
        }
        
        var u = UniformScale.GetValue(context);
        var minExtend = ClampMinExtend.GetValue(context);
        var center = (min + max) * 0.5f;

        Result.Value = new Vector2(center + MathF.Min((min - center) * u, -minExtend),
                                   center + MathF.Max((max - center) * u, minExtend));
    }

    [Input(Guid = "ae2368fa-e36c-40b2-b6f2-94d5cc95d3e2")]
    public readonly InputSlot<Vector2> A = new();

    [Input(Guid = "5b56faab-7a94-4026-8089-a80db8ff2d9e")]
    public readonly InputSlot<float> UniformScale = new();

    [Input(Guid = "E7307B14-2702-4222-9DD6-7C39DC6DAB8E")]
    public readonly InputSlot<Vector2> GuaranteedRange = new();
    
    [Input(Guid = "D7DCBC7A-146D-42F0-84B1-A7D42F662487")]
    public readonly InputSlot<float> ClampMinExtend = new();
}