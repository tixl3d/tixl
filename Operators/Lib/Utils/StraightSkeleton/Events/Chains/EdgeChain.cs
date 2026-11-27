using System.Collections.Generic;
using Lib.Utils.StraightSkeleton.Circular;

namespace Lib.Utils.StraightSkeleton.Events.Chains
{
    internal class EdgeChain : IChain
    {
        private readonly bool _closed;

        public EdgeChain(List<EdgeEvent> edgeList)
        {
            EdgeList = edgeList;
            _closed = PreviousVertex == NextVertex;
        }

        public List<EdgeEvent> EdgeList { get; private set; }

        public Edge PreviousEdge
        {
            get { return EdgeList[0].PreviousVertex.PreviousEdge; }
        }

        public Edge NextEdge
        {
            get { return EdgeList[EdgeList.Count - 1].NextVertex.NextEdge; }
        }

        public Vertex PreviousVertex
        {
            get { return EdgeList[0].PreviousVertex; }
        }

        public Vertex NextVertex
        {
            get { return EdgeList[EdgeList.Count - 1].NextVertex; }
        }

        public Vertex CurrentVertex
        {
            get { return null; }
        }

        public ChainType ChainType
        {
            get { return _closed ? ChainType.ClosedEdge : ChainType.Edge; }
        }
    }
}