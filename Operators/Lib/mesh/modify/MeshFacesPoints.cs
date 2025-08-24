namespace Lib.mesh.modify;

[Guid("58a490b1-eb8a-4102-906a-f74a79c0ad1c")]
internal sealed class MeshFacesPoints : Instance<MeshFacesPoints>
{

    [Output(Guid = "f40d8abc-a97c-4dba-8811-b19042db1c66")]
    public readonly Slot<BufferWithViews> OutBuffer = new Slot<BufferWithViews>();

        [Input(Guid = "bad1db42-201c-4d3b-8e62-82e812a8388f")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> InputMesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "df3005ad-76ca-423a-95f5-4a29ae1284ca")]
        public readonly InputSlot<float> ScaleUniform = new InputSlot<float>();

        [Input(Guid = "0a8bbf07-d77e-46e5-8598-d49e0c21735c")]
        public readonly InputSlot<System.Numerics.Vector3> Stretch = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "d5288530-4e29-4a6d-9175-7e43e2880f1d")]
        public readonly InputSlot<bool> ScaleWithFaceArea = new InputSlot<bool>();

        [Input(Guid = "9361aeff-d96f-400c-8837-7d98fb6da99e")]
        public readonly InputSlot<float> Fx1 = new InputSlot<float>();

        [Input(Guid = "ec278884-ccce-482a-934e-571b48aa36a7")]
        public readonly InputSlot<float> Fx2 = new InputSlot<float>();

        [Input(Guid = "ffe056e2-c937-4d91-a2ad-73e7988ea994")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "803dc471-081f-48c8-b1b5-f5b5e4673518")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "67b24f6d-61d1-49df-9e86-4555474f91b4")]
        public readonly InputSlot<System.Numerics.Vector3> OffsetByTBN = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "71d65094-f6a6-4ea7-8e3a-c0c95db5cff0")]
        public readonly InputSlot<float> OffsetScale = new InputSlot<float>();
        
        
    private enum Directions
    {
        Surface,
        Noise,
        Center,
    }
        

}