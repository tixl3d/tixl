#nullable enable
using System.Numerics;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Least-squares refinement of a projector's pose against its aimed points. Calibration is editor-only; the solved
/// pose is stored in the setup and read by the operators, so this stays out of Core.
/// </summary>
internal static class ProjectorPoseRefinement
{
    /// <summary>
    /// Moves <paramref name="pose"/> to where the lens reproduces the measured pixels best, in the least-squares
    /// sense (Levenberg–Marquardt). The plane solves read the pose from the homography in closed form, which is
    /// exact for perfect data but spreads noise unevenly; this minimizes the miss on the points themselves.
    /// Leaves the pose unchanged when it can't improve it, e.g. with points behind the projector.
    /// </summary>
    public static void Refine(IReadOnlyList<CalibrationPoint> points, in Projection lens, Int2 canvasResolution, ref Pose pose)
    {
        var count = points.Count;
        if (count < 3 || canvasResolution.Width <= 0 || canvasResolution.Height <= 0)
            return;

        var model = new PinholeModel(lens, canvasResolution);
        var rotation = Matrix3D.FromQuaternion(pose.Orientation);
        var position = new Vector3D(pose.Position.X, pose.Position.Y, pose.Position.Z);

        var residualCount = count * 2;
        Span<double> residuals = residualCount <= 64 ? stackalloc double[residualCount] : new double[residualCount];
        Span<double> trial = residualCount <= 64 ? stackalloc double[residualCount] : new double[residualCount];
        Span<double> jacobian = residualCount <= 64 ? stackalloc double[residualCount * 6] : new double[residualCount * 6];
        Span<double> normal = stackalloc double[36];
        Span<double> gradient = stackalloc double[6];
        Span<double> step = stackalloc double[6];

        if (!TryComputeResiduals(points, model, rotation, position, residuals, out var cost))
            return;

        var damping = 1e-3;
        for (var iteration = 0; iteration < MaxRefineIterations; iteration++)
        {
            // Numeric derivatives: six parameters, a handful of points, solved only when the pin changes.
            for (var parameter = 0; parameter < 6; parameter++)
            {
                Perturb(rotation, position, parameter, DerivativeStep, out var perturbedRotation, out var perturbedPosition);
                if (!TryComputeResiduals(points, model, perturbedRotation, perturbedPosition, trial, out _))
                    return;

                for (var row = 0; row < residualCount; row++)
                {
                    jacobian[row * 6 + parameter] = (trial[row] - residuals[row]) / DerivativeStep;
                }
            }

            normal.Clear();
            gradient.Clear();
            for (var row = 0; row < residualCount; row++)
            {
                for (var a = 0; a < 6; a++)
                {
                    var ja = jacobian[row * 6 + a];
                    gradient[a] += ja * residuals[row];
                    for (var b = 0; b < 6; b++)
                    {
                        normal[a * 6 + b] += ja * jacobian[row * 6 + b];
                    }
                }
            }

            var isImproved = false;
            while (damping < 1e10)
            {
                if (!TrySolveDamped(normal, gradient, damping, step))
                {
                    damping *= 10;
                    continue;
                }

                Perturb(rotation, position, step, out var nextRotation, out var nextPosition);
                if (TryComputeResiduals(points, model, nextRotation, nextPosition, trial, out var nextCost) && nextCost < cost)
                {
                    var gain = cost - nextCost;
                    rotation = nextRotation;
                    position = nextPosition;
                    cost = nextCost;
                    trial.CopyTo(residuals);
                    damping = Math.Max(damping * 0.1, 1e-12);
                    isImproved = gain > cost * 1e-10;
                    break;
                }

                damping *= 10;
            }

            if (!isImproved)
                break;
        }

        pose = new Pose(new Vector3((float)position.X, (float)position.Y, (float)position.Z), rotation.ToQuaternion());
    }

    private const int MaxRefineIterations = 50;

    /** Metres and radians: small enough for a clean derivative, far above double rounding. */
    private const double DerivativeStep = 1e-6;

    /** The forward model of <see cref="TryProjectToPixel"/>, in doubles. */
    private readonly struct PinholeModel
    {
        public PinholeModel(in Projection lens, Int2 canvas)
        {
            _yScale = 1 / Math.Tan(Math.Clamp(lens.FieldOfViewY, 1e-4f, MathF.PI - 1e-4f) * 0.5);
            _aspect = canvas.Width / (double)canvas.Height;
            _shiftX = lens.LensShift.X;
            _shiftY = lens.LensShift.Y;
            _width = canvas.Width;
            _height = canvas.Height;
        }

        /** Camera space looks down −z; false for points behind the lens. */
        public bool TryProject(in Vector3D cameraPoint, out double u, out double v)
        {
            u = v = 0;
            var depth = -cameraPoint.Z;
            if (depth <= 1e-9)
                return false;

            var ndcX = _yScale / _aspect * cameraPoint.X / depth + _shiftX;
            var ndcY = _yScale * cameraPoint.Y / depth + _shiftY;
            u = (ndcX * 0.5 + 0.5) * _width;
            v = (0.5 - ndcY * 0.5) * _height;
            return true;
        }

        private readonly double _yScale;
        private readonly double _aspect;
        private readonly double _shiftX;
        private readonly double _shiftY;
        private readonly double _width;
        private readonly double _height;
    }

    private static bool TryComputeResiduals(IReadOnlyList<CalibrationPoint> points, in PinholeModel model, in Matrix3D rotation, in Vector3D position,
                                            Span<double> residuals, out double cost)
    {
        cost = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var offset = new Vector3D(point.StagePosition.X - position.X, point.StagePosition.Y - position.Y, point.StagePosition.Z - position.Z);
            if (!model.TryProject(rotation.TransposeTimes(offset), out var u, out var v))
                return false;

            var du = u - point.OutputPixel.X;
            var dv = v - point.OutputPixel.Y;
            residuals[index * 2] = du;
            residuals[index * 2 + 1] = dv;
            cost += du * du + dv * dv;
        }

        return true;
    }

    /** Parameters 0–2 rotate about the camera's own axes, 3–5 move it along the stage axes. */
    private static void Perturb(in Matrix3D rotation, in Vector3D position, int parameter, double amount, out Matrix3D nextRotation, out Vector3D nextPosition)
    {
        Span<double> delta = stackalloc double[6];
        delta[parameter] = amount;
        Perturb(rotation, position, delta, out nextRotation, out nextPosition);
    }

    private static void Perturb(in Matrix3D rotation, in Vector3D position, ReadOnlySpan<double> delta, out Matrix3D nextRotation, out Vector3D nextPosition)
    {
        nextRotation = rotation.Times(Matrix3D.FromRotationVector(delta[0], delta[1], delta[2]));
        nextPosition = new Vector3D(position.X + delta[3], position.Y + delta[4], position.Z + delta[5]);
    }

    /** Solves (JᵀJ + λ·diag(JᵀJ))·step = −Jᵀr by Gaussian elimination with partial pivoting. */
    private static bool TrySolveDamped(ReadOnlySpan<double> normal, ReadOnlySpan<double> gradient, double damping, Span<double> step)
    {
        Span<double> system = stackalloc double[42];
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 6; column++)
            {
                system[row * 7 + column] = normal[row * 6 + column];
            }

            system[row * 7 + row] += damping * Math.Max(normal[row * 6 + row], 1e-12);
            system[row * 7 + 6] = -gradient[row];
        }

        for (var pivotColumn = 0; pivotColumn < 6; pivotColumn++)
        {
            var pivotRow = pivotColumn;
            for (var row = pivotColumn + 1; row < 6; row++)
            {
                if (Math.Abs(system[row * 7 + pivotColumn]) > Math.Abs(system[pivotRow * 7 + pivotColumn]))
                    pivotRow = row;
            }

            if (Math.Abs(system[pivotRow * 7 + pivotColumn]) < 1e-300)
                return false;

            if (pivotRow != pivotColumn)
            {
                for (var column = 0; column < 7; column++)
                {
                    (system[pivotRow * 7 + column], system[pivotColumn * 7 + column]) = (system[pivotColumn * 7 + column], system[pivotRow * 7 + column]);
                }
            }

            for (var row = pivotColumn + 1; row < 6; row++)
            {
                var factor = system[row * 7 + pivotColumn] / system[pivotColumn * 7 + pivotColumn];
                for (var column = pivotColumn; column < 7; column++)
                {
                    system[row * 7 + column] -= factor * system[pivotColumn * 7 + column];
                }
            }
        }

        for (var row = 5; row >= 0; row--)
        {
            var sum = system[row * 7 + 6];
            for (var column = row + 1; column < 6; column++)
            {
                sum -= system[row * 7 + column] * step[column];
            }

            step[row] = sum / system[row * 7 + row];
        }

        return true;
    }

    /** A row-major rotation matrix in doubles; columns are the camera's axes in stage space. */
    private readonly record struct Matrix3D(double M11, double M12, double M13, double M21, double M22, double M23, double M31, double M32, double M33)
    {
        public static Matrix3D FromQuaternion(Quaternion q)
        {
            double x = q.X, y = q.Y, z = q.Z, w = q.W;
            return new Matrix3D(1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
                                2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                                2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y));
        }

        /** Rodrigues' formula. */
        public static Matrix3D FromRotationVector(double x, double y, double z)
        {
            var angle = Math.Sqrt(x * x + y * y + z * z);
            if (angle < 1e-15)
                return new Matrix3D(1, -z, y, z, 1, -x, -y, x, 1);

            x /= angle;
            y /= angle;
            z /= angle;
            var c = Math.Cos(angle);
            var s = Math.Sin(angle);
            var t = 1 - c;
            return new Matrix3D(t * x * x + c, t * x * y - s * z, t * x * z + s * y,
                                t * x * y + s * z, t * y * y + c, t * y * z - s * x,
                                t * x * z - s * y, t * y * z + s * x, t * z * z + c);
        }

        public Matrix3D Times(in Matrix3D o) =>
            new(M11 * o.M11 + M12 * o.M21 + M13 * o.M31, M11 * o.M12 + M12 * o.M22 + M13 * o.M32, M11 * o.M13 + M12 * o.M23 + M13 * o.M33,
                M21 * o.M11 + M22 * o.M21 + M23 * o.M31, M21 * o.M12 + M22 * o.M22 + M23 * o.M32, M21 * o.M13 + M22 * o.M23 + M23 * o.M33,
                M31 * o.M11 + M32 * o.M21 + M33 * o.M31, M31 * o.M12 + M32 * o.M22 + M33 * o.M32, M31 * o.M13 + M32 * o.M23 + M33 * o.M33);

        /** Stage → camera: the transpose undoes the rotation. */
        public Vector3D TransposeTimes(in Vector3D v) =>
            new(M11 * v.X + M21 * v.Y + M31 * v.Z, M12 * v.X + M22 * v.Y + M32 * v.Z, M13 * v.X + M23 * v.Y + M33 * v.Z);

        public Quaternion ToQuaternion()
        {
            // System.Numerics reads rows as the images of the axes (row-vector convention): the transpose of this one.
            var m = new Matrix4x4((float)M11, (float)M21, (float)M31, 0,
                                  (float)M12, (float)M22, (float)M32, 0,
                                  (float)M13, (float)M23, (float)M33, 0,
                                  0, 0, 0, 1);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        }
    }

    /** A double-precision point for the solve, where float rounding would show up as a skewed rotation. */
    private readonly record struct Vector3D(double X, double Y, double Z);
}
