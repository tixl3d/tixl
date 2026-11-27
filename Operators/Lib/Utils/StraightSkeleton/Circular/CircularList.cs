using System;
using System.Collections.Generic;

namespace Lib.Utils.StraightSkeleton.Circular
{
    internal interface ICircularList
    {
        int Size { get; }

        void AddNext(CircularNode node, CircularNode newNode);
        void AddPrevious(CircularNode node, CircularNode newNode);
        void AddLast(CircularNode node);
        void Remove(CircularNode node);
    }

    internal class CircularList<T> : ICircularList where T : CircularNode
    {
        private T _first;
        private int _size;

        public void AddNext(CircularNode node, CircularNode newNode)
        {
            if (newNode.List != null)
                throw new InvalidOperationException("Node is already assigned to different list!");

            newNode.List = this;

            newNode.Previous = node;
            newNode.Next = node.Next;

            node.Next.Previous = newNode;
            node.Next = newNode;

            _size++;
        }

        public void AddPrevious(CircularNode node, CircularNode newNode)
        {
            if (newNode.List != null)
                throw new InvalidOperationException("Node is already assigned to different list!");

            newNode.List = this;

            newNode.Previous = node.Previous;
            newNode.Next = node;

            node.Previous.Next = newNode;
            node.Previous = newNode;

            _size++;
        }

        public void AddLast(CircularNode node)
        {
            if (node.List != null)
                throw new InvalidOperationException("Node is already assigned to different list!");

            if (_first == null)
            {
                _first = node as T;

                node.List = this;
                node.Next = node;
                node.Previous = node;

                _size++;
            }
            else
                AddPrevious(_first, node);
        }

        public void Remove(CircularNode node)
        {
            if (node.List != this)
                throw new InvalidOperationException("Node is not assigned to this list!");

            if (_size <= 0)
                throw new InvalidOperationException("List is empty can't remove!");

            node.List = null;

            if (_size == 1)
                _first = null;

            else
            {
                if (_first == node)
                    _first = (T) _first.Next;

                node.Previous.Next = node.Next;
                node.Next.Previous = node.Previous;
            }

            node.Previous = null;
            node.Next = null;

            _size--;
        }

        public int Size {get { return _size;  } }

        public T First() { return _first; }

        public IEnumerable<T> Iterate()
        {
            var current = _first;
            var i = 0;
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