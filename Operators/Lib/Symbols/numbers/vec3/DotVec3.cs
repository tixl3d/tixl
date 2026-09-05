namespace Lib.numbers.vec3;

[Guid("57e2c1de-95e7-4fd7-bbfb-2349e1fb17b4")]
internal sealed class DotVec3 : Instance<DotVec3>
{
    [Input(Guid = "6e543f12-1849-4a75-93e3-052eae0d551d")]
    public readonly InputSlot<Vector3> Input1 = new();

    [Input(Guid = "21ae0f87-f8b9-427b-81c3-6f9a5f503b58")]
    public readonly InputSlot<Vector3> Input2 = new();

    [Output(Guid = "b1758ea3-cb8c-42dc-b229-8e1b59ac2033")]
    public readonly Slot<float> Result = new();

    public DotVec3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = Vector3.Dot(Input1.GetValue(context), Input2.GetValue(context));
    }
}