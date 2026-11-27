using System.Collections.Generic;
using Lib.Utils.StraightSkeleton.Events.Chains;
using Lib.Utils.StraightSkeleton.Primitives;

namespace Lib.Utils.StraightSkeleton.Events
{
    internal class MultiSplitEvent : SkeletonEvent
    {
        public readonly List<IChain> Chains;

        public override bool IsObsolete { get { return false; } }

        public MultiSplitEvent(Vector2d point, double distance, List<IChain> chains)
            : base(point, distance)
        {
            Chains = chains;
        }
    }
}