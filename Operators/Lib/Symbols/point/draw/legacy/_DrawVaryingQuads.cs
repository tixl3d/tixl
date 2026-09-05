using T3.Core.Utils;

namespace Lib.point.draw.legacy;

[Guid("ffc0a7ed-fe61-4188-8db9-0b0f07c6b981")]
internal sealed class _DrawVaryingQuads : Instance<_DrawVaryingQuads>
{
    [Output(Guid = "65bf6652-0187-4c5f-8e1f-ccc4254b843b")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "82230324-b1cd-41ba-8d03-933c939001ad")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "c25ab214-7fb3-4595-8740-96471df44905")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "b1a7da14-d6bd-4862-ad69-dfa7ae1cfbb8")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "6fb70409-1cdd-488d-b7bb-aeb8ffaf084c")]
    public readonly InputSlot<Vector2> Offset = new();

    [Input(Guid = "4be3c132-1318-426e-a2ed-9534110ca03f")]
    public readonly InputSlot<float> Rotate = new();

    [Input(Guid = "c15a2562-824d-416a-91fd-6bab0380ff0f")]
    public readonly InputSlot<Vector3> RotateAxis = new();

    [Input(Guid = "1dfc889d-ed5b-4e82-b29c-a1b9079b8fa8")]
    public readonly InputSlot<bool> ApplyPointOrientation = new();

    [Input(Guid = "a0d7fc98-590d-481a-83a2-8522a3053082")]
    public readonly InputSlot<float> WSpeed = new();

    [Input(Guid = "c080dd2e-3043-4f3f-a8fe-19cdce6ca5b4")]
    public readonly InputSlot<Gradient> Gradient = new();

    [Input(Guid = "9645b08f-ed92-4b82-8090-0a31162e83fb")]
    public readonly InputSlot<Curve> Scale = new();

    [Input(Guid = "a6069687-171d-417b-a746-7557486204bc", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMod = new();

    [Input(Guid = "b83c1c1d-fef9-48d0-bf6b-da5b4ad8b094")]
    public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "a3543b5d-0ee1-47de-9ef3-7747d7f9903f")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "4256223c-ed88-4263-90f0-96cbc6da84d2")]
    public readonly InputSlot<Texture2D> Texture_ = new();

    [Input(Guid = "7d021815-20a0-475d-91e0-1514173bf5d5")]
    public readonly InputSlot<float> AlphaCutOff = new();

    [Input(Guid = "4c4dc1b7-2fb0-4c18-8bee-441a7ad3cc7a")]
    public readonly InputSlot<float> Sdfgh = new();

    [Input(Guid = "cb4b567b-2a1e-4548-82ce-6683ba3c39de")]
    public readonly InputSlot<bool> ApplyFog = new();

}