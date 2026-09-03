#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using T3.Core.Utils;

namespace Lib.geometry;

/// <summary>
/// Loads a Wavefront OBJ file as MeshGeometry, preserving N-gon faces (quads stay
/// quads - important for beveling). OBJ objects/groups become parts with their
/// centroid as pivot; normals and texture coordinates become corner attributes.
/// </summary>
[Guid("d25b8f41-6c93-4e07-ba58-1f9e2a7c4d60")]
internal sealed class LoadObjGeometry : Instance<LoadObjGeometry>, IDescriptiveFilename, IStatusProvider
{
    [Output(Guid = "8e1a4c76-05d9-4f32-b6e8-72c9d5a0f143")]
    public readonly Slot<MeshGeometry?> Result = new();

    public LoadObjGeometry()
    {
        _resource = new Resource<MeshGeometry>(Path, TryCreateResource, allowDisposal: false);
        _resource.AddDependentSlots(Result);
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var scale = Scale.GetValue(context);
        if (Math.Abs(scale - _scale) > 0.0001f)
        {
            _scale = scale;
            _resource.MarkFileAsChanged();
        }

        if (_resource.TryGetValue(context, out var geometry))
        {
            Result.Value = geometry;
            _warningMessage = string.Empty;
        }
        else
        {
            Result.Value = null;
            _warningMessage = $"Failed loading {Path.Value}";
        }
    }

    private bool TryCreateResource(FileResource file, MeshGeometry? currentValue,
                                   [NotNullWhen(true)] out MeshGeometry? newValue,
                                   [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            newValue = ParseObj(file.AbsolutePath, _scale);
            if (newValue.FaceCount == 0)
            {
                failureReason = $"No faces in {file.AbsolutePath}";
                newValue = null;
                return false;
            }

            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            failureReason = $"Can't read {file.AbsolutePath}: {e.Message}";
            Log.Warning(failureReason, this);
            newValue = null;
            return false;
        }
    }

    private static MeshGeometry ParseObj(string path, float scale)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var texCoords = new List<Vector2>();

        var faceOffsets = new List<int> { 0 };
        var cornerPoints = new List<int>();
        var cornerNormals = new List<Vector3>();
        var cornerTexCoords = new List<Vector2>();
        var hasNormals = false;
        var hasTexCoords = false;

        var partFaceStarts = new List<int>();
        var partNames = new List<string>();

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var entries = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (entries[0])
            {
                case "v":
                    positions.Add(new Vector3(ParseFloat(entries[1]),
                                              ParseFloat(entries[2]),
                                              ParseFloat(entries[3])) * scale);
                    break;

                case "vn":
                    normals.Add(new Vector3(ParseFloat(entries[1]),
                                            ParseFloat(entries[2]),
                                            ParseFloat(entries[3])));
                    break;

                case "vt":
                    // Raw UVs, matching the convention of [LoadObj] / ObjMesh
                    texCoords.Add(new Vector2(ParseFloat(entries[1]),
                                              entries.Length > 2 ? ParseFloat(entries[2]) : 0f));
                    break;

                case "o":
                case "g":
                    // Only start a new part if the previous marker produced faces
                    var currentFaceCount = faceOffsets.Count - 1;
                    if (partFaceStarts.Count == 0 || partFaceStarts[^1] != currentFaceCount)
                    {
                        partFaceStarts.Add(currentFaceCount);
                        partNames.Add(entries.Length > 1 ? entries[1] : string.Empty);
                    }

                    break;

                case "f":
                    for (var i = 1; i < entries.Length; i++)
                    {
                        SplitIndices(entries[i], out var vIndex, out var tIndex, out var nIndex);
                        cornerPoints.Add(ResolveIndex(vIndex, positions.Count));

                        if (nIndex != 0)
                        {
                            hasNormals = true;
                            cornerNormals.Add(normals[ResolveIndex(nIndex, normals.Count)]);
                        }
                        else
                        {
                            cornerNormals.Add(Vector3.Zero);
                        }

                        if (tIndex != 0)
                        {
                            hasTexCoords = true;
                            cornerTexCoords.Add(texCoords[ResolveIndex(tIndex, texCoords.Count)]);
                        }
                        else
                        {
                            cornerTexCoords.Add(Vector2.Zero);
                        }
                    }

                    faceOffsets.Add(cornerPoints.Count);
                    break;
            }
        }

        var geometry = new MeshGeometry
                           {
                               Positions = positions.ToArray(),
                               FaceCornerOffsets = faceOffsets.ToArray(),
                               CornerPointIndices = cornerPoints.ToArray(),
                           };

        geometry.Parts = BuildParts(geometry, partFaceStarts);

        if (hasNormals)
        {
            var attribute = geometry.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, cornerNormals.Count);
            cornerNormals.CopyTo(attribute.Values);
        }

        if (hasTexCoords)
        {
            var attribute = geometry.Attributes.GetOrCreate<Vector2>(GeometryAttributeNames.TexCoord, AttributeDomain.Corner, cornerTexCoords.Count);
            cornerTexCoords.CopyTo(attribute.Values);
        }

        geometry.InvalidateTopologyCaches();
        return geometry;
    }

    private static GeometryPart[] BuildParts(MeshGeometry geometry, List<int> partFaceStarts)
    {
        if (partFaceStarts.Count == 0)
            return [];

        var parts = new GeometryPart[partFaceStarts.Count];
        for (var partIndex = 0; partIndex < partFaceStarts.Count; partIndex++)
        {
            var faceStart = partFaceStarts[partIndex];
            var faceEnd = partIndex + 1 < partFaceStarts.Count ? partFaceStarts[partIndex + 1] : geometry.FaceCount;
            var faceCount = faceEnd - faceStart;
            if (faceCount <= 0)
                continue;

            var pivot = Vector3.Zero;
            var cornerStart = geometry.FaceCornerOffsets[faceStart];
            var cornerEnd = geometry.FaceCornerOffsets[faceEnd];
            for (var c = cornerStart; c < cornerEnd; c++)
            {
                pivot += geometry.Positions[geometry.CornerPointIndices[c]];
            }

            if (cornerEnd > cornerStart)
                pivot /= cornerEnd - cornerStart;

            parts[partIndex] = new GeometryPart(faceStart, faceCount, pivot, partIndex, partIndex);
        }

        return parts;
    }

    private static void SplitIndices(string entry, out int vIndex, out int tIndex, out int nIndex)
    {
        vIndex = 0;
        tIndex = 0;
        nIndex = 0;
        var parts = entry.Split('/');
        vIndex = int.Parse(parts[0], CultureInfo.InvariantCulture);
        if (parts.Length > 1 && parts[1].Length > 0)
            tIndex = int.Parse(parts[1], CultureInfo.InvariantCulture);
        if (parts.Length > 2 && parts[2].Length > 0)
            nIndex = int.Parse(parts[2], CultureInfo.InvariantCulture);
    }

    /// <summary>OBJ indices are 1-based; negative values count back from the current end.</summary>
    private static int ResolveIndex(int index, int count)
    {
        return index > 0 ? index - 1 : count + index;
    }

    private static float ParseFloat(string text)
    {
        return float.Parse(text, CultureInfo.InvariantCulture);
    }

    public InputSlot<string> SourcePathSlot => Path;

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_warningMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage()
    {
        return _warningMessage;
    }

    private readonly Resource<MeshGeometry> _resource;
    private string _warningMessage = string.Empty;
    private float _scale = 1;

    [Input(Guid = "4c8f2d90-a7e5-4361-9b0c-e82d6f1a5347")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "a1d75e39-08c4-4f82-b5d1-c69e0b3f8724")]
    public readonly InputSlot<float> Scale = new();
}
