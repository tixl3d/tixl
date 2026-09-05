namespace Lib.numbers.vec3;

[Guid("3021300c-7f56-49c1-bcaf-92447b716dad")]
internal sealed class SubVec3 : Instance<SubVec3>
{
    [Output(Guid = "75606f1e-3bd6-4ae0-b9d2-f8dc953ec212")]
    public readonly Slot<Vector3> Result = new();

    public SubVec3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = Input1.GetValue(context) - Input2.GetValue(context);
    }


    [Input(Guid = "b1fe7da6-169a-4c09-839d-0d494606d527")]
    public readonly InputSlot<Vector3> Input1 = new();

    [Input(Guid = "1c78d0e2-88b1-46d3-a575-ec088b293450")]
    public readonly InputSlot<Vector3> Input2 = new();
}