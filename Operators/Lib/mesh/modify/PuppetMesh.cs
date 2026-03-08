namespace Lib.mesh.modify;

[Guid("81d27ec1-2d02-4352-98d2-f2bfb0d726bc")]
internal sealed class PuppetMesh :Instance<PuppetMesh>
{
    [Output(Guid = "35ce06c8-21ff-497f-817c-797a319a89c4")]
    public readonly Slot<MeshBuffers> Result = new();
        
 
   

    [Input(Guid = "e0aa5992-1893-4126-b6b5-fc90c010e669")]
    public readonly InputSlot<MeshBuffers> Mesh = new();



    [Input(Guid = "46762c06-6bcc-4fda-8010-73a6881e36be")]
    public readonly InputSlot<bool> UseVertexSelection = new();



        [Input(Guid = "2f4d18c2-2a68-41f0-8e7d-52de359f9d85")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> AnchorPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "039daa17-44bb-456d-9f29-b86abbf308fe")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> AnchorPointsCurrent = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "a96c3b81-609a-498a-96e7-ac829ba29c72")]
        public readonly InputSlot<bool> ShowWeight = new InputSlot<bool>();

        [Input(Guid = "752ca6e8-e2d6-4439-afa1-7f14d644d3a6")]
        public readonly InputSlot<float> WeightStrength = new InputSlot<float>();

        [Input(Guid = "49b5c5df-17f1-498b-aa57-dca843fba736", MappedType = typeof(InfluenceModes))]
    public readonly InputSlot<int> InfluenceMode = new InputSlot<int>();

    public enum InfluenceModes
    {
        Smooth,
        Linear,
        InverseSquare,
        Gaussian,
        InverseCubic,
    }








}