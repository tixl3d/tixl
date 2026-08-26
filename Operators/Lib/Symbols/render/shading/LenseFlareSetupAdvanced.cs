namespace Lib.render.shading;

[Guid("8f4ca295-7268-44a2-94b0-4e974fac22a4")]
internal sealed class LenseFlareSetupAdvanced : Instance<LenseFlareSetupAdvanced>
{
    [Output(Guid = "fe0eca40-4f2d-475b-8372-0351d9f67c59")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "f2c310f0-a019-40e5-9b17-3c35d254bb82")]
    public readonly InputSlot<float> Brightness = new();

    [Input(Guid = "18b19778-3ff2-4e38-8fb6-fa748babb967")]
    public readonly InputSlot<int> RandomSeed = new();

    [Input(Guid = "8fa8fbae-4d50-480e-ae20-677946d3e533")]
    public readonly InputSlot<int> LightIndex = new();

    [Input(Guid = "240c75c2-f725-48b8-8abd-4bc4c05190f7")]
    public readonly InputSlot<Vector4> RandomizeColor = new();

    [Input(Guid = "7cd57dc9-e6a8-4bb6-8245-ffbe962f6bf4")]
    public readonly InputSlot<float> Digital = new InputSlot<float>();

    [Input(Guid = "7a870089-8e1e-4cd4-a920-7cc5a50d7376")]
    public readonly InputSlot<float> Star = new InputSlot<float>();

    [Input(Guid = "c3e3042d-b306-4c14-a4cb-9b2a6a54e841")]
    public readonly InputSlot<float> Center = new InputSlot<float>();

    [Input(Guid = "381c5f2f-b9bb-481f-9567-9fd949063338")]
    public readonly InputSlot<float> ColorEdgeGlow = new InputSlot<float>();

    [Input(Guid = "7a8b4acd-69db-4282-a6ce-d637a1339a27")]
    public readonly InputSlot<float> MultiIris = new InputSlot<float>();

    [Input(Guid = "94630a8b-c97d-42f3-8df4-a40791bc7e6c")]
    public readonly InputSlot<float> Sparkle = new InputSlot<float>();

    [Input(Guid = "be8975c0-2748-4ace-8047-c941caad45c2")]
    public readonly InputSlot<float> Shimmer = new InputSlot<float>();

    [Input(Guid = "007b11db-6d0e-49e6-a747-6a2d8cdc0e6b")]
    public readonly InputSlot<float> FlareSprites = new InputSlot<float>();


}