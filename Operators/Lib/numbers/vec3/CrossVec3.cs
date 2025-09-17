namespace Lib.numbers.vec3;

[Guid("6cc830f0-cbc5-430d-9f32-1eab47b44693")]
internal sealed class CrossVec3 : Instance<CrossVec3>
{
    public CrossVec3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = Vector3.Cross(Input1.GetValue(context), Input2.GetValue(context));
    }
    
    [Input(Guid = "c88f66a4-8702-48ed-9693-29b3eac044f9")]
    public readonly InputSlot<System.Numerics.Vector3> Input1 = new InputSlot<System.Numerics.Vector3>();

    [Input(Guid = "5d81821a-19d1-4961-b895-09c25f3f6afe")]
    public readonly InputSlot<System.Numerics.Vector3> Input2 = new InputSlot<System.Numerics.Vector3>();

    [Output(Guid = "b5f83b5e-ed8c-47c4-bff2-c6f24651223d")]
    public readonly Slot<System.Numerics.Vector3> Result = new Slot<System.Numerics.Vector3>();
}