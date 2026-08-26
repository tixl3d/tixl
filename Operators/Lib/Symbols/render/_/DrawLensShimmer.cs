namespace Lib.render.@_;

[Guid("c87b140b-1109-4eff-bf77-98bff3fc3e17")]
internal sealed class DrawLensShimmer : Instance<DrawLensShimmer>
{
    [Output(Guid = "e44490fc-bf15-4e36-8b81-4d7c45949dbc")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "04ea959f-9f20-4c77-80f4-ff1460b1a209", MappedType = typeof(Styles))]
    public readonly InputSlot<int> Style = new InputSlot<int>();

    [Input(Guid = "3ba2f274-43c5-4c2c-aaa3-474b3f178fbc")]
    public readonly InputSlot<float> Segments = new InputSlot<float>();

    [Input(Guid = "a2785b57-abcc-442e-90c7-cd120d3255ab")]
    public readonly InputSlot<float> SegmentFill = new InputSlot<float>();

    [Input(Guid = "338a0765-027c-4f49-a5de-38c1ab1035ef")]
    public readonly InputSlot<float> JitterWidths = new InputSlot<float>();

    [Input(Guid = "724d28d3-5b04-4e82-9d4f-01e7d42b9577")]
    public readonly InputSlot<float> JitterLength = new InputSlot<float>();

    [Input(Guid = "d0b327fa-f48a-44cd-aa01-161b1c61bdc1")]
    public readonly InputSlot<float> JitterBrightness = new InputSlot<float>();

    [Input(Guid = "f1561cc1-3d5a-4caf-899e-fc0c81eed15b")]
    public readonly InputSlot<float> JitterColors = new InputSlot<float>();

    [Input(Guid = "1191708c-9cdb-4331-8886-1742193f532e")]
    public readonly InputSlot<float> AnimationSpeed = new InputSlot<float>();

    [Input(Guid = "f9070783-1989-4fcd-9db1-f370f36be71e")]
    public readonly InputSlot<float> AnimationOffset = new InputSlot<float>();

    [Input(Guid = "66aeff41-c6af-4a06-9813-e526308968c2")]
    public readonly InputSlot<float> CircularCompletion = new InputSlot<float>();

    [Input(Guid = "647fcd91-534c-4433-a338-e1ac77ec5f2b")]
    public readonly InputSlot<float> CircularCompletionEdge = new InputSlot<float>();

    [Input(Guid = "a9e7091e-d0d3-414e-8c17-9f06cd568e24")]
    public readonly InputSlot<float> CompletionAffectsLength = new InputSlot<float>();

    [Input(Guid = "bab504e1-dce7-4264-b1c8-40ec14202305")]
    public readonly InputSlot<float> CoreBrightness = new InputSlot<float>();

    [Input(Guid = "793ce198-9956-4dc9-9a4b-393208143f49")]
    public readonly InputSlot<Gradient> ShimmerGradient = new InputSlot<Gradient>();

    [Input(Guid = "384bd5d5-55a4-4d0f-895b-b2e5cac51e95")]
    public readonly InputSlot<Gradient> SparkleGradient = new InputSlot<Gradient>();

    [Input(Guid = "7c6c2ad1-b7d5-4496-822e-1720356b59d7")]
    public readonly InputSlot<bool> MultiSampling4x = new InputSlot<bool>();

    [Input(Guid = "c1f2801c-7bda-4b55-943a-0a01d0e6ca2d")]
    public readonly MultiInputSlot<StructuredList> SpriteDefinition = new MultiInputSlot<StructuredList>();

    private enum Styles
    {
        Shimmer,
        Sparkle,
    }
}