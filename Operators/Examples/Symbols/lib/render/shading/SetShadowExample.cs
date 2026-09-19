namespace Examples.Lib.render.shading;

[Guid("1f73d45d-fdbf-4429-8e17-741285a050f5")]
public class SetShadowExample : Instance<SetShadowExample>
{
    [Output(Guid = "302055d3-17af-4393-b85f-d792866a1f79")]
    public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();
}