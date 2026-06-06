using T3.Core.Utils;

namespace Lib.numbers.vec2;

[Guid("54af403f-a4bd-4ef0-95df-2201c0d3ba56")]
internal sealed class Vec2JitterOffset :Instance<Vec2JitterOffset>{
    [Output(Guid = "393553be-3985-4c00-9b0a-51e8374da078")]
    public readonly Slot<Vector2> Result = new();

    public Vec2JitterOffset()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = GetJitterOffset(
            Seed.GetValue(context), 
            SampleCount.GetValue(context));
    }
    
    /// <summary>
    /// Sub-pixel jitter offset in pixels, range [-0.5, 0.5], from a Halton(2,3) sequence.
    /// sampleCount 8 or 16 are typical. Predictable and repeating, per your spec.
    /// </summary>
    public static Vector2 GetJitterOffset(int frameIndex, int sampleCount)
    {
        var i = (frameIndex % sampleCount.ClampMin(1)) + 1; // Halton is undefined at index 0
        return new Vector2(Halton(i, 2) - 0.5f, Halton(i, 3) - 0.5f);
    }
    
    private static float Halton(int index, int radix)
    {
        var result = 0f;
        var fraction = 1f / radix;
        while (index > 0)
        {
            result += (index % radix) * fraction;
            index /= radix;
            fraction /= radix;
        }
        return result;
    }
    
    
    [Input(Guid = "0763898d-5f9b-44eb-9a7f-9e0ec62222ef")]
    public readonly InputSlot<int> Seed = new();
    
    [Input(Guid = "0A421D3C-0A72-429B-B028-32B4EFF26877")]
    public readonly InputSlot<int> SampleCount = new();

}