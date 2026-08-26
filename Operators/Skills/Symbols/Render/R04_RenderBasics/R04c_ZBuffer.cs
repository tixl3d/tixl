namespace Skills.Render.R04_RenderBasics;

[Guid("f8935a0d-bd26-448b-84a3-d0347e47ac1d")]
internal sealed class R04c_ZBuffer :Instance<R04c_ZBuffer>{
    [Output(Guid = "26a79101-5247-46f8-81ed-84a0a7dc3705")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}