using T3.Core.Utils;

namespace Lib.point.draw.legacy;

[Guid("16d10dc8-63b9-4ddf-90b8-41caef99d945")]
internal sealed class _DrawQuads : Instance<_DrawQuads>
{
    [Output(Guid = "5c6f0299-16bd-4553-9ca1-e8d7c7634b37", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "ad6b28be-ba14-4063-baf9-cfaf0096f1ea")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "edf0f842-5ab8-4366-83d1-972653056220")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "f02e81bb-441f-4d78-9e6c-71f931b6bb5c")]
    public readonly InputSlot<Vector2> Offset = new();

    [Input(Guid = "9cb2ad14-dac0-44f2-a87d-3ae29c8a7d97")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "2bf3f3ef-43f4-4fbb-ac8c-c6b3777aa3ed")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "7fd87d1d-bb54-4366-b0ac-ea2850ed13fb")]
    public readonly InputSlot<float> UseWForSize = new();

    [Input(Guid = "0577916f-49b5-4564-8703-92074ee27ea0")]
    public readonly InputSlot<float> Rotate = new();

    [Input(Guid = "94116342-bc50-42ed-934b-7dd408eafe45")]
    public readonly InputSlot<Vector3> RotateAxis = new();

    [Input(Guid = "ae975647-36f6-494c-b4f1-3289e4d8c03e")]
    public readonly InputSlot<Texture2D> Texture_ = new();

    [Input(Guid = "fc19ad98-65ea-46f2-896d-6b9279a9eaa4")]
    public readonly InputSlot<Int2> TextureCells = new();

    [Input(Guid = "285eedd2-50d1-43ff-ae18-7a9475ba8e89")]
    public readonly InputSlot<int> AltasMode = new();

    [Input(Guid = "dc21d7ab-5988-4df8-99b5-1b107eb6c3c9", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new();

    [Input(Guid = "c1163955-3d1e-48aa-a4c1-ba81790b08c8")]
    public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "154ccdc4-c918-499f-8acb-ca09e6daa8b1")]
    public readonly InputSlot<bool> EnableDepthTest = new();

    [Input(Guid = "64837573-d5d1-4cc2-9dd2-337649308cb0")]
    public readonly InputSlot<CullMode> CullMode = new();
}