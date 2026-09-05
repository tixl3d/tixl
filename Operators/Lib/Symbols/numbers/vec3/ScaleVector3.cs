namespace Lib.numbers.vec3;

[Guid("646d6a5d-a1d7-471e-86ab-e1fb2542a2c2")]
internal sealed class ScaleVector3 : Instance<ScaleVector3>
{
    [Output(Guid = "956F31AA-8C84-4D2D-9FC7-E63D753F6941")]
    public readonly Slot<Vector3> Result = new();

        
    public ScaleVector3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var a = A.GetValue(context);
        var b = B.GetValue(context);
        var u = ScaleUniform.GetValue(context);
        Result.Value = a * b * u;
    }
        
    [Input(Guid = "DE6BFE5A-EBCD-4DA6-8C8A-79989A31DD9F")]
    public readonly InputSlot<Vector3> A = new();

    [Input(Guid = "2218624E-2F7B-4BC6-9C36-447C371A6CD7")]
    public readonly InputSlot<Vector3> B = new();
        
    [Input(Guid = "4AB40AA5-B390-4042-A959-8EDDF9CBC9B0")]
    public readonly InputSlot<float> ScaleUniform = new();
        
}