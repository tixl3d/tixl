namespace Lib.Utils.StraightSkeleton.Path
{
    internal class PathQueueNode<T> where T : PathQueueNode<T>
    {
        public PathQueue<T> List;
        public PathQueueNode<T> Next;
        public PathQueueNode<T> Previous;

        public bool IsEnd
        {
            get { return Next == null || Previous == null; }
        }

        public void AddPush(PathQueueNode<T> node)
        {
            List.AddPush(this, node);
        }

        public PathQueueNode<T> AddQueue(PathQueueNode<T> queue)
        {
            if (List == queue.List)
                return null;

            var currentQueue = this;

            var current = queue;

            while (current != null)
            {
                var next = current.Pop();
                currentQueue.AddPush(current);
                currentQueue = current;

                current = next;
            }

            return currentQueue;
        }

        public PathQueueNode<T> FindEnd()
        {
            if (IsEnd)
                return this;

            var current = this;
            while (current.Previous != null)
                current = current.Previous;

            return current;
        }

        public PathQueueNode<T> Pop()
        {
            return List.Pop(this);
        }
    }
}