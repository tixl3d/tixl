namespace Lib.point.draw;

[Guid("37bdbafc-d14c-4b81-91c3-8f63c3b63812")]
internal sealed class VisualizePoints : Instance<VisualizePoints>
{
    [Output(Guid = "b0294b73-58a9-4d79-b3e2-caaed304109d", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "54fc4cd7-dfc3-4690-9fd1-2b555f7656d4")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "C85649DF-A235-49D6-A964-C69B299FB4B5")]
    public readonly InputSlot<GizmoVisibility> Visibility = new();

    [Input(Guid = "8f72275d-d903-4372-852c-51c3db35fe90")]
    public readonly InputSlot<bool> ShowCenterPoints = new();

    [Input(Guid = "d0ac63c5-639b-4b3c-b40b-348b76fa0fd2")]
    public readonly InputSlot<bool> ShowAxis = new();

    [Input(Guid = "d2472768-dd40-436f-af1b-7359289b5118")]
    public readonly InputSlot<bool> ShowIndices = new();

    [Input(Guid = "621bf2cf-8d49-4b5f-88b9-4460045e8914")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "b857b40b-2ca7-42a4-bebe-1cb11700ed71")]
    public readonly InputSlot<float> LineThickness = new();

    [Input(Guid = "c4332cb5-4dbc-4dd1-a738-cee8a3098c17")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "40a04de8-54aa-4f66-acea-80ffc4dab7bd")]
    public readonly InputSlot<float> PointSize = new();

    [Input(Guid = "f6942098-3f69-41fb-9228-96bd2ffb1cbf")]
    public readonly InputSlot<bool> UseWForSize = new();

    [Input(Guid = "bbc26907-416d-4168-9e89-72ee1c6a530e")]
    public readonly InputSlot<bool> ShowAttributeList = new();

    [Input(Guid = "90173b57-cd09-4270-a16e-6e2454882b9b")]
    public readonly InputSlot<int> StartIndex = new();

    [Input(Guid = "98fe7249-39ea-4f45-b045-36e07a8f2018")]
    public readonly InputSlot<bool> ShowVelocity = new();

    [Input(Guid = "08174efd-78e5-4552-b559-5aa7b1b8c33e")]
    public readonly InputSlot<bool> ShowSpritePlane = new();

    [Input(Guid = "bcdec771-bfae-4d1c-905e-f7817eab5eec")]
    public readonly InputSlot<float> SpriteSize = new();

    [Input(Guid = "7b2054d4-e6b5-43e3-9cbe-1d2073ae35aa")]
    public readonly InputSlot<bool> ShowXyRadius = new();

}