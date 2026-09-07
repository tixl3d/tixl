using System;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Core.Utils;

public static class AnimMath
{
    public static float GetSpeedOverrideFromContext(EvaluationContext context, InputSlot<int> allowSpeedFactor)
    {
        var speedFactors = (SpeedFactors)allowSpeedFactor.GetValue(context).Clamp(0, Enum.GetNames(typeof(SpeedFactors)).Length -1);
        float rateFactorFromContext = 1;
            
        switch (speedFactors)
        {
            case SpeedFactors.None:
                break;
                
            case SpeedFactors.FactorA:
            {
                if (!context.FloatVariables.TryGetValue(SpeedFactorA, out rateFactorFromContext))
                    rateFactorFromContext = 1;
                break;
            }
            case SpeedFactors.FactorB:
                if (!context.FloatVariables.TryGetValue(SpeedFactorB, out rateFactorFromContext))
                    rateFactorFromContext = 1;
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        return rateFactorFromContext;
    }
        
    public static float CalcValueForNormalizedTime(Shapes shape, double time, int componentIndex, float bias, float ratio)
    {
        float result = 0;
        switch (shape)
        {
            case Shapes.Ramps:
            case Shapes.Saws:
            case Shapes.Wave:
            case Shapes.Square:
            case Shapes.ZigZag:
            case Shapes.KickSaws:
                result = SchlickBias(_mapShapes[(int)shape](((float)MathUtils.Fmod(time, 1) / ratio).Clamp(0, 1)), bias);
                break;
                
            case Shapes.Sin:
                result = SchlickBiasWithNegative(_mapShapes[(int)shape](((float)MathUtils.Fmod(time, 1) / ratio).Clamp(0, 1)), bias);
                break;
                
            case Shapes.Random:
                result = SchlickBias((float)((double)MathUtils.XxHash((uint)(time + 28657 * componentIndex)) / uint.MaxValue), bias);
                break;
                
            case Shapes.RandomSigned:
                result = SchlickBias((float)((double)MathUtils.XxHash((uint)(time + 28657 * componentIndex)) / uint.MaxValue), bias) * 2 -1;
                break;
                
            case Shapes.Endless:
            {
                var fraction = (MathUtils.Fmod(time, 1) / ratio).Clamp(0,1);
                result =  (int)time + SchlickBias((float)fraction, bias);
                break;
            }

            case Shapes.PerlinNoise:
                result = SchlickBiasWithNegative(MathUtils.PerlinNoise((float)time, 1, 5, 43 * componentIndex) * 1.25f, bias) / 2 + 0.5f;
                break;
                
            case Shapes.PerlinNoiseSigned:
                result = SchlickBiasWithNegative(MathUtils.PerlinNoise((float)time, 1, 5, 43 * componentIndex) * 1.25f, bias);
                break;

            case Shapes.Steps:
                result =  (int)time;
                break;
        }
        return result;
    }        
        
    private static float SchlickBias(float x, float bias)
    {
        return x / ((1 / bias - 2) * (1 - x) + 1);
    }
        
    private static float SchlickBiasWithNegative(float xx, float bias)
    {
        var normalized = xx / 2 + 0.5f;
        var biased = normalized / ((1 / bias - 2) * (1 - normalized) + 1);
        return biased * 2 - 1;
    }        
        
    private delegate float MappingFunction(float fraction);
    private static readonly MappingFunction[] _mapShapes =
        {
            f => f, // 0: Endless
            f => f, // 1: Ramps
            f => 1 - MathUtils.Fmod(f, 1), // 2: Saw,
            f => f <= 0 ? 0 : (1 - f.Clamp(0, 1)), // 3: KickSaw (Saw resting at last value)
            f => f > 0.5f ? 1 : 0, // 4: Square
            f =>
            {
                var ff = MathUtils.Fmod(f, 1);
                return ff < 0.5f ? (ff * 2) : (1 - (ff - 0.5f) * 2);
            }, // 5: ZigZag,
            f => (float)Math.Sin((f + 0.25) * 2 * 3.141592f) / 2 + 0.5f, // 6: Wave                
            f => (float)Math.Sin((f + 0.25) * 2 * 3.141592f), // 7: Sin
            f => f, // 8: PerlinNoise
            f => f, // 9: Random
            f => f, // 10: Steps
        };
        
    public enum Shapes
    {
        Endless = 0,
        Ramps = 1,
        Saws = 2,
        KickSaws = 3,
        Square = 4,
        ZigZag = 5,
        Wave = 6,
        Sin = 7,
        PerlinNoise = 8,
        PerlinNoiseSigned = 9,
        Random = 10,
        RandomSigned = 11,
        Steps = 12,
    }

    private const string SpeedFactorA = "SpeedFactorA";
    private const string SpeedFactorB = "SpeedFactorB";
    public enum SpeedFactors
    {
        None,
        FactorA,
        FactorB,
    }
}