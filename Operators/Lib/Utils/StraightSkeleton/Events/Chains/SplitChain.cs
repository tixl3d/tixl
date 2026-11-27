using Lib.Utils.StraightSkeleton.Circular;

namespace Lib.Utils.StraightSkeleton.Events.Chains
{
    internal class SplitChain : IChain
    {
        private readonly SplitEvent _splitEvent;

        public SplitChain(SplitEvent @event)
        {
            _splitEvent = @event;
        }

        public Edge OppositeEdge
        {
            get
            {
                if (!(_splitEvent is VertexSplitEvent))
                    return _splitEvent.OppositeEdge;

                return null;
            }
        }

        public Edge PreviousEdge
        {
            get { return _splitEvent.Parent.PreviousEdge; }
        }

        public Edge NextEdge
        {
            get { return _splitEvent.Parent.NextEdge; }
        }

        public Vertex PreviousVertex
        {
            get { return _splitEvent.Parent.Previous as Vertex; }
        }

        public Vertex NextVertex
        {
            get { return _splitEvent.Parent.Next as Vertex; }
        }

        public Vertex CurrentVertex
        {
            get { return _splitEvent.Parent; }
        }

        public ChainType ChainType
        {
            get { return ChainType.Split; }
        }
    }
}