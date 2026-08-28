using T3.Core.Utils;

namespace Lib.numbers.vec3;

[Guid("50aab941-0a29-474a-affd-13a74ea0c780")]
internal sealed class PerlinNoise3 : Instance<PerlinNoise3>
{
    [Output(Guid = "1666BC49-4DAE-4CB0-900B-80B50F913117")]
    public readonly Slot<Vector3> Result = new();

    public PerlinNoise3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var value = OverrideTime.HasInputConnections
                        ? OverrideTime.GetValue(context)
                        : (float)context.LocalFxTime;

        value += Phase.GetValue(context);
        
        var seed = Seed.GetValue(context);
        var period = Frequency.GetValue(context);
        var octaves = Octaves.GetValue(context);
        var rangeMin = RangeMin.GetValue(context);
        var rangeMax = RangeMax.GetValue(context);
        var scale = Amplitude.GetValue(context);
        var scaleXYZ = AmplitudeXYZ.GetValue(context);
        var biasAndGain = BiasAndGain.GetValue(context);
        var offset = Offset.GetValue(context);

        var scaleToUniformFactor = 1.37f;
        var vec = new Vector3(ScalarNoise(0), 
                              ScalarNoise(123), 
                              ScalarNoise(234));

        Result.Value = vec.Remap(Vector3.Zero, Vector3.One, rangeMin, rangeMax) * scaleXYZ * scale + offset; 
        return;

        float ScalarNoise(int seedOffset)
        {
            return (MathUtils.PerlinNoise(value,period, octaves, seed + seedOffset) * scaleToUniformFactor + 1f) 
                   * 0.5f.ApplyGainAndBias(biasAndGain.X, biasAndGain.Y);
        }

        // Vector3 Remap3(Vector3 value3, Vector3 inMin, Vector3 inMax, Vector3 outMin, Vector3 outMax)
        // {
        //     var factor = (value3 - inMin) / (inMax - inMin);
        //     var v = factor * (outMax - outMin) + outMin;
        //     return v;
        // }
    }

    [Input(Guid = "deddfbee-386d-4f8f-9339-ec6c01908a11")]
    public readonly InputSlot<float> OverrideTime = new();

    [Input(Guid = "D8B83CA4-1F72-43DA-818D-0927F15B6EFB")]
    public readonly InputSlot<float> Phase = new();


    [Input(Guid = "03df41a8-3d72-47b1-b854-81e6e59e7cb4")]
    public readonly InputSlot<float> Frequency = new();

    [Input(Guid = "2693cb7d-33b3-4a0c-929f-e6911d2d4a0c")]
    public readonly InputSlot<int> Octaves = new();

    [Input(Guid = "C427D83B-1046-4B8D-B44A-E616A64A702A")]
    public readonly InputSlot<Vector3> AmplitudeXYZ = new();

    [Input(Guid = "E0F4333D-8BEE-4F9E-BB29-9F76BD72E61F")]
    public readonly InputSlot<float> Amplitude = new();

    [Input(Guid = "3065B319-7C6F-4447-A32A-454EE690BA36")]
    public readonly InputSlot<Vector3> Offset = new();

    [Input(Guid = "B4B38D87-F661-4B8B-B978-70BF34152422")]
    public readonly InputSlot<Vector3> RangeMin = new();

    [Input(Guid = "5401E715-7A82-43AB-BA16-D0E55A1D83D4")]
    public readonly InputSlot<Vector3> RangeMax = new();

    [Input(Guid = "B7B95FC2-C73F-4DB1-BDAB-2445FE62F032")]
    public readonly InputSlot<Vector2> BiasAndGain = new();

    [Input(Guid = "1cd2174e-aeb2-4258-8395-a9cc16f276b5")]
    public readonly InputSlot<int> Seed = new();
}