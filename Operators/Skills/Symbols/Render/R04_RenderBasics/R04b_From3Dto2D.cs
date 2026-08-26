namespace Skills.Render.R04_RenderBasics;

[Guid("59333bff-6604-4e02-bcc5-05bfc2c68082")]
internal sealed class R04b_From3Dto2D :Instance<R04b_From3Dto2D>{
    [Output(Guid = "91126863-cdc0-40df-9397-205aee5d8c6d")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}