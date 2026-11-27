
namespace Lib.Utils.StraightSkeleton.Primitives
{
    /// <summary>
    ///     Geometry line in parametric form:
    ///     x = x_A + t * u_x;
    ///     y = y_A + t * u_y;
    ///     where t in R
    ///     <see href="http://en.wikipedia.org/wiki/Linear_equation" />
    /// </summary>
    internal struct LineParametric2d
    {
        public static readonly LineParametric2d Empty = new LineParametric2d(Vector2d.Empty, Vector2d.Empty);
        public Vector2d A;
        public Vector2d U;

        public LineParametric2d(Vector2d pA, Vector2d pU)
        {
            A = pA;
            U = pU;
        }

        public LineLinear2d CreateLinearForm()
        {
            var x = this.A.X;
            var y = this.A.Y;

            var B = -U.X;
            var A = U.Y;

            var C = -(A*x + B*y);
            return new LineLinear2d(A, B, C);
        }

        public static Vector2d Collide(LineParametric2d ray, LineLinear2d line, double epsilon)
        {
            var collide = LineLinear2d.Collide(ray.CreateLinearForm(), line);
            if (collide.Equals(Vector2d.Empty))
                return Vector2d.Empty;

            var collideVector = collide - ray.A;
            return ray.U.Dot(collideVector) < epsilon ? Vector2d.Empty : collide;
        }

        public bool IsOnLeftSite(Vector2d point, double epsilon)
        {
            var direction = point - A;
            return PrimitiveUtils.OrthogonalRight(U).Dot(direction) < epsilon;
        }

        public bool IsOnRightSite(Vector2d point, double epsilon)
        {
            var direction = point - A;
            return PrimitiveUtils.OrthogonalRight(U).Dot(direction) > -epsilon;
        }
    }
}