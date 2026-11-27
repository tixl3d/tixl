using Lib.Utils.StraightSkeleton.Circular;

namespace Lib.Utils.StraightSkeleton.Events.Chains
{
    internal interface IChain
    {
        Edge PreviousEdge { get; }
        Edge NextEdge { get; }
        Vertex PreviousVertex { get; }
        Vertex NextVertex { get; }
        Vertex CurrentVertex { get; }
        ChainType ChainType { get; }
    }
}