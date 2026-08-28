namespace Lib.numbers.vec2;

[Guid("b732a668-8994-4f34-ab96-5245d355e33f")]
internal sealed class Vec2ToVec3 : Instance<Vec2ToVec3>
{
    [Output(Guid = "E12A6D13-3DC5-490A-B8C8-677B4671EE73")]
    public readonly Slot<Vector3> Result = new ();

    public Vec2ToVec3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var a = XY.GetValue(context);
        var z = Z.GetValue(context);
        Result.Value = new Vector3(a.X, a.Y, z);

    }
        
    [Input(Guid = "a71e7512-08ac-40b4-abdc-5b0835472e7f")]
    public readonly InputSlot<Vector2> XY = new();
        
    [Input(Guid = "F1E036D8-61BC-4FDC-BB87-5A8A27982D65")]
    public readonly InputSlot<float> Z = new();
        
}