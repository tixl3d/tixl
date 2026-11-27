using System;
using System.Collections.Generic;

namespace Lib.Utils.StraightSkeleton.Path
{
    internal class PathQueue<T> where T : PathQueueNode<T>
    {
        public int Size { get; private set; }
        public PathQueueNode<T> First { get; private set; }

        public virtual void AddPush(PathQueueNode<T> node, PathQueueNode<T> newNode)
        {
            if (newNode.List != null)
                throw new InvalidOperationException("Node is already assigned to different list!");

            if (node.Next != null && node.Previous != null)
                throw new InvalidOperationException("Can't push new node. Node is inside a Quere. " +
                                                    "New node can by added only at the end of queue.");

            newNode.List = this;
            Size++;

            if (node.Next == null)
            {
                newNode.Previous = node;
                newNode.Next = null;

                node.Next = newNode;
            }
            else
            {
                newNode.Previous = null;
                newNode.Next = node;

                node.Previous = newNode;
            }
        }

        public void AddFirst(T node)
        {
            if (node.List != null)
                throw new InvalidOperationException("Node is already assigned to different list!");

            if (First == null)
            {
                First = node;

                node.List = this;
                node.Next = null;
                node.Previous = null;

                Size++;
            }
            else
                throw new InvalidOperationException("First element already exist!");
        }

        public PathQueueNode<T> Pop(PathQueueNode<T> node)
        {
            if (node.List != this)
                throw new InvalidOperationException("Node is not assigned to this list!");

            if (Size <= 0)
                throw new InvalidOperationException("List is empty can't remove!");

            if (!node.IsEnd)
                throw new InvalidOperationException("Can pop only from end of queue!");

            node.List = null;

            PathQueueNode<T> previous = null;

            if (Size == 1)
                First = null;
            else
            {
                if (First == node)
                {
                    if (node.Next != null)
                        First = node.Next;
                    else if (node.Previous != null)
                        First = node.Previous;
                    else
                        throw new InvalidOperationException("Ups ?");
                }
                if (node.Next != null)
                {
                    node.Next.Previous = null;
                    previous = node.Next;
                }
                else if (node.Previous != null)
                {
                    node.Previous.Next = null;
                    previous = node.Previous;
                }
            }

            node.Previous = null;
            node.Next = null;

            Size--;
            return previous;
        }

        public IEnumerable<T> Iterate()
        {
            T current = (T) (First != null ? First.FindEnd() : null);
            int i = 0;
            while (current != null)
            {
                yield return current;
                if (++i == Size)
                    yield break;
                current = current.Next as T;
            }
        }
    }
}