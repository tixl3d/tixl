using T3.Core.Utils;

namespace Lib.mesh.draw;

[Guid("0e313329-74fb-4f2a-b1c2-136e1ecf9b3e")]
internal sealed class DrawMeshChunksAtPoints : Instance<DrawMeshChunksAtPoints>
{
    [Output(Guid = "aec2ae24-3b64-48ae-a61c-49291829f284", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "40e840c7-b707-4f63-8ae8-bd7498d34f54")]
    public readonly InputSlot<BufferWithViews> GPoints = new InputSlot<BufferWithViews>();

    [Input(Guid = "43527e99-7da1-459a-b51d-0addeeb6786a")]
    public readonly InputSlot<Vector4> Color = new InputSlot<Vector4>();

    [Input(Guid = "93561534-b33e-4e72-9601-36216031e5d2")]
    public readonly InputSlot<float> Size = new InputSlot<float>();

    [Input(Guid = "8613e460-b7c3-47d7-9bc6-01fff9150ec0")]
    public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

    [Input(Guid = "f758666a-4716-446e-8ad3-a9e7b2a05ff6")]
    public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

    [Input(Guid = "f3c88d15-a33e-4d44-bf13-27f76a1e7a50", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new InputSlot<int>();

    [Input(Guid = "821e86ce-9118-429d-94d8-d30f5df9e455")]
    public readonly InputSlot<MeshBuffers> Mesh = new InputSlot<MeshBuffers>();

    [Input(Guid = "3ba54ff1-0c53-4f8d-92f0-20de48144b82")]
    public readonly InputSlot<CullMode> CullMode = new InputSlot<CullMode>();

    [Input(Guid = "5ff607e0-a3db-4ece-93dc-3713669def9e")]
    public readonly InputSlot<bool> UseF1ForSize = new InputSlot<bool>();

    [Input(Guid = "010d4e6c-26b8-444e-8d22-3be0d93222ef")]
    public readonly InputSlot<float> AlphaCutOff = new InputSlot<float>();

    [Input(Guid = "c159c08f-ceef-43b3-8e69-91bfb943c226", MappedType = typeof(FillMode))]
    public readonly InputSlot<int> FillMode = new InputSlot<int>();

    [Input(Guid = "7d609639-d9f3-4c7c-9fde-91c71a29aedc")]
    public readonly InputSlot<bool> UseStretch = new InputSlot<bool>();

    [Input(Guid = "8cdea8a6-7e39-433d-a616-2f64e5e04ed7")]
    public readonly InputSlot<BufferWithViews> ChunkIndices = new InputSlot<BufferWithViews>();

    [Input(Guid = "fc846c4a-ac50-4260-8267-4484ced2d22f")]
    public readonly InputSlot<bool> UpdateDrawData = new InputSlot<bool>();

            [Input(Guid = "2ba5d5a0-2292-409a-b603-867bc2c347c7")]
        public readonly InputSlot<float> ShadowOffset = new InputSlot<float>();

        [Input(Guid = "271ffc87-bf9a-4958-ab9f-387f27744aee")]
        public readonly InputSlot<float> ShadowBias = new InputSlot<float>();

        [Input(Guid = "c54759f0-759d-470e-8000-03cdfe609f1c")]
        public readonly InputSlot<System.Numerics.Vector4> ShadowColor = new InputSlot<System.Numerics.Vector4>();
}