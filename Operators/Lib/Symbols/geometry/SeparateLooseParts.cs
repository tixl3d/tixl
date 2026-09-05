using System;
using System.Collections.Generic;
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.geometry;

/// <summary>
/// Splits a MeshGeometry into separate parts based on connected components (loose parts),
/// and sets the pivot of each new part to its volume centroid.
/// </summary>
[Guid("EC6F1A69-9D76-4A46-A7A7-2248989EC386")]
internal sealed class SeparateLooseParts : Instance<SeparateLooseParts>
{
    [Output(Guid = "5FBCE231-EFE4-4C19-957D-72B996F133AD")]
    public readonly Slot<MeshGeometry> Result = new();

    [Output(Guid = "417E4FF5-71AA-4AB8-AA0C-A379FFC3FAC4")]
    public readonly Slot<int> PartCount = new();

    public SeparateLooseParts()
    {
        Result.UpdateAction = Update;
        PartCount.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        if (source == null || source.FaceCount == 0)
        {
            Result.Value = source;
            PartCount.Value = 0;
            return;
        }

        // 1. Build face adjacency via edge topology.
        var edgeTopo = source.Edges; // builds lazily
        var faceCount = source.FaceCount;
        var adjacency = new List<int>[faceCount];
        for (int i = 0; i < faceCount; i++) adjacency[i] = new List<int>();

        foreach (var edge in edgeTopo.Edges)
        {
            int f0 = edge.Face0;
            int f1 = edge.Face1;
            if (f0 >= 0 && f1 >= 0)
            {
                adjacency[f0].Add(f1);
                adjacency[f1].Add(f0);
            }
        }

        // 2. Flood fill to find connected components of faces.
        var visited = new bool[faceCount];
        var components = new List<List<int>>(); // each list contains original face indices
        for (int f = 0; f < faceCount; f++)
        {
            if (visited[f]) continue;
            var comp = new List<int>();
            var stack = new Stack<int>();
            stack.Push(f);
            visited[f] = true;
            while (stack.Count > 0)
            {
                int current = stack.Pop();
                comp.Add(current);
                foreach (int neighbor in adjacency[current])
                {
                    if (!visited[neighbor])
                    {
                        visited[neighbor] = true;
                        stack.Push(neighbor);
                    }
                }
            }
            components.Add(comp);
        }

        // 3. Build new mesh data (keep all points, reorder faces by component).
        var newOffsets = new List<int> { 0 };
        var newCornerIndices = new List<int>();
        var newParts = new List<GeometryPart>();

        int totalFacesAdded = 0; // running count of faces placed in newParts

        // Process components sequentially; the new face index order
        // follows the order of components and then the order of faces within each component.
        foreach (var comp in components)
        {
            // 3a. Compute volume centroid for this component.
            Vector3 pivot = ComputeVolumeCentroid(comp, source);

            // Fallback: if centroid is near zero (degenerate), use average of used points.
            if (pivot.LengthSquared() < 1e-12f)
            {
                var usedPoints = new HashSet<int>();
                foreach (int f in comp)
                {
                    int start = source.FaceCornerOffsets[f];
                    int end = source.FaceCornerOffsets[f + 1];
                    for (int c = start; c < end; c++)
                        usedPoints.Add(source.CornerPointIndices[c]);
                }
                pivot = Vector3.Zero;
                foreach (int pi in usedPoints)
                    pivot += source.Positions[pi];
                pivot /= usedPoints.Count;
            }

            // 3b. Create a part for this component.
            int faceStartIdx = totalFacesAdded;
            int faceCountComp = comp.Count;

            // 3c. Copy the corners of all faces in this component.
            foreach (int f in comp)
            {
                int start = source.FaceCornerOffsets[f];
                int end = source.FaceCornerOffsets[f + 1];
                for (int c = start; c < end; c++)
                    newCornerIndices.Add(source.CornerPointIndices[c]);
                newOffsets.Add(newCornerIndices.Count); // cumulative offset
            }

            newParts.Add(new GeometryPart(
                FaceStart: faceStartIdx,
                FaceCount: faceCountComp,
                Pivot: pivot,
                Id: newParts.Count,        // unique id per part
                SeedIndex: 0               // not used here
            ));

            totalFacesAdded += faceCountComp;
        }

        // 4. Build the output MeshGeometry.
        var output = new MeshGeometry
        {
            Positions = source.Positions,               // keep all points (they're all used)
            FaceCornerOffsets = newOffsets.ToArray(),
            CornerPointIndices = newCornerIndices.ToArray(),
            Parts = newParts.ToArray()
        };

        Result.Value = output;
        PartCount.Value = components.Count;
    }

    /// <summary>
    /// Computes the volume centroid of a set of faces (N‑gons) by fan‑triangulating each face
    /// and summing tetrahedra from the origin. Works well for closed, watertight meshes.
    /// Returns <see cref="Vector3.Zero"/> if total volume is near zero.
    /// </summary>
    private Vector3 ComputeVolumeCentroid(List<int> faceIndices, MeshGeometry mesh)
    {
        Vector3 centroidSum = Vector3.Zero;
        double totalVolume = 0.0;

        var positions = mesh.Positions;
        var offsets = mesh.FaceCornerOffsets;
        var cornerPoints = mesh.CornerPointIndices;

        foreach (int f in faceIndices)
        {
            int start = offsets[f];
            int end = offsets[f + 1];
            int cornerCount = end - start;
            if (cornerCount < 3) continue;

            // Fan triangulation: v0 = first corner, then triangles (v0, v1, v2), (v0, v2, v3), ...
            int i0 = cornerPoints[start];
            Vector3 v0 = positions[i0];
            for (int k = start + 1; k < end - 1; k++)
            {
                int i1 = cornerPoints[k];
                int i2 = cornerPoints[k + 1];
                Vector3 v1 = positions[i1];
                Vector3 v2 = positions[i2];

                // Signed volume of tetrahedron (origin, v0, v1, v2).
                double vol = Vector3.Dot(v0, Vector3.Cross(v1, v2)) / 6.0;
                if (Math.Abs(vol) < 1e-15) continue;

                Vector3 tetCentroid = (v0 + v1 + v2) / 4.0f;
                centroidSum += (float)vol * tetCentroid;
                totalVolume += vol;
            }
        }

        if (Math.Abs(totalVolume) < 1e-15)
            return Vector3.Zero;

        return centroidSum / (float)totalVolume;
    }

    [Input(Guid = "52E9E7CB-C0E3-430C-B182-A5EA2E9C0270")]
    public readonly InputSlot<MeshGeometry> Geometry = new();
}