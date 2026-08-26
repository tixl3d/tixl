using T3.Core.Utils.Geometry;

namespace Lib.numbers.vec3;

[Guid("20ad2907-524d-4160-802b-ad4a359b3d0d")]
internal sealed class TransformVec3 : Instance<TransformVec3>
{
    [Output(Guid = "8ae5ca74-f39e-4b2e-881f-6c80d2b274ef")]
    public readonly Slot<Vector3> Result = new();

        
    public TransformVec3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var a = A.GetValue(context);
        var m = Matrix.GetValue(context);

        m.Transpose();
        var v4 = Vector4.Transform(new Vector4(a, 1.0f), m);
        Result.Value = new Vector3(v4.X, v4.Y, v4.Z);
    }
        
    [Input(Guid = "b8e06842-f8e9-4aa1-b291-48dd077cfab5")]
    public readonly InputSlot<Vector3> A = new();

    [Input(Guid = "5B124D01-CE79-4055-88EA-66394F7831D8")]
    public readonly InputSlot<Matrix4x4> Matrix = new();
        
    // [Input(Guid = "95a16d6a-fc64-4753-a5f2-1ec8ad36d909")]
    // public readonly InputSlot<float> ScaleUniform = new();
        
}