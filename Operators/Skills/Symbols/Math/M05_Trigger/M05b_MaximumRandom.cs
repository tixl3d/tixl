namespace Skills.Math.M05_Trigger;

[Guid("78b2714d-7625-4b3d-a499-faeb45d27b1d")]
internal sealed class M05b_MaximumRandom :Instance<M05b_MaximumRandom>{
    [Output(Guid = "68562c1d-0f69-49f2-9c31-32c3e5e971f6")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}