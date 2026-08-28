namespace Lib.image.fx.stylize;

[Guid("6820b166-1782-43b9-bc5c-6b4f63b16f86")]
internal sealed class FakeLight : Instance<FakeLight>
{
    [Output(Guid = "27e1e1b6-89e0-4dca-98e1-5989286f6331")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "00c53b57-7347-4ebc-97d7-1ab48483f09b")]
    public readonly InputSlot<Texture2D> HeightMap = new InputSlot<Texture2D>();

    [Input(Guid = "56eccacb-65ac-4813-ad7e-fad8e581f570")]
    public readonly InputSlot<Texture2D> LightMap = new InputSlot<Texture2D>();

    [Input(Guid = "767ddbe0-202f-4d0b-9aa1-9a22d61a2d40")]
    public readonly InputSlot<float> Amount = new InputSlot<float>();

    [Input(Guid = "0212bfb2-9f5f-4d60-aab0-3f9525bd7bfc")]
    public readonly InputSlot<float> Specularity = new InputSlot<float>();

    [Input(Guid = "4aa128ab-d0a8-42d5-800f-6992959bd0cf")]
    public readonly InputSlot<float> Shade = new InputSlot<float>();

    [Input(Guid = "7f0c127b-ee60-44c8-8490-2d3599cde4a2")]
    public readonly InputSlot<float> Twist = new InputSlot<float>();

    [Input(Guid = "03298545-a5d6-44d5-bb7c-4747172d2667")]
    public readonly InputSlot<Vector2> Direction = new InputSlot<Vector2>();

    [Input(Guid = "9d5e3055-c17e-4013-963d-e17c76b707c1")]
    public readonly InputSlot<Vector4> HighlightColor = new InputSlot<Vector4>();

    [Input(Guid = "9c9e9e49-2f3a-4746-ac1f-c5fb1b3dd96b")]
    public readonly InputSlot<Vector4> MidColor = new InputSlot<Vector4>();

    [Input(Guid = "d162a7e1-1906-45bc-a896-10ee2c2483fe")]
    public readonly InputSlot<float> BlurRadius = new InputSlot<float>();

    [Input(Guid = "f93db0c6-c5ed-40da-9677-0c284618f5bb")]
    public readonly InputSlot<float> SampleRadius = new InputSlot<float>();

    [Input(Guid = "3c615aa0-61b7-43c5-bea3-8d1110b4f5cd")]
    public readonly InputSlot<Int2> Resolution = new InputSlot<Int2>();

}