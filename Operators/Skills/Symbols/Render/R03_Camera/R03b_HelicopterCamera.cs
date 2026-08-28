namespace Skills.Render.R03_Camera;

[Guid("dcce7259-dbe4-4733-ab8d-511c52ad8773")]
internal sealed class R03b_HelicopterCamera :Instance<R03b_HelicopterCamera>{
    [Output(Guid = "b76749a5-26a1-4ced-b2d0-d668e2627b83")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}