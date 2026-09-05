using T3.Core.Utils;

namespace Lib.render.basic;

[Guid("d14233a2-8240-47d3-bb92-0854f6d3b218")]
internal sealed class DustParticles : Instance<DustParticles>
{
    [Output(Guid = "846bc771-7367-48e2-8389-206e2ee5c063")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "f084ae44-d69a-48c3-936b-b89e310ca185")]
    public readonly InputSlot<bool> Refresh = new InputSlot<bool>();

    [Input(Guid = "df0a84c2-a1e9-492f-903a-5ef7511c6ec8")]
    public readonly InputSlot<int> Count = new InputSlot<int>();

    [Input(Guid = "c42d8124-d656-4b52-864f-9c9d888e0c54")]
    public readonly InputSlot<float> WrapSize = new InputSlot<float>();

    [Input(Guid = "e3a995dc-ec77-439c-8aed-e47df5e2b923")]
    public readonly InputSlot<Vector3> OffsetVolume = new InputSlot<Vector3>();

    [Input(Guid = "e92432c8-6ceb-494b-9cf4-b21191567b29")]
    public readonly InputSlot<Vector4> Color = new InputSlot<Vector4>();

    [Input(Guid = "70f0f867-a4de-44da-b8e3-a7e95e1740a3")]
    public readonly InputSlot<float> Size = new InputSlot<float>();

    [Input(Guid = "9ef1f3f7-da31-408b-b563-7d6c089278c7")]
    public readonly InputSlot<Vector3> Gravity = new InputSlot<Vector3>();

    [Input(Guid = "0744e03b-9df3-4a5e-9cfa-f522d9d946ea")]
    public readonly InputSlot<float> NoiseAmount = new InputSlot<float>();

    [Input(Guid = "10fa40ee-c6f6-4f6b-bf70-54eeb4c11891")]
    public readonly InputSlot<float> NoiseFrequency = new InputSlot<float>();

    [Input(Guid = "3783617d-17d5-4052-b732-60c61948ca1f")]
    public readonly InputSlot<float> NoiseVariation = new InputSlot<float>();

    [Input(Guid = "cee8f890-4ea3-4f58-9940-156c93c7bc1f")]
    public readonly InputSlot<float> Phase = new InputSlot<float>();

    [Input(Guid = "bafd96cc-c886-4392-8f57-592cb8651181")]
    public readonly InputSlot<int> TrailLength = new InputSlot<int>();

    [Input(Guid = "fd8340d5-b604-4605-9c3a-a57477d85919")]
    public readonly InputSlot<Vector4> TrailColor = new InputSlot<Vector4>();

    [Input(Guid = "31cfdca2-7f14-4c88-93ca-6f160829ca78")]
    public readonly InputSlot<float> TrailWidth = new InputSlot<float>();

    [Input(Guid = "49337c9b-7016-4093-b695-f7d1dd948fa1")]
    public readonly InputSlot<float> FadeNearest = new InputSlot<float>();

    [Input(Guid = "60b17724-e469-463f-9700-745b4d45d6ff")]
    public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

    [Input(Guid = "a8f0a422-4846-48ad-8756-9d542b3141d3", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new InputSlot<int>();

    [Input(Guid = "9b42caa4-040a-4f83-8822-bc5bfbdff334")]
    public readonly InputSlot<Texture2D> Texture_ = new InputSlot<Texture2D>();


}