using T3.Core.Utils;
using T3.Core.Utils.Splines;

namespace Lib.point.helper;

[Guid("6f65e325-21cc-4bc5-9aea-4a691476e3bf")]
internal sealed class SampleCpuPoints : Instance<SampleCpuPoints>
{
    [Output(Guid = "4EC76FD1-A89E-4FE4-AF6D-E0F2D2DAAA1C")]
    public readonly Slot<StructuredList> ResultPoint = new();

    public SampleCpuPoints()
    {
        ResultPoint.UpdateAction += Update;
        ResultPoint.Value = _result;
    }

    private readonly StructuredList<Point> _result = new(1);

    private void Update(EvaluationContext context)
    {
        var points = PointList.GetValue(context);
        if (points is not StructuredList<Point> pointList || pointList.NumElements == 0)
            return;

        var samplePosition = SamplePos.GetValue(context);
        if (!samplePosition._IsFinite()) // prevent NaN
            samplePosition = 0;

        //var refPoints = pointList.TypedElements;
        //var pos = BezierPointSpline.SampleCubicBezier(samplePosition, 1,  refPoints);
        
        var f = samplePosition.Clamp(0, pointList.NumElements - 1);
        var i0 = (int)f.ClampMin(0);
        var i1 = (i0 + 1).ClampMax(points.NumElements - 1);
        var a = pointList.TypedElements[i0];
        var b = pointList.TypedElements[i1];
        var t = f - i0;

        var posA = a.Position;
        var posB = b.Position;
        var d = posB - posA;
        var l = d.Length();
        if (l <= float.Epsilon)
        {
            _result.TypedElements[0] = a;
            return;
        }

        var smoothT = MathUtils.SmootherStep(0, 1, t);

        var tLength = TangentScale.GetValue(context) * l;
        var tA = Vector3.Transform(Vector3.UnitZ *tLength , Quaternion.Normalize( a.Orientation));
        var tB = Vector3.Transform(-Vector3.UnitZ *tLength, Quaternion.Normalize(b.Orientation));

        var pos = Bezier.GetPoint(posA, posA + tA, posB + tB, posB, t);
        var tan  = Bezier.GetFirstDerivative(posA, posA + tA, posB + tB, posB, t); // derivative
        
        // Up from authored key orientations
        var upA = Vector3.Transform(Vector3.UnitY, Quaternion.Normalize(a.Orientation));
        var upB = Vector3.Transform(Vector3.UnitY, Quaternion.Normalize(b.Orientation));
        var up  = SlerpUnit(upA, upB, MathUtils.SmootherStep(0,1, t));

        var pUpA = posA + upA * tLength;
        var pUpB = posB + upB * tLength;

        //var up = Bezier.GetPoint(pUpA, upA + tA, pUpB + tB, pUpB, smoothT);
        
        // Z-forward alignment
        //var orientation = LookAtRH_ZForward(tan, up);
        //var orientation = ComputeOrientation(a.Orientation, b.Orientation, tan,  MathUtils.SmootherStep(0,1,t));
        var orientation = ComputeOrientation(a.Orientation, b.Orientation, tan,  t);
            
        var p = new Point
                    {
                        Position = pos,
                        Orientation = orientation,
                    };

        _result.TypedElements[0] = p;
    }
    

    public static Quaternion LookAtRH_ZForward(Vector3 forward, Vector3 upHint)
    {
        var f = Vector3.Normalize(forward);

        // make up orthogonal to forward (stable when up≈forward)
        var u = upHint - f * Vector3.Dot(upHint, f);
        if (u.LengthSquared() < 1e-8f)
            u = MathF.Abs(f.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        u = Vector3.Normalize(u);

        // RH basis: right = up × forward
        var r = Vector3.Normalize(Vector3.Cross(u, f));
        u = Vector3.Cross(f, r);

        // rows: right, up, forward
        float m00 = r.X, m01 = r.Y, m02 = r.Z;
        float m10 = u.X, m11 = u.Y, m12 = u.Z;
        float m20 = f.X, m21 = f.Y, m22 = f.Z;

        // quaternion from rotation matrix (unchanged math)
        float trace = m00 + m11 + m22;
        Quaternion q;
        if (trace > 0f) {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            q.W = 0.25f * s;
            q.X = (m12 - m21) / s;
            q.Y = (m20 - m02) / s;
            q.Z = (m01 - m10) / s;
        } else if (m00 > m11 && m00 > m22) {
            float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
            q.W = (m12 - m21) / s;
            q.X = 0.25f * s;
            q.Y = (m01 + m10) / s;
            q.Z = (m02 + m20) / s;
        } else if (m11 > m22) {
            float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
            q.W = (m20 - m02) / s;
            q.X = (m01 + m10) / s;
            q.Y = 0.25f * s;
            q.Z = (m12 + m21) / s;
        } else {
            float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
            q.W = (m01 - m10) / s;
            q.X = (m02 + m20) / s;
            q.Y = (m12 + m21) / s;
            q.Z = 0.25f * s;
        }
        return Quaternion.Normalize(q);
    }
    
    
    
    private static Quaternion ComputeOrientation(
        Quaternion qa, Quaternion qb,
        Vector3 bezierTangent, float t)
    {
        var f = Vector3.Normalize(bezierTangent);                 // +Z forward

        // up from keyframes
        var upA = Vector3.Transform(Vector3.UnitY, qa);
        var upB = Vector3.Transform(Vector3.UnitY, qb);
        var up  = SlerpUnitWithRef(upA, upB, t, f);               // deterministic axis
        //var up = Vector3.One;
        

        // make up orthogonal to forward
        up -= f * Vector3.Dot(up, f);
        if (up.LengthSquared() < 1e-8f)
            up = MathF.Abs(f.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        up = Vector3.Normalize(up);

        // RH basis: right = up × forward, up = forward × right
        var right = Vector3.Normalize(Vector3.Cross(up, f));
        up        = Vector3.Cross(f, right);

        var m = new Matrix4x4(
                              right.X, right.Y, right.Z, 0,
                              up.X,    up.Y,    up.Z,    0,
                              f.X,     f.Y,     f.Z,     0,
                              0, 0, 0, 1);

        return Quaternion.CreateFromRotationMatrix(m);
    }

    private static Vector3 SlerpUnitWithRef(Vector3 a, Vector3 b, float t, Vector3 refAxis)
    {
        a = Vector3.Normalize(a);
        b = Vector3.Normalize(b);
        float dot = MathUtils.Clamp(Vector3.Dot(a, b), -1f, 1f);

        if (dot > 0.9995f)
            return Vector3.Normalize(Vector3.Lerp(a, b, t));

        if (dot < -0.9995f)
        {
            // choose axis using the path tangent to avoid frame-to-frame flips
            var axis = Vector3.Cross(refAxis, a);
            if (axis.LengthSquared() < 1e-8f)                       // tangent ∥ a
                axis = Vector3.Cross(MathF.Abs(a.X) < 0.1f ? Vector3.UnitX : Vector3.UnitY, a);
            axis = Vector3.Normalize(axis);
            return RotateAroundAxis(a, axis, MathF.PI * t);
        }

        float theta = MathF.Acos(dot);
        float s = MathF.Sin(theta);
        return a * (MathF.Sin((1 - t) * theta) / s) + b * (MathF.Sin(t * theta) / s);
    }

    
    private static Vector3 SlerpUnit(Vector3 a, Vector3 b, float t)
    {
        a = Vector3.Normalize(a);
        b = Vector3.Normalize(b);

        var dot = Vector3.Dot(a, b).Clamp(-1f, 1f);

        switch (dot)
        {
            // nearly identical: nlerp is fine
            case > 0.9995f:
            {
                Log.Debug(" Case A " + dot);
                return Vector3.Normalize(Vector3.Lerp(a, b, t));
            }
            // nearly opposite: rotate a around an arbitrary orthogonal axis
            case < -0.9995f:
            {
                Log.Debug(" Case B " + dot);
                var ortho = MathF.Abs(a.X) < 0.1f ? Vector3.UnitX : Vector3.UnitY;
                var axis = Vector3.Normalize(Vector3.Cross(a, ortho));
                return RotateAroundAxis(a, axis, MathF.PI * t);
            }
        }

        float theta = MathF.Acos(dot);
        float sinTheta = MathF.Sin(theta);
        float w1 = MathF.Sin((1 - t) * theta) / sinTheta;
        float w2 = MathF.Sin(t * theta) / sinTheta;
        return a * w1 + b * w2;
    }

    static Vector3 RotateAroundAxis(Vector3 v, Vector3 axis, float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return v * c + Vector3.Cross(axis, v) * s + axis * Vector3.Dot(axis, v) * (1 - c);
    }
    
    

    [Input(Guid = "8cf06759-9c93-438f-ae5f-12a55a29b347")]
    public readonly InputSlot<StructuredList> PointList = new();

    [Input(Guid = "6412d80e-d6fd-4c47-a8a4-6b88b5da95a5")]
    public readonly InputSlot<float> SamplePos = new();
    
    [Input(Guid = "1BD99405-7FE5-4712-9EF2-6E66B8D41AEB")]
    public readonly InputSlot<float> TangentScale = new();
}