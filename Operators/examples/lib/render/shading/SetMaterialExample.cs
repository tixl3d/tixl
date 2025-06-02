namespace Examples.Lib.render.shading;

[Guid("5b86e841-548d-4dbd-a39b-6361e28e23f5")]
 internal sealed class SetMaterialExample : Instance<SetMaterialExample>
{
    [Output(Guid = "a945d055-790b-4cca-856f-300850a6634e")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}