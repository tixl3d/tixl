namespace Lib.mesh.modify;

[Guid("93e2f11a-18a2-4dcb-86df-c452d340b409")]
internal sealed class BlendMeshVertices : Instance<BlendMeshVertices>
{

    [Output(Guid = "27258c56-6421-4800-a057-26c9c2ede324")]
    public readonly Slot<MeshBuffers> BlendedMesh = new();

    [Input(Guid = "a7ef92db-87c9-4cf3-bd62-f8cb858d9ed9")]
    public readonly InputSlot<MeshBuffers> MeshA = new InputSlot<MeshBuffers>();

    [Input(Guid = "44acb67a-a443-4c11-8939-2b76132f8dbf")]
    public readonly InputSlot<MeshBuffers> MeshB = new InputSlot<MeshBuffers>();

    [Input(Guid = "add0fd7b-fa76-4788-8940-d1949a44b342")]
    public readonly InputSlot<float> BlendValue = new InputSlot<float>();

    [Input(Guid = "3f4ed7b1-dbb6-4736-b323-31ea8fad870e")]
    public readonly InputSlot<float> Scatter = new InputSlot<float>();

    [Input(Guid = "5ccbc77e-2180-4c83-aaa6-ed3232de8afb", MappedType = typeof(BlendModes))]
    public readonly InputSlot<int> BlendMode = new InputSlot<int>();

    [Input(Guid = "355ae0a4-893a-4852-a37d-9dd77179c507")]
    public readonly InputSlot<float> RangeWidth = new InputSlot<float>();

    [Input(Guid = "42941c1f-c53e-45ed-876c-f9043753a473", MappedType = typeof(PairingModes))]
    public readonly InputSlot<int> Pairing = new InputSlot<int>();

        
    private enum BlendModes
    {
        Blend,
        UseW1AsWeight,
        UseW2AsWeight,
        RangeBlend,
        RangeBlendSmooth,
    }
        
    private enum PairingModes
    {
        WrapAround,
        Adjust,
    }
        
}