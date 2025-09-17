namespace Lib.mesh.draw{
    [Guid("0920c9cf-91b3-42ee-9db4-a5a63c16d71e")]
    internal sealed class VisualizeUvMap : Instance<VisualizeUvMap>
    {
        [Output(Guid = "e1297eb9-ba31-4ded-a1c9-4a0ee115188e")]
        public readonly Slot<Command> Output = new Slot<Command>();


        [Input(Guid = "55351998-2a43-46d4-b1c3-d506225070e6")]
        public readonly InputSlot<MeshBuffers> Mesh = new InputSlot<MeshBuffers>();

        [Input(Guid = "2772f510-8cdc-4d99-bf22-4c2054cf9491")]
        public readonly InputSlot<Texture2D> Texture = new InputSlot<Texture2D>();

        [Input(Guid = "35a9cbb6-fd1d-4137-bcb3-741f5061a711")]
        public readonly InputSlot<bool> SwitchUV = new InputSlot<bool>();

        [Input(Guid = "b1da0217-3d9a-48dd-90a0-7195f0eaf12a")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

    }
}

