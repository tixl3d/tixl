namespace Lib.mesh.draw;

[Guid("c757cde3-511c-44cb-af62-39156557daa6")]
internal sealed class DrawMeshHatched : Instance<DrawMeshHatched>
{
    [Output(Guid = "56b8a7fc-2d23-4e25-84cb-a3b25b832935")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "42042144-1ca7-41c7-92b8-21ea1136698a")]
    public readonly InputSlot<Vector4> ColorHighlight = new();

    [Input(Guid = "204a3776-c191-48b1-b502-2efc45adec67")]
    public readonly InputSlot<Vector4> ColorShade = new();

    [Input(Guid = "61eb78f5-d36e-479e-938f-a4fa4b31a1b6")]
    public readonly InputSlot<float> LineWidth = new();

    [Input(Guid = "ce4cc48f-07bb-4aa8-a410-2042732aaa25")]
    public readonly InputSlot<float> FollowSurface = new();

    [Input(Guid = "603078ef-e7bc-4ba8-87b2-81574a04cfb6")]
    public readonly InputSlot<float> OffsetDirection = new();

    [Input(Guid = "e069cd71-547b-4231-a6ff-33036337f805")]
    public readonly InputSlot<float> RandomFaceDirection = new();

    [Input(Guid = "50935c43-666f-4c31-825b-667bc83c327a")]
    public readonly InputSlot<float> RandomFaceLighting = new();

    [Input(Guid = "d84bd197-8788-4787-9883-c43bc0e285e7")]
    public readonly InputSlot<Gradient> Shading = new();

    [Input(Guid = "a9021b39-f236-4e29-98d7-e7805ceaa82f")]
    public readonly InputSlot<CullMode> Culling = new();

    [Input(Guid = "db54450a-2648-4cea-99ef-d2e0a083a4de")]
    public readonly InputSlot<bool> EnableZTest = new();

    [Input(Guid = "c74dbffa-cea8-4ab7-a3b9-0bc6009041f3")]
    public readonly InputSlot<bool> EnableZWrite = new();

    [Input(Guid = "120e2a56-c7fa-4eb8-80b0-ff75cc924960")]
    public readonly InputSlot<Texture2D> ColorMap = new();

    [Input(Guid = "f5037611-c425-4097-927f-08041dfee27f")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

}