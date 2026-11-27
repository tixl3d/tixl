using Lib.Utils.StraightSkeleton.Primitives;

namespace Lib.Utils.StraightSkeleton.Circular
{
    internal class Edge : CircularNode
    {
        public readonly Vector2d Begin;
        public readonly Vector2d End;
        public readonly Vector2d Norm;

        internal readonly LineLinear2d LineLinear2d;
        internal LineParametric2d BisectorNext;
        internal LineParametric2d BisectorPrevious;

        public Edge(Vector2d begin, Vector2d end)
        {
            Begin = begin;
            End = end;

            LineLinear2d = new LineLinear2d(begin, end);
            Norm = (end - begin).Normalized(); 
        }

        public override string ToString()
        {
            return "Edge [p1=" + Begin + ", p2=" + End + "]";
        }
    }
}