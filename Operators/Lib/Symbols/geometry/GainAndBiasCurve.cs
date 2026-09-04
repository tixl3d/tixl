using System;

namespace Lib.geometry;

/// <summary>
/// Produces a RemapCurve applying Schlick gain and bias to a 0..1 value.
/// X is gain, Y is bias; 0.5/0.5 is the identity. The math mirrors
/// ApplyGainAndBias() in shaders/shared/bias-functions.hlsl so CPU fields and
/// GPU ops remap identically.
/// </summary>
[Guid("aa1c7c11-62e6-4b1d-a7eb-a3fc1347f3c6")]
internal sealed class GainAndBiasCurve : Instance<GainAndBiasCurve>
{
    [Output(Guid = "6d14a0bb-5232-4e34-9104-329bbc0ad7f5")]
    public readonly Slot<RemapCurve> Curve = new();

    public GainAndBiasCurve()
    {
        Curve.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var gainAndBias = GainAndBias.GetValue(context);
        var gain = Math.Clamp(gainAndBias.X, 0.001f, 0.999f);
        var bias = Math.Clamp(gainAndBias.Y, 0.001f, 0.999f);

        Curve.Value = new RemapCurve(value =>
                                     {
                                         if (value > 0.9999f)
                                             return 1;

                                         if (value < 0.00001f)
                                             return 0;

                                         if (gain < 0.5f)
                                         {
                                             value = GetBias(bias, value);
                                             return GetSchlickBias(gain, value);
                                         }

                                         value = GetSchlickBias(gain, value);
                                         return GetBias(bias, value);
                                     });
    }

    private static float GetBias(float bias, float x)
    {
        return x / ((1f / bias - 2f) * (1f - x) + 1f);
    }

    private static float GetSchlickBias(float g, float x)
    {
        if (x < 0.5f)
            return 0.5f * GetBias(g, x * 2f);

        return 0.5f * GetBias(1f - g, x * 2f - 1f) + 0.5f;
    }

    [Input(Guid = "9a47a81a-bb15-4c2a-962b-0fb9f48afd94")]
    public readonly InputSlot<Vector2> GainAndBias = new();
}
