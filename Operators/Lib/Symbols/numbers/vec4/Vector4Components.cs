namespace Lib.numbers.vec4;

[Guid("b15e4950-5c72-4655-84bc-c00647319030")]
internal sealed class Vector4Components : Instance<Vector4Components>
{
    [Output(Guid = "CFB58526-0053-4BCA-AA85-D83823EFBA96")]
    public readonly Slot<float> X = new();
    [Output(Guid = "2F8E90DD-BA03-43DC-82A2-8D817DF45CC7")]
    public readonly Slot<float> Y = new();
    [Output(Guid = "162BB4FE-3C59-45C2-97CC-ECBA85C1B275")]
    public readonly Slot<float> Z = new();
    [Output(Guid = "E1DEDE5F-6963-4BCC-AA12-ABEB819BB5DA")]
    public readonly Slot<float> W = new();

    public Vector4Components()
    {
        X.UpdateAction += Update;
        Y.UpdateAction += Update;
        Z.UpdateAction += Update;
        W.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Vector4 value = Value.GetValue(context);
        X.Value = value.X;
        Y.Value = value.Y;
        Z.Value = value.Z;
        W.Value = value.W;
    }
        
    [Input(Guid = "980EF785-6AE2-44D1-803E-FEBFC75791C5")]
    public readonly InputSlot<Vector4> Value = new();
}