namespace Lib.mesh._;

[Guid("68cf773d-30ac-4ae0-bc1e-b7a17ea322bb")]
internal sealed class UVsViewer : Instance<UVsViewer>
{

    [Output(Guid = "6ac1d050-592c-4533-9b5e-c9e62884c992")]
    public readonly Slot<MeshBuffers> BlendedMesh = new();

    [Input(Guid = "4ccfb3fe-5c64-45d6-8b3f-63249c69e146")]
    public readonly InputSlot<MeshBuffers> Mesh = new InputSlot<MeshBuffers>();

    [Input(Guid = "017cf5d0-5e39-480e-9a23-ce3d6e1c0d40")]
    public readonly InputSlot<float> BlendValue = new InputSlot<float>();

    [Input(Guid = "35a63cbe-7fd5-45da-9d69-322dd79db493")]
    public readonly InputSlot<bool> SwitchUV = new();


}