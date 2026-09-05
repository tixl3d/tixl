namespace Lib.numbers.anim.animators;

[Guid("8a6ab5ec-caa6-4baa-a9d1-2079af22685c")]
internal sealed class OscillateVec3 : Instance<OscillateVec3>
{
    [Output(Guid = "525D6B20-9779-46FD-AD43-8D89E35BF405", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Result = new();

    public OscillateVec3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var t = OverrideTime.HasInputConnections
                    ? OverrideTime.GetValue(context)
                    : (float)context.LocalFxTime * SpeedFactor.GetValue(context);

        //var value = Value.GetValue(context);
        var amplitude = Amplitude.GetValue(context);
        var period = Period.GetValue(context);
        var offset = Offset.GetValue(context);
        var phase = Phase.GetValue(context);
        var amplitudeScale = AmplitudeScale.GetValue(context);

        Result.Value = new Vector3(
                                   (float)Math.Sin(t / period.X + phase.X) * amplitude.X * amplitudeScale + offset.X,
                                   (float)Math.Sin(t / period.Y + phase.Y) * amplitude.Y * amplitudeScale + offset.Y,
                                   (float)Math.Sin(t / period.Z + phase.Z) * amplitude.Z * amplitudeScale + offset.Z
                                  );
    }

    [Input(Guid = "9A54D73F-0CD4-4BA2-AFC2-8E8BC156C6BA")]
    public readonly InputSlot<float> SpeedFactor = new();

    [Input(Guid = "AFCF8B5A-11FA-42E6-8DA3-09457C950EE7")]
    public readonly InputSlot<float> OverrideTime = new();

    [Input(Guid = "4C94A39E-88D9-4B7F-BBBF-42E80F9627EF")]
    public readonly InputSlot<Vector3> Amplitude = new();

    [Input(Guid = "9505B4AA-0EE1-4D0A-B215-F17194C71BE4")]
    public readonly InputSlot<float> AmplitudeScale = new();

    [Input(Guid = "8CE12440-49A0-4B0B-AFEA-BFBBAA4F899D")]
    public readonly InputSlot<Vector3> Period = new();

    [Input(Guid = "B3B06A36-EA91-4B26-88AC-DEDE32123EE6")]
    public readonly InputSlot<Vector3> Phase = new();

    [Input(Guid = "0C53C29E-7E33-40B3-8825-945E55C84AD5")]
    public readonly InputSlot<Vector3> Offset = new();
}