namespace Lib.mesh.modify;

[Guid("daacabd8-0338-4998-898f-94580abd8eac")]
internal sealed class ScatterMeshFaces : Instance<ScatterMeshFaces>
{
    [Output(Guid = "485badd0-a01d-4838-a516-5db8867d2e04")]
    public readonly Slot<MeshBuffers> Result = new();

    [Input(Guid = "d2ca131d-1bab-47ce-b0e7-382c61d6a176")]
    public readonly InputSlot<MeshBuffers> InputMesh = new();

    [Input(Guid = "a795f91b-75e7-4b95-a8c7-28cbbe17fc04", MappedType = typeof(Directions))]
    public readonly InputSlot<int> Direction = new();

    [Input(Guid = "de7b5f05-d936-43af-a18d-bc27692dc9ed")]
    public readonly InputSlot<float> Amount = new();

    [Input(Guid = "46dbd416-53ab-4efc-bfe0-5e82e98db3b6")]
    public readonly InputSlot<float> Rotate = new();

    [Input(Guid = "5ece59ea-9e0d-48bc-a19b-c8b33cbbaa47")]
    public readonly InputSlot<float> Push = new();

    [Input(Guid = "77c28c83-7592-4ab3-b7ca-d64a176965b1")]
    public readonly InputSlot<float> Shrink = new();

    [Input(Guid = "e7b6d028-1ddf-4a4e-b2ca-13a73e0d1508")]
    public readonly InputSlot<float> Scatter = new();

    [Input(Guid = "cf800a99-185b-44a1-b9f4-8d8bf4154fb3")]
    public readonly InputSlot<float> Distort = new();

    [Input(Guid = "95162eb0-c762-4370-8ae0-8fe8c05247fe")]
    public readonly InputSlot<float> NoiseAmount = new();

    [Input(Guid = "cb0e3b52-fc4f-429b-a3c0-48138defcfe5")]
    public readonly InputSlot<float> NoiseFrequency = new();

    [Input(Guid = "30764b06-86f1-4194-9b14-e76b327a7f78")]
    public readonly InputSlot<float> NoisePhase = new();

    [Input(Guid = "811ab837-44a7-4780-91d0-1058e6bf3557")]
    public readonly InputSlot<float> NoiseVariation = new();

    [Input(Guid = "80778578-bdcc-469f-bae2-23f5d496f3cb")]
    public readonly InputSlot<bool> UseVertexSelection = new();

    [Input(Guid = "9ed2888a-b2f9-4d7d-bd6f-804459e23da2")]
    public readonly InputSlot<Vector3> AmountDistribution = new();
        
        
    private enum Directions
    {
        Surface,
        Noise,
        Center,
    }
        

}