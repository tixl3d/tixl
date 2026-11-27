using System.Collections.Generic;
using Lib.Utils.StraightSkeleton.Circular;
using Lib.Utils.StraightSkeleton.Primitives;

namespace Lib.Utils.StraightSkeleton
{
    internal class EdgeResult
    {
        public readonly Edge Edge;
        public readonly List<Vector2d> Polygon;

        public EdgeResult(Edge edge, List<Vector2d> polygon)
        {
            Edge = edge;
            Polygon = polygon;
        }
    }
}