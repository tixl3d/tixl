namespace Lib.render.gizmo;

[Guid("935e6597-3d9f-4a79-b4a6-600e8f28861e")]
internal sealed class GridPlane : Instance<GridPlane>
{
    [Output(Guid = "1eb82dc0-2e66-4c3c-a3e8-1b246886e59f")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "c0f652b8-80fb-4bd2-b6cd-cfc459f9fcc5")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "39a74407-5447-45fd-8fc5-5f96bd8bbdfb")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "7096708e-4b56-4b57-b86c-576540434626")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "a8e8da31-b4e0-4710-8292-2f27175c5f6b")]
    public readonly InputSlot<Vector3> Rotation = new();
}