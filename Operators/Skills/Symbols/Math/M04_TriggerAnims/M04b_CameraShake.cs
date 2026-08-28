namespace Skills.Math.M04_TriggerAnims;

[Guid("c24dd182-0c86-4e0f-9222-b2d12c892aa3")]
internal sealed class M04b_CameraShake :Instance<M04b_CameraShake>{
    [Output(Guid = "9dc29f39-e2cf-4d03-8fbe-7ac123d41cbb")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}