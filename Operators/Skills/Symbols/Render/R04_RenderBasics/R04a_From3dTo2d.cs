namespace Skills.Render.R04_RenderBasics;

[Guid("3d9d4460-83de-45ef-a60f-314e9bae8e8e")]
internal sealed class R04a_From3dTo2d :Instance<R04a_From3dTo2d>{
    [Output(Guid = "616b51c6-704a-4b1a-a640-0eed7d9a6a42")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}