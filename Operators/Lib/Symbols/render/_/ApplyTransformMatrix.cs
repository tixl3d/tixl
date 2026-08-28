using T3.Core.Utils.Geometry;

namespace Lib.render.@_;

[Guid("195afff5-13f6-4c5d-af49-655a4f92c2f8")]
internal sealed class ApplyTransformMatrix : Instance<ApplyTransformMatrix>
{
    [Output(Guid = "51334471-d9fe-4574-8541-f87b67f2deab")]
    public readonly Slot<Command> Output = new();

    public ApplyTransformMatrix()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var vec4Rows = TransformRows.GetValue(context);
        if (vec4Rows == null || vec4Rows.Length != 4)
        {
            Log.Warning("Invalid matrix format");
            return;
        }
            
        var matrix = vec4Rows.ToMatrixFromRows();
        matrix.Transpose();
            
        var previousObjectToWorld = context.ObjectToWorld;
        context.ObjectToWorld = Matrix4x4.Multiply(matrix, context.ObjectToWorld);
        Command.GetValue(context);
        context.ObjectToWorld = previousObjectToWorld;

            
        // var previousWorldTobject = context.ObjectToWorld;
        //
        // context.ObjectToWorld = Matrix.Multiply(, context.ObjectToWorld);
        //
        // Command.GetValue(context);
        // context.ObjectToWorld = previousWorldTobject;
    }

    [Input(Guid = "f7d28833-d894-446f-9402-e8ac74794870")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "c3b1ba6c-4306-4ae4-9429-d1f2461e2e8c")]
    public readonly InputSlot<Vector4[]> TransformRows = new();
        
}