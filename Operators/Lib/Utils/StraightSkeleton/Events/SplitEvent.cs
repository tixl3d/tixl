using System;
using Lib.Utils.StraightSkeleton.Circular;
using Lib.Utils.StraightSkeleton.Primitives;

namespace Lib.Utils.StraightSkeleton.Events
{
    internal class SplitEvent : SkeletonEvent
    {
        public readonly Edge OppositeEdge;
        public readonly Vertex Parent;

        public SplitEvent(Vector2d point, double distance, Vertex parent, Edge oppositeEdge)
            : base(point, distance)
        {
            Parent = parent;
            OppositeEdge = oppositeEdge;
        }

        public override bool IsObsolete { get { return Parent.IsProcessed; } }


        public override String ToString()
        {
            return "SplitEvent [V=" + V + ", Parent=" + (Parent != null ? Parent.Point.ToString() : "null") +
                   ", Distance=" + Distance + "]";
        }
    }
}