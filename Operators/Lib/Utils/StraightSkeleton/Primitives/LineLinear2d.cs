using System;

namespace Lib.Utils.StraightSkeleton.Primitives
{
    /// <summary>
    ///     Geometry line in linear form. General form:
    ///     Ax + By + C = 0;
    ///     <see href="http://en.wikipedia.org/wiki/Linear_equation"/>
    /// </summary>
    internal struct LineLinear2d
    {
        public double A;
        public double B;
        public double C;

        /// <summary> Linear line from two points on line. </summary>
        public LineLinear2d(Vector2d pP1, Vector2d pP2)
        {
            A = pP1.Y - pP2.Y;
            B = pP2.X - pP1.X;
            C = pP1.X*pP2.Y - pP2.X*pP1.Y;
        }

        /// <summary> Linear line. </summary>
        public LineLinear2d(double pA, double pB, double pC)
        {
            A = pA;
            B = pB;
            C = pC;
        }

        /// <summary> Collision point of two lines. </summary>
        /// <param name="pLine">Line to collision.</param>
        /// <returns>Collision point.</returns>
        public Vector2d Collide(LineLinear2d pLine)
        {
            return Collide(this, pLine);
        }

        /// <summary> Collision point of two lines. </summary>
        public static Vector2d Collide(LineLinear2d pLine1, LineLinear2d pLine2)
        {
            return Collide(pLine1.A, pLine1.B, pLine1.C, pLine2.A, pLine2.B, pLine2.C);
        }

        /// <summary> Collision point of two lines. </summary>
        public static Vector2d Collide(double A1, double B1, double C1, double A2, double B2, double C2)
        {
            var WAB = A1*B2 - A2*B1;
            var WBC = B1*C2 - B2*C1;
            var WCA = C1*A2 - C2*A1;

            return WAB == 0 ? Vector2d.Empty : new Vector2d(WBC / WAB, WCA / WAB);
        }

        /// <summary> Check whether point belongs to line. </summary>
        public bool Contains(Vector2d point)
        {
            return Math.Abs((point.X * A + point.Y * B + C)) < double.Epsilon;
        }
    }
}