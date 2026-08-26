namespace Lib.point.sim._legacy;

[Guid("69c3b4ce-490a-48d4-b1d0-56dd6bf7a9a8")]
internal sealed class _LegacySimForwardMovement : Instance<_LegacySimForwardMovement>
{

    [Output(Guid = "9495dbae-0e49-449c-ab4a-58e267974385")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "2038b006-b5a3-472d-870b-d1a3623dfc0c")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "495e9766-0e5f-4ab7-abc1-c06b2edfe55d")]
    public readonly InputSlot<float> Drag = new();

    [Input(Guid = "697b7aa9-2b6c-423c-8f84-dbdbae721609")]
    public readonly InputSlot<float> Speed = new();

    [Input(Guid = "e733cd17-e854-4c23-99cb-9a03d4ae5eb5")]
    public readonly InputSlot<bool> IsEnabled = new();
}