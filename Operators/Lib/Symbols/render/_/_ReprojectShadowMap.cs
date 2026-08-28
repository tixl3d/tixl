namespace Lib.render.@_;

[Guid("e6df0d5d-bf6c-4672-801e-7a3270bd359b")]
internal sealed class _ReprojectShadowMap : Instance<_ReprojectShadowMap>
{
    [Output(Guid = "5d59930e-6cf7-46f5-b28f-c4c4682877bd")]
    public readonly Slot<Texture2D> TextureOutput = new();

        
    [Input(Guid = "0a8d40f1-c032-4c60-ac46-f7c6fd501f61")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "b95865f0-ae7a-4290-aebb-43bc6f277474")]
    public readonly InputSlot<Texture2D> Texture = new();
        
    [Input(Guid = "ceeeeca9-492d-40bd-9fbb-670b7f469d56")]
    public readonly InputSlot<object> CameraReference = new();

    [Input(Guid = "0319e655-9ac4-4526-80c9-f7286082e507")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "cc8bc844-60ff-4976-ae70-d207400b42b6")]
    public readonly InputSlot<Vector4> ClearColor = new();

    [Input(Guid = "68e0c6a1-991a-4553-a919-b00a6aad5f4c")]
    public readonly InputSlot<Format> TextureFormat = new();


        
}