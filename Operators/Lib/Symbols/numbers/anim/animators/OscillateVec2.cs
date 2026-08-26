namespace Lib.numbers.anim.animators;

[Guid("49796f63-27a8-4714-ba69-2073488ca833")]
internal sealed class OscillateVec2 : Instance<OscillateVec2>
{
    [Output(Guid = "9DF7498C-543F-46F3-B331-AE8F143A2A65")]
    public readonly Slot<Vector2> Result = new();
        
    public OscillateVec2()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        //var t = (float)context.Playback.BeatTime * SpeedFactor.GetValue(context);

        var t = OverrideTime.HasInputConnections
                    ? OverrideTime.GetValue(context)
                    : (float)context.LocalFxTime * SpeedFactor.GetValue(context);
            
        //var value = Value.GetValue(context);
        var amplitude = Amplitude.GetValue(context);
        var period = Period.GetValue(context);
        var offset = Offset.GetValue(context);
        var phase = Phase.GetValue(context);
        var amplitudeScale = AmplitudeScale.GetValue(context);
            
        Result.Value = new Vector2(
                                   (float)Math.Sin(t / period.X + phase.X) * amplitude.X * amplitudeScale + offset.X,
                                   (float)Math.Sin(t / period.Y + phase.Y) * amplitude.Y * amplitudeScale + offset.Y
                                  );
    }

        [Input(Guid = "f82759c6-154d-41cb-97a8-8b1eea635f6b")]
        public readonly InputSlot<float> OverrideTime = new InputSlot<float>();

        [Input(Guid = "2c812efc-de0b-4263-a651-2966c596fe76")]
        public readonly InputSlot<float> SpeedFactor = new InputSlot<float>();

        [Input(Guid = "2C9A6B15-AC0D-4708-A6C8-2834CFC3086C")]
        public readonly InputSlot<System.Numerics.Vector2> Amplitude = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "031BF887-E45E-45AA-BA17-214AECF155DA")]
        public readonly InputSlot<float> AmplitudeScale = new InputSlot<float>();

        [Input(Guid = "4959c4c7-e216-4c3d-9b51-228fe4a0b0f9")]
        public readonly InputSlot<System.Numerics.Vector2> Period = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "48a94f9e-32fc-46d0-9e06-8f7cbe1d40f3")]
        public readonly InputSlot<System.Numerics.Vector2> Phase = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "63c0681d-eb5a-45bb-b0df-be868e236c1e")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();
}