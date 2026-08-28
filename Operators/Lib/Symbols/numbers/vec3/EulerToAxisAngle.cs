namespace Lib.numbers.vec3;

[Guid("185958a3-be54-499d-a105-cad22c0dd448")]
internal sealed class EulerToAxisAngle : Instance<EulerToAxisAngle>
{
    [Output(Guid = "bf1ea1fa-cd5d-4bdf-b0cc-34042e4fd8df")]
    public readonly Slot<Vector3> Axis = new();

    [Output(Guid = "7A400997-BCFC-4575-BC08-FBEFD5807F27")]
    public readonly Slot<float> Angle = new();

    public EulerToAxisAngle()
    {
        Axis.UpdateAction += Update;
        Angle.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // from https://www.euclideanspace.com/maths/geometry/rotations/conversions/eulerToAngle/
        var eulerAngles = Rotation.GetValue(context);
        var heading = eulerAngles.X;
        var attitude = eulerAngles.Y;
        var bank = eulerAngles.Z;
            
        // Assuming the angles are in radians.
        var c1 = Math.Cos(heading / 2);
        var s1 = Math.Sin(heading / 2);
        var c2 = Math.Cos(attitude / 2);
        var s2 = Math.Sin(attitude / 2);
        var c3 = Math.Cos(bank / 2);
        var s3 = Math.Sin(bank / 2);
        var c1c2 = c1 * c2;
        var s1s2 = s1 * s2;
        var w = c1c2 * c3 - s1s2 * s3;
        var x = c1c2 * s3 + s1s2 * c3;
        var y = s1 * c2 * c3 + c1 * s2 * s3;
        var z = c1 * s2 * c3 - s1 * c2 * s3;
        var angle = 2 * Math.Acos(w);
        var norm = x * x + y * y + z * z;
        if (norm < 0.001)
        {
            // when all euler angles are zero angle =0 so
            // we can set axis to anything to avoid divide by zero
            x = 1;
            y = z = 0;
        }
        else
        {
            norm = Math.Sqrt(norm);
            x /= norm;
            y /= norm;
            z /= norm;
        }

        Axis.Value = new Vector3((float)x, (float)y, (float)z);
        Angle.Value = (float)angle;
        Axis.DirtyFlag.Clear();
        Angle.DirtyFlag.Clear();
    }

    [Input(Guid = "30AB9590-D1E2-4926-BF01-B7395A719056")]
    public readonly InputSlot<Vector3> Rotation = new();
}