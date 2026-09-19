namespace Examples.Lib.render.camera;

[Guid("5317ade3-d4df-480d-872d-a17c63909da0")]
 internal sealed class CameraExample : Instance<CameraExample>
{
    [Output(Guid = "0c2f45b5-7591-45a6-9861-ce2575444dd4")]
    public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


}