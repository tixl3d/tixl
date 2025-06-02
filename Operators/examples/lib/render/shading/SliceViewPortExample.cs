namespace Examples.Lib.render.shading;

[Guid("81337795-7e15-4335-a067-6d2c54a7b4b8")]
 internal sealed class SliceViewPortExample : Instance<SliceViewPortExample>
{
    [Output(Guid = "328c2eb7-80bf-4a64-aef0-fb0c8bb72ed0")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}