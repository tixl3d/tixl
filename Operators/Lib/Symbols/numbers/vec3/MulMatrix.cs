using T3.Core.Utils.Geometry;

namespace Lib.numbers.vec3;

[Guid("b4c5519e-26fb-49f6-b932-7fcd076c458a")]
internal sealed class MulMatrix : Instance<MulMatrix>
{
    [Output(Guid = "D2C5641D-844A-4D90-87F0-C0AD34B4E344")]
    public readonly Slot<Matrix4x4> Result = new();

        
    public MulMatrix()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var a = MatrixA.GetValue(context);
        var b = MatrixB.GetValue(context);

        //m.Transpose();
        //var v4 = Vector4.Transform(new Vector4(a, 1.0f), m);
        Result.Value = Matrix4x4.Multiply(a, b);
    }
        

    [Input(Guid = "297a2b5b-6c04-464c-93e9-9721908e33c2")]
    public readonly InputSlot<Matrix4x4> MatrixA = new();
        
    [Input(Guid = "D20C6EC1-D611-4FAE-8E0B-7BEC8EB68AF0")]
    public readonly InputSlot<Matrix4x4> MatrixB = new();
    // [Input(Guid = "95a16d6a-fc64-4753-a5f2-1ec8ad36d909")]
    // public readonly InputSlot<float> ScaleUniform = new();
        
}