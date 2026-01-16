namespace Lib.numbers.vec3;

[Guid("5adffcb0-5e38-4e80-80a3-a56a427d6d7a")]
internal sealed class Vec2Magnitude : Instance<Vec2Magnitude>
{
    [Output(Guid = "08719372-3366-48FF-8BF5-3B103F71BD6E")]
    public readonly Slot<float> Result = new ();

    public Vec2Magnitude()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = Input.GetValue(context).Length();
    }
        
    [Input(Guid = "85639CE8-20FF-4FBA-8573-706774CC53D5")]
    public readonly InputSlot<Vector2> Input = new();

}