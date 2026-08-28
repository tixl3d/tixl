namespace Skills.Math.M05_Trigger;

[Guid("ce6dadc1-ace3-49a2-84b8-453d70688dbb")]
internal sealed class M05a_TriggerEvents :Instance<M05a_TriggerEvents>{
    [Output(Guid = "8a8080a1-aa17-47b2-a112-b690ac7ad2a5")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}