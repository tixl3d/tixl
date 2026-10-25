namespace Lib.point.transform;

[Guid("d48b02de-8967-4a03-9f7f-843e443470dd")]
internal sealed class OrientPointsToMovement : Instance<OrientPointsToMovement>
{
    [Output(Guid = "72850d93-fbd8-4b1c-a3b3-33a92ba5fa76")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "3dd8ec3d-3e94-4d1c-a36d-d328050dfa05")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "cad872c2-a0d9-4cbe-9fb5-662c0ecbc42c")]
    public readonly InputSlot<float> Smoothing = new();

    [Input(Guid = "5fc4a0d0-5bd4-4b85-b263-8eddd5e50ef1")]
    public readonly InputSlot<System.Numerics.Vector3> UpVector = new();

    [Input(Guid = "61aa155b-a917-4ce2-ae9d-4941ecf78f32")]
    public readonly InputSlot<float> AlignToUpVector = new();

    [Input(Guid = "656aea6e-7757-4be2-85ff-f714c9366612")]
    public readonly InputSlot<bool> Flip = new();

    [Input(Guid = "15631d43-e3b6-4203-a0ec-c76be4f35a39")]
    public readonly InputSlot<float> MinMovement = new();

    [Input(Guid = "fdeb9f33-f942-45d1-bd94-7a7a64a54d96")]
    public readonly InputSlot<float> JumpThreshold = new();

    [Input(Guid = "00731dd7-5956-4f9b-bdc5-52b708699843")]
    public readonly InputSlot<bool> Reset = new();
}
