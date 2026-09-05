namespace Lib.numbers.vec3;

[Guid("a8083b41-951e-41f2-bb8a-9b511da26102")]
internal sealed class Vector3Components : Instance<Vector3Components>
{
    [Output(Guid = "2F05B628-8FC0-46DC-B312-9B107B8CA4A2")]
    public readonly Slot<float> X = new();
    [Output(Guid = "F07622C1-ACA1-4B8B-8E4A-42D94BE87539")]
    public readonly Slot<float> Y = new();
    [Output(Guid = "5173CF99-C9AE-4DA4-8B7A-A6B6F27DAA84")]
    public readonly Slot<float> Z = new();

    public Vector3Components()
    {
        X.UpdateAction += Update;
        Y.UpdateAction += Update;
        Z.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Vector3 value = Value.GetValue(context);
        X.Value = value.X;
        Y.Value = value.Y;
        Z.Value = value.Z;
    }
        
    [Input(Guid = "BC217D95-25D4-44E8-B5BA-05B7FACD9A20")]
    public readonly InputSlot<Vector3> Value = new();
}