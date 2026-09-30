namespace Examples.Lib.image.generate;

[Guid("7d7c9abb-1742-407e-85c7-ba4f6e87f390")]
 internal sealed class RenderTargetExample : Instance<RenderTargetExample>
{
    [Output(Guid = "d3295b50-3343-456c-bae1-8c1351b4f875")]
    public readonly Slot<Texture2D> Output = new();


}