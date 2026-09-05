namespace Lib.render.basic;

[Guid("d38fe24e-be01-4a22-9595-b06bc906bf49")]
internal sealed class ShadowPlane : Instance<ShadowPlane>
{
    [Output(Guid = "e7622351-6afd-41dd-b9c7-bddc1e25e128")]
    public readonly Slot<Command> Output = new Slot<Command>();


    [Input(Guid = "7a51aab1-7de2-4bc2-bfb6-bd32aca0381c")]
    public readonly InputSlot<Command> Command = new InputSlot<Command>();

    [Input(Guid = "5a2f2521-c521-48a9-99c9-3916413c7938")]
    public readonly InputSlot<float> PlaneSize = new InputSlot<float>();

    [Input(Guid = "16527748-fe55-4ced-b08d-6c94377c5563")]
    public readonly InputSlot<float> FieldOfView = new InputSlot<float>();

    [Input(Guid = "2f90c70b-7979-4426-aad2-7e715e7c1691", MappedType = typeof(Resolutions))]
    public readonly InputSlot<int> Resolution = new InputSlot<int>();

    [Input(Guid = "f192118e-ea03-4233-a7b5-93f0fa380e93")]
    public readonly InputSlot<Vector4> Color = new InputSlot<Vector4>();

    [Input(Guid = "7a330847-eb4d-4b18-8f55-d220351537a7")]
    public readonly InputSlot<float> BlurRadius = new InputSlot<float>();

    [Input(Guid = "ad589e97-e1db-4557-8a46-f6618f3c969b")]
    public readonly InputSlot<Vector2> BlurDistribution = new InputSlot<Vector2>();

    [Input(Guid = "bcfc6e35-6427-46fd-9902-189e88651ebe")]
    public readonly InputSlot<Vector3> Center = new InputSlot<Vector3>();

    [Input(Guid = "48974f83-203f-4c35-896f-c1e0820214cf")]
    public readonly InputSlot<Vector3> Rotation = new InputSlot<Vector3>();

    private enum Resolutions
    {
        _128 = 128,
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096,
    }
        
}