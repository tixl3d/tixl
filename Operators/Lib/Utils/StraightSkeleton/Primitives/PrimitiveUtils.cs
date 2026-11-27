using System;
using System.Collections.Generic;

namespace Lib.Utils.StraightSkeleton.Primitives
{
    internal static class PrimitiveUtils
    {
        #region Vector specific

        public static Vector2d FromTo(Vector2d begin, Vector2d end)
        {
            return new Vector2d(end.X - begin.X, end.Y - begin.Y);
        }

        public static Vector2d OrthogonalLeft(Vector2d v)
        {
            return new Vector2d(-v.Y, v.X);
        }

        public static Vector2d OrthogonalRight(Vector2d v)
        {
            return new Vector2d(v.Y, -v.X);
        }

        /// <summary>
        ///     <see href="http://en.wikipedia.org/wiki/Vector_projection" />
        /// </summary>
        public static Vector2d OrthogonalProjection(Vector2d unitVector, Vector2d vectorToProject)
        {
            var n = new Vector2d(unitVector).Normalized();

            var px = vectorToProject.X;
            var py = vectorToProject.Y;

            var ax = n.X;
            var ay = n.Y;

            return new Vector2d(px * ax * ax + py * ax * ay, px * ax * ay + py * ay * ay);
        }

        public static Vector2d BisectorNormalized(Vector2d norm1, Vector2d norm2)
        {
            var e1v = OrthogonalLeft(norm1);
            var e2v = OrthogonalLeft(norm2);

            // 90 - 180 || 180 - 270
            if (norm1.Dot(norm2) > 0)
                return e1v + e2v;

            // 0 - 180
            var ret = new Vector2d(norm1);
            ret.Negate();
            ret += norm2;

            // 270 - 360
            if (e1v.Dot(norm2) < 0)
                ret.Negate();

            return ret;
        }

        #endregion

        #region Ray specific

        /// <summary> Error epsilon. Anything that avoids division. </summary>
        private const double SmallNum = 0.00000001;

        /// <summary> Return value if there is no intersection. </summary>
        private static readonly IntersectPoints Empty = new IntersectPoints();

        public static bool IsPointOnRay(Vector2d point, LineParametric2d ray, double epsilon)
        {
            var rayDirection = new Vector2d(ray.U).Normalized();
            // test if point is on ray
            var pointVector = point - ray.A;

            var dot = rayDirection.Dot(pointVector);

            if (dot < epsilon)
                return false;

            var x = rayDirection.X;
            rayDirection.X = rayDirection.Y;
            rayDirection.Y = -x;

            dot = rayDirection.Dot(pointVector);

            return -epsilon < dot && dot < epsilon;
        }

        /// <summary>
        ///     Calculate intersection points for rays. It can return more then one
        ///     intersection point when rays overlaps.
        ///     <see href="http://geomalgorithms.com/a05-_intersect-1.html" />
        ///     <see href="http://softsurfer.com/Archive/algorithm_0102/algorithm_0102.htm" />
        /// </summary>
        /// <returns>class with intersection points. It never return null.</returns>
        public static IntersectPoints IntersectRays2D(LineParametric2d r1, LineParametric2d r2)
        {
            var s1p0 = r1.A;
            var s1p1 = r1.A + r1.U;

            var s2p0 = r2.A;

            var u = r1.U;
            var v = r2.U;

            var w = s1p0 - s2p0;
            var d = Perp(u, v);

            // test if they are parallel (includes either being a point)
            if (Math.Abs(d) < SmallNum)
            {
                // they are NOT collinear
                // S1 and S2 are parallel
                if (Perp(u, w) != 0 || Perp(v, w) != 0)
                    return Empty;

                // they are collinear or degenerate
                // check if they are degenerate points
                var du = Dot(u, u);
                var dv = Dot(v, v);
                if (du == 0 && dv == 0)
                {
                    // both segments are points
                    if (s1p0 != s2p0)
                        return Empty;

                    // they are the same point
                    return new IntersectPoints(s1p0);
                }
                if (du == 0)
                {
                    // S1 is a single point
                    if (!InCollinearRay(s1p0, s2p0, v))
                        return Empty;

                    return new IntersectPoints(s1p0);
                }
                if (dv == 0)
                {
                    // S2 a single point
                    if (!InCollinearRay(s2p0, s1p0, u))
                        return Empty;

                    return new IntersectPoints(s2p0);
                }
                // they are collinear segments - get overlap (or not)
                double t0, t1;
                // endpoints of S1 in eqn for S2
                var w2 = s1p1 - s2p0;
                if (v.X != 0)
                {
                    t0 = w.X / v.X;
                    t1 = w2.X / v.X;
                }
                else
                {
                    t0 = w.Y / v.Y;
                    t1 = w2.Y / v.Y;
                }
                if (t0 > t1)
                {
                    // must have t0 smaller than t1
                    var t = t0;
                    t0 = t1;
                    t1 = t; // swap if not
                }
                if (t1 < 0)
                    // NO overlap
                    return Empty;

                // clip to min 0
                t0 = t0 < 0 ? 0 : t0;

                if (t0 == t1)
                {
                    // intersect is a point
                    var I0 = new Vector2d(v);
                    I0 *= t0;
                    I0 += s2p0;

                    return new IntersectPoints(I0);
                }

                // they overlap in a valid subsegment

                // I0 = S2_P0 + t0 * v;
                var I_0 = new Vector2d(v);
                I_0 *= t0;
                I_0 += s2p0;

                // I1 = S2_P0 + t1 * v;
                var I1 = new Vector2d(v);
                I1 *= t1;
                I1 += s2p0;

                return new IntersectPoints(I_0, I1);
            }

            // the segments are skew and may intersect in a point
            // get the intersect parameter for S1
            var sI = Perp(v, w) / d;
            if (sI < 0 /* || sI > 1 */)
                return Empty;

            // get the intersect parameter for S2
            var tI = Perp(u, w) / d;
            if (tI < 0 /* || tI > 1 */)
                return Empty;

            // I0 = S1_P0 + sI * u; // compute S1 intersect point
            var IO = new Vector2d(u);
            IO *= sI;
            IO += s1p0;

            return new IntersectPoints(IO);
        }

        private static bool InCollinearRay(Vector2d p, Vector2d rayStart, Vector2d rayDirection)
        {
            // test if point is on ray
            var collideVector = p - rayStart;
            var dot = rayDirection.Dot(collideVector);

            return !(dot < 0);
        }

        private static double Dot(Vector2d u, Vector2d v)
        {
            return u.Dot(v);
        }

        /// <summary> Perp Dot Product. </summary>
        private static double Perp(Vector2d u, Vector2d v)
        {
            return u.X * v.Y - u.Y * v.X;
        }

        internal class IntersectPoints
        {
            /// <summary> Intersection point or begin of intersection segment. </summary>
            public readonly Vector2d Intersect;

            /// <summary> Intersection end. </summary>
            public readonly Vector2d IntersectEnd;

            public IntersectPoints(Vector2d intersect, Vector2d intersectEnd)
            {
                Intersect = intersect;
                IntersectEnd = intersectEnd;
            }

            public IntersectPoints(Vector2d intersect)
                : this(intersect, Vector2d.Empty)
            {
            }

            public IntersectPoints()
                : this(Vector2d.Empty, Vector2d.Empty)
            {
            }
        }

        #endregion

        #region Polygon specific

        /// <summary> Check if polygon is clockwise. </summary>
        /// <param name="polygon"> List of polygon points. </param>
        /// <returns> If polygon is clockwise. </returns>
        public static bool IsClockwisePolygon(List<Vector2d> polygon)
        {
            return Area(polygon) < 0;
        }

        /// <summary> Calculate area of polygon outline. For clockwise are will be less then. </summary>
        /// <param name="polygon">List of polygon points.</param>
        /// <returns> Area. </returns>
        private static double Area(List<Vector2d> polygon)
        {
            var n = polygon.Count;
            double A = 0.0f;
            for (int p = n - 1, q = 0; q < n; p = q++)
                A += polygon[p].X * polygon[q].Y - polygon[q].X * polygon[p].Y;

            return A * 0.5f;
        }

        /// <summary> Always returns points ordered as counter clockwise. </summary>
        /// <param name="polygon"> Polygon as list of points. </param>
        /// <returns> Counter clockwise polygon.</returns>
        public static List<Vector2d> MakeCounterClockwise(List<Vector2d> polygon)
        {
            if (IsClockwisePolygon(polygon))
                polygon.Reverse();
            return polygon;
        }

        /// <summary>
        ///     Test if point is inside polygon.
        ///     <see href="http://en.wikipedia.org/wiki/Point_in_polygon" />
        ///     <see href="http://en.wikipedia.org/wiki/Even-odd_rule" />
        ///     <see href="http://paulbourke.net/geometry/insidepoly/" />
        /// </summary>
        public static bool IsPointInsidePolygon(Vector2d point, List<Vector2d> points)
        {
            var numpoints = points.Count;

            if (numpoints < 3)
                return false;

            var it = 0;
            var first = points[it];
            var oddNodes = false;

            for (var i = 0; i < numpoints; i++)
            {
                var node1 = points[it];
                it++;
                var node2 = i == numpoints - 1 ? first : points[it];

                var x = point.X;
                var y = point.Y;

                if (node1.Y < y && node2.Y >= y || node2.Y < y && node1.Y >= y)
                {
                    if (node1.X + (y - node1.Y) / (node2.Y - node1.Y) * (node2.X - node1.X) < x)
                        oddNodes = !oddNodes;
                }
            }

            return oddNodes;
        }

        #endregion
    }
}
