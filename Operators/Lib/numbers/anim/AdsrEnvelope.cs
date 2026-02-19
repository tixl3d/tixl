using System;
using System.Numerics;
using T3.Core.Audio;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using T3.Core.Utils;

namespace Lib.numbers.anim;

/// <summary>
/// Applies an ADSR envelope to modulate a float value.
/// The envelope output (0-1) is mapped to the specified Min/Max range.
/// 
/// Example uses:
/// - Modulate brightness/opacity
/// - Animate size/scale on trigger
/// - Control audio volume with shaped envelope
/// - Create smooth parameter transitions
/// </summary>
[Guid("c8d9e0f1-a2b3-4c5d-6e7f-8a9b0c1d2e3f")]
internal sealed class AdsrEnvelope : Instance<AdsrEnvelope>
{
    [Input(Guid = "d9e0f1a2-b3c4-4d5e-6f7a-8b9c0d1e2f3a")]
    public readonly InputSlot<bool> Gate = new();

    [Input(Guid = "e0f1a2b3-c4d5-4e6f-7a8b-9c0d1e2f3a4b")]
    public readonly InputSlot<float> Duration = new();

    [Input(Guid = "f1a2b3c4-d5e6-4f7a-8b9c-0d1e2f3a4b5c", MappedType = typeof(AdsrCalculator.TriggerMode))]
    public readonly InputSlot<int> Mode = new();

    // ADSR Envelope as Vector4: X=Attack, Y=Decay, Z=Sustain, W=Release
    [Input(Guid = "a2b3c4d5-e6f7-4a8b-9c0d-1e2f3a4b5c6d")]
    public readonly InputSlot<Vector4> Envelope = new();

    [Input(Guid = "b3c4d5e6-f7a8-4b9c-0d1e-2f3a4b5c6d7e")]
    public readonly InputSlot<float> Min = new();

    [Input(Guid = "c4d5e6f7-a8b9-4c0d-1e2f-3a4b5c6d7e8f")]
    public readonly InputSlot<float> Max = new();

    [Output(Guid = "d5e6f7a8-b9c0-4d1e-2f3a-4b5c6d7e8f9a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    [Output(Guid = "f7a8b9c0-d1e2-4f3a-4b5c-6d7e8f9a0b1c", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> IsActive = new();

    public AdsrEnvelope()
    {
        Result.UpdateAction += Update;
        IsActive.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var gate = Gate.GetValue(context);
        var duration = Duration.GetValue(context);
        var mode = (AdsrCalculator.TriggerMode)Mode.GetValue(context);
        var envelope = Envelope.GetValue(context);
        var min = Min.GetValue(context);
        var max = Max.GetValue(context);

        // Extract ADSR from Vector4
        var attack = envelope.X > 0 ? envelope.X : 0.01f;
        var decay = envelope.Y > 0 ? envelope.Y : 0.1f;
        var sustain = envelope.Z >= 0 ? Math.Clamp(envelope.Z, 0f, 1f) : 0.7f;
        var release = envelope.W > 0 ? envelope.W : 0.3f;

        _calculator.Update(gate, context.LocalFxTime, attack, decay, sustain, release, mode, duration);

        // Map envelope to output range
        Result.Value = MathUtils.Lerp(min, max, _calculator.Value);
        IsActive.Value = _calculator.IsActive;
    }

    private readonly AdsrCalculator _calculator = new();
}
