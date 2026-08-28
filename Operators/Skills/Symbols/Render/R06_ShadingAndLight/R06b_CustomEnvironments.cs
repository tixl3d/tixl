namespace Skills.Render.R06_ShadingAndLight;

[Guid("9c27c938-5bf5-4d64-8673-17b5152ba227")]
internal sealed class R06b_CustomEnvironments :Instance<R06b_CustomEnvironments>{
    [Output(Guid = "a093657d-fc68-4984-a2ce-01cdd7e1705a")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}