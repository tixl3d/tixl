namespace Lib.mesh.modify;

[Guid("208a86b5-55cc-460a-86e6-2b17da818494")]
internal sealed class TransformMeshUVs : Instance<TransformMeshUVs>
{

    [Output(Guid = "1030db1a-e5d0-4eac-9f3d-cc1e8d3867c7")]
    public readonly Slot<MeshBuffers> Result = new();

    [Input(Guid = "b9e7efdf-98d6-4d5a-94e8-16f38cfe4e55")]
    public readonly InputSlot<MeshBuffers> InputMesh = new InputSlot<MeshBuffers>();

    [Input(Guid = "b8409f71-d2e3-4fb3-91dc-abf96b55379f")]
    public readonly InputSlot<Vector3> Translate = new InputSlot<Vector3>();

    [Input(Guid = "da73250e-fcf2-4fe9-9a84-a1d139a0390c")]
    public readonly InputSlot<Vector3> Rotate = new InputSlot<Vector3>();

    [Input(Guid = "4b8a067d-8630-485e-b390-1fca7cc06323")]
    public readonly InputSlot<Vector3> Stretch = new InputSlot<Vector3>();

    [Input(Guid = "d2d278d9-7933-49ea-83c2-3566f5a13d1f")]
    public readonly InputSlot<float> Uniformscale = new InputSlot<float>();

    [Input(Guid = "888017f2-3ac2-464b-ae6f-9f8caf53ba6d")]
    public readonly InputSlot<bool> UseVertexSelection = new InputSlot<bool>();

    [Input(Guid = "590e24cc-00cc-4c9a-8f96-850857686c4a")]
    public readonly InputSlot<Vector3> Pivot = new InputSlot<Vector3>();

        [Input(Guid = "b9724cb4-1a5f-4250-8d2d-d0f5a12dbeb1")]
        public readonly InputSlot<bool> TexCoord2 = new InputSlot<bool>();

        
    private enum Spaces
    {
        PointSpace,
        ObjectSpace,
        WorldSpace,
    }
        
    private enum Directions
    {
        WorldSpace,
        SurfaceNormal,
    }
}