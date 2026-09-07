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
        var edgeTopo = source.Edges;
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
        var components = new List<List<int>>();
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

        // 3. Build new topology and mapping arrays.
        var newOffsets = new List<int> { 0 };
        var newCornerIndices = new List<int>();
        var newParts = new List<GeometryPart>();

        // old → new face index mapping
        var oldToNewFace = new int[faceCount];
        // old → new corner index mapping (size = cornerCount, initialized to -1)
        int cornerCount = source.CornerCount;
        var oldToNewCorner = new int[cornerCount];
        for (int i = 0; i < cornerCount; i++) oldToNewCorner[i] = -1;

        int totalFacesAdded = 0;

        foreach (var comp in components)
        {
            // Compute volume centroid for this component.
            Vector3 pivot = ComputeVolumeCentroid(comp, source);
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

            int faceStartIdx = totalFacesAdded;
            int faceCountComp = comp.Count;

            int newFaceIdx = faceStartIdx;
            foreach (int oldFace in comp)
            {
                oldToNewFace[oldFace] = newFaceIdx++;

                int start = source.FaceCornerOffsets[oldFace];
                int end = source.FaceCornerOffsets[oldFace + 1];
                for (int oldCorner = start; oldCorner < end; oldCorner++)
                {
                    int newCorner = newCornerIndices.Count;
                    oldToNewCorner[oldCorner] = newCorner;
                    newCornerIndices.Add(source.CornerPointIndices[oldCorner]);
                }
                newOffsets.Add(newCornerIndices.Count);
            }

            newParts.Add(new GeometryPart(
                FaceStart: faceStartIdx,
                FaceCount: faceCountComp,
                Pivot: pivot,
                Id: newParts.Count,
                SeedIndex: 0));

            totalFacesAdded += faceCountComp;
        }

        // 4. Build output mesh topology.
        var output = new MeshGeometry
        {
            Positions = source.Positions,
            FaceCornerOffsets = newOffsets.ToArray(),
            CornerPointIndices = newCornerIndices.ToArray(),
            Parts = newParts.ToArray()
        };

        // 5. Reorder and preserve attributes.
        output.Attributes.Clear();
        foreach (var attr in source.Attributes)
        {
            switch (attr.Domain)
            {
                case AttributeDomain.Point:
                    // Point attributes are unchanged – share the same buffer.
                    output.Attributes.Add(attr);
                    break;

                case AttributeDomain.Corner:
                    ReorderCornerAttribute(attr, oldToNewCorner, output.Attributes);
                    break;

                case AttributeDomain.Face:
                    ReorderFaceAttribute(attr, oldToNewFace, output.Attributes);
                    break;

                // Part attributes are invalid after splitting; Edge attributes are recomputed.
                // Other domains (ControlPoint, Segment, Contour) don't apply to meshes.
                default:
                    // Silently drop – they have no meaningful mapping.
                    break;
            }
        }

        output.InvalidateTopologyCaches();
        Result.Value = output;
        PartCount.Value = components.Count;
    }

 
    // Attribute reordering helpers (support common unmanaged types)
    private static void ReorderCornerAttribute(GeometryAttribute attr, int[] oldToNewCorner, GeometryAttributes target)
    {
        int count = oldToNewCorner.Length;
        if (attr is GeometryAttribute<float> fAttr)
        {
            var newVals = new float[count];
            for (int i = 0; i < count; i++) newVals[oldToNewCorner[i]] = fAttr.Values[i];
            var newAttr = new GeometryAttribute<float>(attr.Name, AttributeDomain.Corner, count);
            Array.Copy(newVals, newAttr.Values, count);
            target.Add(newAttr);
        }
        else if (attr is GeometryAttribute<int> iAttr)
        {
            var newVals = new int[count];
            for (int i = 0; i < count; i++) newVals[oldToNewCorner[i]] = iAttr.Values[i];
            var newAttr = new GeometryAttribute<int>(attr.Name, AttributeDomain.Corner, count);
            Array.Copy(newVals, newAttr.Values, count);
            target.Add(newAttr);
        }
        else if (attr is GeometryAttribute<Vector2> v2Attr)
        {
            var newVals = new Vector2[count];
            for (int i = 0; i < count; i++) newVals[oldToNewCorner[i]] = v2Attr.Values[i];
            var newAttr = new GeometryAttribute<Vector2>(attr.Name, AttributeDomain.Corner, count);
            Array.Copy(newVals, newAttr.Values, count);
            target.Add(newAttr);
        }
        else if (attr is GeometryAttribute<Vector3> v3Attr)
        {
            var newVals = new Vector3[count];
            for (int i = 0; i < count; i++) newVals[oldToNewCorner[i]] = v3Attr.Values[i];
            var newAttr = new GeometryAttribute<Vector3>(attr.Name, AttributeDomain.Corner, count);
            Array.Copy(newVals, newAttr.Values, count);
            target.Add(newAttr);
        }
        else if (attr is GeometryAttribute<Vector4> v4Attr)
        {
            var newVals = new Vector4[count];
            for (int i = 0; i < count; i++) newVals[oldToNewCorner[i]] = v4Attr.Values[i];
            var newAttr = new GeometryAttribute<Vector4>(attr.Name, AttributeDomain.Corner, count);
            Array.Copy(newVals, newAttr.Values, count);
            target.Add(newAttr);
        }
        // Add other types as needed – fallback: copy as‑is (unsafe, but better than dropping)
        else
        {
            // If we cannot reorder, we keep the original (but this will be misaligned).
            // Usually this never happens because we cover all common types.
            target.Add(attr);
        }
    }

    private static void ReorderFaceAttribute(GeometryAttribute attr, int[] oldToNewFace, GeometryAttributes target)
    {
        int count = oldToNewFace.Length;
        if (attr is GeometryAttribute<float> fAttr)
        {
            var newVals = new float[count];
            for (int i = 0; i < count; i++) newVals[oldToNewFace[i]] = fAttr.Values[i];
            var newAttr = new GeometryAttribute<float>(attr.Name, AttributeDomain.Face, count);
            Array.Copy(newVals, newAttr.Values, count);
            target.Add(newAttr);
        }
        else if (attr is GeometryAttribute<int> iAttr)
        {
            var newVals = new int[count];
            for (int i = 0; i < count; i++) newVals[oldToNewFace[i]] = iAttr.Values[i];
            var newAttr = new GeometryAttribute<int>(attr.Name, AttributeDomain.Face, count);
            Array.Copy(newVals, newAttr.Values, count);
            target.Add(newAttr);
        }
        else if (attr is GeometryAttribute<Vector2> v2Attr)
        {
            var newVals = new Vector2[count];
            for (int i = 0; i < count; i++) newVals[oldToNewFace[i]] = v2Attr.Values[i];
            var newAttr = new GeometryAttribute<Vector2>(attr.Name, AttributeDomain.Face, count);
            Array.Copy(newVals, newAttr.Values, count);
            target.Add(newAttr);
        }
        else if (attr is GeometryAttribute<Vector3> v3Attr)
        {
            var newVals = new Vector3[count];
            for (int i = 0; i < count; i++) newVals[oldToNewFace[i]] = v3Attr.Values[i];
            var newAttr = new GeometryAttribute<Vector3>(attr.Name, AttributeDomain.Face, count);
            Array.Copy(newVals, newAttr.Values, count);
            target.Add(newAttr);
        }
        else if (attr is GeometryAttribute<Vector4> v4Attr)
        {
            var newVals = new Vector4[count];
            for (int i = 0; i < count; i++) newVals[oldToNewFace[i]] = v4Attr.Values[i];
            var newAttr = new GeometryAttribute<Vector4>(attr.Name, AttributeDomain.Face, count);
            Array.Copy(newVals, newAttr.Values, count);
            target.Add(newAttr);
        }
        else
        {
            target.Add(attr);
        }
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

            int i0 = cornerPoints[start];
            Vector3 v0 = positions[i0];
            for (int k = start + 1; k < end - 1; k++)
            {
                int i1 = cornerPoints[k];
                int i2 = cornerPoints[k + 1];
                Vector3 v1 = positions[i1];
                Vector3 v2 = positions[i2];

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