namespace Lib.render.@_;

[Guid("ade1d03d-db80-41ad-bcfa-8a2b900e9d41")]
internal sealed class _ComputeDepthToLinear : Instance<_ComputeDepthToLinear>
{
    [Output(Guid = "eff29dae-87c5-43a4-856b-51ac3abf567a")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "de65c36d-10a7-446f-a4dd-e55ce42f4203")]
    public readonly InputSlot<Texture2D> DepthBuffer = new();

    [Input(Guid = "a5f6347a-9c57-46f2-be39-80499b35cdf7")]
    public readonly InputSlot<float> Near = new();

    [Input(Guid = "9f42b73c-d6f1-4907-ba55-9fb56514aa23")]
    public readonly InputSlot<float> Far = new();

    [Input(Guid = "50dbf432-ea4d-4d49-8cf4-e946a950b998")]
    public readonly InputSlot<Texture2D> OutputTexture = new();

    [Input(Guid = "7e1e99e1-3e2a-4960-bcc3-5b7e8e6ae95c")]
    public readonly InputSlot<Vector2> OutRange = new();

    [Input(Guid = "831c97ad-40c1-4687-b536-f549bbbccf6f")]
    public readonly InputSlot<bool> ClampOutput = new();

    [Input(Guid = "63ce243b-48f4-482e-9ddf-a43cf1e4fc5f", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    private enum Modes
    {
        Linear,
        LegacyDOF,
    }
}