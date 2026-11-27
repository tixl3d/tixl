using System;

namespace Lib.Utils.StraightSkeleton.Path
{
    internal class FaceQueueUtil
    {
        /// <summary>
        ///     Connect two nodes queue. Id both nodes comes from the same queue, queue
        ///     is closed. If nodes are from different queues nodes are moved to one of
        ///     them.
        /// </summary>
        /// <param name="firstFace">First face queue.</param>
        /// <param name="secondFace">Second face queue.</param>
        public static void ConnectQueues(FaceNode firstFace, FaceNode secondFace)
        {
            if (firstFace.List == null)
                throw new ArgumentException("firstFace.list cannot be null.");
            if (secondFace.List == null)
                throw new ArgumentException("secondFace.list cannot be null.");

            if (firstFace.List == secondFace.List)
            {
                if (!firstFace.IsEnd || !secondFace.IsEnd)
                    throw new InvalidOperationException("try to connect the same list not on end nodes");

                if (firstFace.IsQueueUnconnected || secondFace.IsQueueUnconnected)
                    throw new InvalidOperationException("can't close node queue not conected with edges");

                firstFace.QueueClose();
                return;
            }

            if (!firstFace.IsQueueUnconnected && !secondFace.IsQueueUnconnected)
                throw new InvalidOperationException(
                    "can't connect two diffrent queues if each of them is connected to edge");

            if (!firstFace.IsQueueUnconnected)
            {
                var qLeft = secondFace.FaceQueue;
                MoveNodes(firstFace, secondFace);
                qLeft.Close();
            }
            else
            {
                var qRight = firstFace.FaceQueue;
                MoveNodes(secondFace, firstFace);
                qRight.Close();
            }
        }

        private static void MoveNodes(FaceNode firstFace, FaceNode secondFace)
        {
            firstFace.AddQueue(secondFace);
        }
    }
}