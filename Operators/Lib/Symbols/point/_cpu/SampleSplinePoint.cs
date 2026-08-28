using T3.Core.Utils;

namespace Lib.point._cpu;

[Guid("688230de-a3fc-4740-a12d-9e2f98cad60a")]
internal sealed class SampleSplinePoint : Instance<SampleSplinePoint>
{
    [Output(Guid = "C1FCC3A4-BB4C-4EE5-925F-1CC498F352DA")]
    public readonly Slot<Command> Output = new();
        
    [Output(Guid = "E978692F-FE00-4D59-81B9-C71F78C19151")]
    public readonly Slot<Vector3> Position = new();
        
    public SampleSplinePoint()
    {
        Output.UpdateAction += Update;
        Position.UpdateAction += Update;
    }


    private void Update(EvaluationContext context)
    {
        var p = Points.GetValue(context);

        if (p is not StructuredList<Point> pointSet)
        {
            Log.Warning("Needs a point set", this);
            return;
        }

        if (pointSet.NumElements == 0)
        {
            Log.Warning("Point set is empty", this);
            return;
        }

        if (pointSet.NumElements == 1)
        {
            Position.Value = pointSet.TypedElements[0].Position;
            return;
        }
            
        var u = U.GetValue(context).Clamp(0, 1);

        var uScaled = u * (pointSet.NumElements-2);
        var i = (int)uScaled;
        var f = uScaled - i;
        var p1 = pointSet.TypedElements[i];
        var p2 = pointSet.TypedElements[i+1];

        var pos = Vector3.Lerp(p1.Position, p2.Position, f);
        Position.Value = pos;
        var q = Quaternion.Slerp(p1.Orientation, p2.Orientation, f);

        var rot = QuaternionToMatrix(q);
        rot = Matrix4x4.Transpose(rot);
            
        var m = Matrix4x4.Identity;
            
        var prevMatrix = context.ObjectToWorld;

        //m = Matrix.Translation(pos.ToSharpDxVector3();
        m = Matrix4x4.Multiply(m, rot);
        m = Matrix4x4.Multiply(m, Matrix4x4.CreateTranslation(pos));
        context.ObjectToWorld = Matrix4x4.Multiply(m, context.ObjectToWorld);

        context.ObjectToWorld = m;
            
        SubTree.GetValue(context);

        context.ObjectToWorld = prevMatrix;
    }

    [Input(Guid = "d3aca17d-cb4a-4f7b-acdf-eaeb25016bc6")]
    public readonly InputSlot<Command> SubTree = new();

        
    [Input(Guid = "73C4A495-D846-4762-9EFD-FC47364920C1")]
    public readonly InputSlot<float> U = new();
        
    [Input(Guid = "7C787D9D-38A4-4B09-9EC7-2A24123F9E03")]
    public readonly InputSlot<StructuredList> Points = new();
        


    private static Matrix4x4 QuaternionToMatrix(in Quaternion quat)
    {
        var m = Matrix4x4.Identity; //float4x4(float4(0, 0, 0, 0), float4(0, 0, 0, 0), float4(0, 0, 0, 0), float4(0, 0, 0, 0));

        float x = quat.X, y = quat.Y, z = quat.Z, w = quat.W;
        float x2 = x + x, y2 = y + y, z2 = z + z;
        float xx = x * x2, xy = x * y2, xz = x * z2;
        float yy = y * y2, yz = y * z2, zz = z * z2;
        float wx = w * x2, wy = w * y2, wz = w * z2;

        m.M11 = 1.0f - (yy + zz);
        m.M12 = xy - wz;
        m.M13 = xz + wy;

        m.M21 = xy + wz;
        m.M22 = 1.0f - (xx + zz);
        m.M23 = yz - wx;

        m.M31 = xz - wy;
        m.M32 = yz + wx;
        m.M33 = 1.0f - (xx + yy);

        m.M44 = 1.0f;
            
        // m[0][0] = 1.0 - (yy + zz);
        // m[0][1] = xy - wz;
        // m[0][2] = xz + wy;
        //
        // m[1][0] = xy + wz;
        // m[1][1] = 1.0 - (xx + zz);
        // m[1][2] = yz - wx;
        //
        // m[2][0] = xz - wy;
        // m[2][1] = yz + wx;
        // m[2][2] = 1.0 - (xx + yy);
        //
        // m[3][3] = 1.0;

        return m;
    }
}