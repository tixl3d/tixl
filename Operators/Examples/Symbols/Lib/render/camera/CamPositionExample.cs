namespace Examples.Lib.render.camera;

[Guid("5fb11919-0eef-4cee-bddf-bf30e0a98ad9")]
internal sealed class CamPositionExample : Instance<CamPositionExample>
{
    [Output(Guid = "d18c45be-bb7e-4727-8de2-2c227529ed76")]
    public readonly Slot<Texture2D> ColorBuffer = new();
}