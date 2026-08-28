namespace Lib.image.color;


[Guid("a1e3b8d3-3888-4e18-80e4-f6c65a136e75")]
internal sealed class ColorGradeDepth : Instance<ColorGradeDepth>
{
    [Output(Guid = "9189d238-5196-41fb-8dc0-b6c4befe71a8")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "81749c4d-a01d-4279-86dd-fec14e265301")]
    public readonly InputSlot<Texture2D> Texture2d = new();

    [Input(Guid = "8f850308-21f4-41f4-865e-1a5f2bddb01e")]
    public readonly InputSlot<float> PreSaturate = new();

    [Input(Guid = "2f41d547-f23b-448c-b19d-c95cf8d3a3ea")]
    public readonly InputSlot<Vector4> Gain = new();

    [Input(Guid = "6a4eb09f-070a-4fd2-82f5-677b7cafb1dc")]
    public readonly InputSlot<Vector4> Gamma = new();

    [Input(Guid = "bfb058cb-3611-441a-840f-0a1c4efd4de1")]
    public readonly InputSlot<Vector4> Lift = new();

    [Input(Guid = "2e91b9e5-67eb-424f-be6f-3ed6ab77f3b8")]
    public readonly InputSlot<Vector4> VignetteColor = new();

    [Input(Guid = "686a23e0-bd6a-4662-85e8-e910315a4e84")]
    public readonly InputSlot<float> VignetteRadius = new();

    [Input(Guid = "11e8c433-deab-4979-9054-90dc373cb5f9")]
    public readonly InputSlot<float> VignetteFeather = new();

    [Input(Guid = "c3693fbf-83d1-4780-98a0-302e7ecf7ca2")]
    public readonly InputSlot<Vector2> VignetteCenter = new();

    [Input(Guid = "c0f721fb-f39a-4147-a288-db8546c240fd")]
    public readonly InputSlot<Texture2D> DepthBuffer = new();

    [Input(Guid = "92912c42-9928-495e-a8c7-c752e79eeba6")]
    public readonly InputSlot<Gradient> Gradient = new();

    [Input(Guid = "c36d2140-8f34-4172-ac77-8e48c40c01df")]
    public readonly InputSlot<Vector2> GradientDepthRange = new();

    [Input(Guid = "e104b69f-c9ca-4125-b99f-f85f48409aa0")]
    public readonly InputSlot<Vector2> CamNearFarClip = new();

}