namespace Lib.mesh.draw;

[Guid("c08a6847-e7c4-46e6-a8fb-24bb62a64b96")]
public class DrawMeshWithShadow : Instance<DrawMeshWithShadow>
{
    [Output(Guid = "41608df0-8589-4144-80a0-438b73ced439")]
    public readonly Slot<Command> Output = new Slot<Command>();

        [Input(Guid = "830f6fea-926b-4597-8fad-075b6d52db4d")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "5c4def3d-9261-4100-8136-abc552e9f547")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "734e5d0f-d319-43a9-84ab-8aee82acbb7f")]
        public readonly InputSlot<float> AlphaCutOff = new InputSlot<float>();

        [Input(Guid = "80071837-3b7c-440f-b0da-ec3c7fa31590")]
        public readonly InputSlot<int> BlendMode = new InputSlot<int>();

        [Input(Guid = "2c7cb3a7-ba47-42e9-bc39-597e5fd45457")]
        public readonly InputSlot<int> FillMode = new InputSlot<int>();

        [Input(Guid = "8cea9257-8bde-46ac-9c32-8a63fea405d6")]
        public readonly InputSlot<SharpDX.Direct3D11.CullMode> Culling = new InputSlot<SharpDX.Direct3D11.CullMode>();

        [Input(Guid = "f3f574a3-6e2f-45b9-9335-c8fc17111e05")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "d81604c9-5a34-44e7-a301-284c1a99afc0")]
        public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

        [Input(Guid = "0a01af5b-3f25-43e2-9157-1aa961b2ffcf")]
        public readonly InputSlot<System.Numerics.Vector4> ShadowColor = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "848e10b4-5511-4495-bf63-eb76a1b63fb4")]
        public readonly InputSlot<float> ShadowBias = new InputSlot<float>();

        [Input(Guid = "bec607ca-e53f-43c8-a052-224506596016")]
        public readonly InputSlot<float> ShadowOffset = new InputSlot<float>();

}