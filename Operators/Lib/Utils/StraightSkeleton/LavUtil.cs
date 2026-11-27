using System;
using System.Collections.Generic;
using Lib.Utils.StraightSkeleton.Circular;

namespace Lib.Utils.StraightSkeleton
{
    internal class LavUtil
    {
        /// <summary> Check if two vertex are in the same lav. </summary>
        public static bool IsSameLav(Vertex v1, Vertex v2)
        {
            if (v1.List == null || v2.List == null)
                return false;
            return v1.List == v2.List;
        }

        public static void RemoveFromLav(Vertex vertex)
        {
            // if removed or not in list, skip
            if (vertex == null || vertex.List == null)
                return;
            vertex.Remove();
        }

        /// <summary>
        ///     Cuts all vertex after given startVertex and before endVertex. start and
        ///     and vertex are _included_ in cut result.
        /// </summary>
        /// <param name="startVertex">Start vertex.</param>
        /// <param name="endVertex">End vertex.</param>
        /// <returns> List of vertex in the middle between start and end vertex. </returns>
        public static List<Vertex> CutLavPart(Vertex startVertex, Vertex endVertex)
        {
            var ret = new List<Vertex>();
            var size = startVertex.List.Size;
            var next = startVertex;

            for (var i = 0; i < size; i++)
            {
                var current = next;
                next = current.Next as Vertex;
                current.Remove();
                ret.Add(current);

                if (current == endVertex)
                    return ret;
            }

            throw new InvalidOperationException("End vertex can't be found in start vertex lav");
        }

        /// <summary>
        ///     Add all vertex from "merged" lav into "base" lav. Vertex are added before
        ///     base vertex. Merged vertex order is reversed.
        /// </summary>
        /// <param name="base">Vertex from lav where vertex will be added.</param>
        /// <param name="merged">Vertex from lav where vertex will be removed.</param>
        public static void MergeBeforeBaseVertex(Vertex @base, Vertex merged)
        {
            var size = merged.List.Size;
            for (var i = 0; i < size; i++)
            {
                var nextMerged = merged.Next as Vertex;
                nextMerged.Remove();

                @base.AddPrevious(nextMerged);
            }
        }

        /// <summary>
        ///     Moves all nodes from given vertex lav, to new lav. All moved nodes are
        ///     added at the end of lav. The lav end is determined by first added vertex
        ///     to lav.
        /// </summary>
        public static void MoveAllVertexToLavEnd(Vertex vertex, CircularList<Vertex> newLaw)
        {
            var size = vertex.List.Size;
            for (var i = 0; i < size; i++)
            {
                var ver = vertex;
                vertex = vertex.Next as Vertex;
                ver.Remove();
                newLaw.AddLast(ver);
            }
        }
    }
}