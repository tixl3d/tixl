namespace Skills.Render.R03_Camera;

[Guid("8b869a9e-1bf6-4291-b310-7ddc0a523e86")]
internal sealed class R03g_SceneinaScene :Instance<R03g_SceneinaScene>{
    [Output(Guid = "18ca5aa4-92e6-4bde-88a3-21127c023020")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}