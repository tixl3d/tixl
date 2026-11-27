using System;
using Lib.Utils.StraightSkeleton.Primitives;

namespace Lib.Utils.StraightSkeleton.Events
{
    internal abstract class SkeletonEvent
    {
        public Vector2d V;

        public double Distance { get; protected set; }
        public abstract bool IsObsolete { get; }

        protected SkeletonEvent(Vector2d point, double distance)
        {
            V = point;
            Distance = distance;
        }

        public override String ToString()
        {
            return "IntersectEntry [V=" + V + ", Distance=" + Distance + "]";
        }
    }
}