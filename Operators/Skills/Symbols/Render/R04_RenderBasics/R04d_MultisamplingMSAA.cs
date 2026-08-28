namespace Skills.Render.R04_RenderBasics;

[Guid("abb8caef-fba7-457c-9934-613d6476f99f")]
internal sealed class R04d_MultisamplingMSAA :Instance<R04d_MultisamplingMSAA>{
    [Output(Guid = "c05ffcf9-eb72-40c4-b553-d7e4e94c7146")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}