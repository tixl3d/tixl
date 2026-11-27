namespace Lib.Utils.StraightSkeleton.Circular
{
    internal class CircularNode
    {
        internal ICircularList List;

        public CircularNode Next;
        public CircularNode Previous;

        public void AddNext(CircularNode node)
        {
            List.AddNext(this, node);
        }

        public void AddPrevious(CircularNode node)
        {
            List.AddPrevious(this, node);
        }

        public void Remove()
        {
            List.Remove(this);
        }
    }
}