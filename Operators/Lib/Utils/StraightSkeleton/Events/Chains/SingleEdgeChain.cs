using Lib.Utils.StraightSkeleton.Circular;

namespace Lib.Utils.StraightSkeleton.Events.Chains
{
    internal class SingleEdgeChain : IChain
    {
        private readonly Vertex _nextVertex;
        private readonly Edge _oppositeEdge;
        private readonly Vertex _previousVertex;

        public SingleEdgeChain(Edge oppositeEdge, Vertex nextVertex)
        {
            _oppositeEdge = oppositeEdge;
            _nextVertex = nextVertex;

            // previous vertex for opposite edge event is valid only before
            // processing of multi split event start. We need to store vertex before
            // processing starts.
            _previousVertex = nextVertex.Previous as Vertex;
        }

        public Edge PreviousEdge { get { return _oppositeEdge; } }

        public Edge NextEdge { get { return _oppositeEdge; } }

        public Vertex PreviousVertex { get { return _previousVertex; } }

        public Vertex NextVertex { get { return _nextVertex; } }

        public Vertex CurrentVertex { get { return null; } }

        public ChainType ChainType { get { return ChainType.Split; } }
    }
}