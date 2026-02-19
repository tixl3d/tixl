#nullable enable
using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;
// ReSharper disable InconsistentNaming

namespace Lib.point.helper;

[Guid("ad651447-75e7-4491-a56a-f737d70c0522")]
internal sealed class LoadObjAsPoints : Instance<LoadObjAsPoints>
{
    // [Output(Guid = "02c14b5e-e187-4897-8163-85d2f6383c1c")]
    // public readonly Slot<BufferWithViews> VertexBuffer = new Slot<BufferWithViews>();
    //
    // [Output(Guid = "76ad9595-92ea-4fb1-a434-edb3e8834f7f")]
    // public readonly Slot<BufferWithViews> IndexBuffer = new Slot<BufferWithViews>();

    [Output(Guid = "2CAEEB72-F67D-4101-9A85-24AB8DEEB1C7")]
    public readonly Slot<StructuredList> Points = new();

    public LoadObjAsPoints()
    {
        Points.UpdateAction += Update;
        _meshResource = new Resource<ObjMesh>(Path, TryLoadMesh, allowDisposal: false);
        _meshResource.AddDependentSlots(Points);
    }

    private bool TryLoadMesh(FileResource file,  ObjMesh? currentValue, [NotNullWhen(true)] out ObjMesh? newValue,[NotNullWhen(false)] out string? failureReason)
    {
        var absolutePath = file.AbsolutePath;
        
        if (!ObjMesh.TryLoadFromFile(absolutePath, out var mesh))
        {
            failureReason = $"Can't read file {absolutePath}";
            Log.Warning(failureReason, this);
            newValue = null;
            return false;
        }
            
        newValue = mesh;
        failureReason = null;
        return true;
    }

    private static readonly int[][] _sortAxisAndDirections =
        {
                [0, 1],
                [0, -1],
                [1, 1],
                [1, -1],
                [2, 1],
                [2, -1],
        };

    private void Update(EvaluationContext context)
    {
        if (!_meshResource.TryGetValue(context, out var mesh))
        {
            Log.Debug("No mesh found", this);
            return;
        }

        // Prepare sorting
        var sortedVertexIndices = Range(0, mesh.Positions.Count).ToList();
        var sorting = Sorting.GetValue(context);

        if (sorting != (int)ObjMesh.SortDirections.Ignore)
        {
            var sortAxisIndex = _sortAxisAndDirections[sorting][0];
            var sortDirection = _sortAxisAndDirections[sorting][1];
            sortedVertexIndices.Sort((v1, v2) =>
                                     {
                                         var axisValue1 = mesh.Positions[v1].GetValueUnsafe(sortAxisIndex);
                                         var axisValue2 = mesh.Positions[v2].GetValueUnsafe(sortAxisIndex);
                                         return axisValue1.CompareTo(axisValue2) * sortDirection;
                                     });
        }

        // Export
        var exportMode = (Modes)Mode.GetValue(context);
        switch (exportMode)
        {
            case Modes.AllVertices:
            {
                //var list = new StructuredList<Point>(pointCount);
                Log.Warning("Object mode not implemented", this);
                break;
            }
            case Modes.Vertices_WithColor:
            case Modes.Vertices_ColorAsGrayScale:
            case Modes.Vertices_GrayscaleAsW:
            {
                if (mesh.Colors.Count == 0)
                {
                    Log.Warning($"Mesh doesn't contain colors definitions. You can use MeshLab to export such files.", this);
                }

                if (mesh.Positions.Count == 0)
                {
                    Log.Warning($"Mesh doesn't contain vertex definitions.", this);
                }

                try
                {
                    _points = new StructuredList<Point>(mesh.Positions.Count);

                    for (var vertexIndex = 0; vertexIndex < mesh.Positions.Count; vertexIndex++)
                    {
                        var sortedVertexIndex = sortedVertexIndices[vertexIndex];
                        var c = (sortedVertexIndex >= mesh.Colors.Count)
                                    ? Vector4.One
                                    : mesh.Colors[sortedVertexIndex];

                        if (exportMode == Modes.Vertices_GrayscaleAsW)
                        {
                            _points.TypedElements[vertexIndex] = new Point()
                                                                     {
                                                                         Position = new Vector3(
                                                                                                mesh.Positions[sortedVertexIndex].X,
                                                                                                mesh.Positions[sortedVertexIndex].Y,
                                                                                                mesh.Positions[sortedVertexIndex].Z),
                                                                         F1 = (c.X + c.Y + c.Z) / 3,
                                                                         Color = c,
                                                                     };
                        }
                        else if (exportMode == Modes.Vertices_ColorAsGrayScale)
                        {
                            var gray = (c.X + c.Y + c.Z) / 3;
                            _points.TypedElements[vertexIndex] = new Point()
                                                                     {
                                                                         Position = new Vector3(
                                                                                                mesh.Positions[sortedVertexIndex].X,
                                                                                                mesh.Positions[sortedVertexIndex].Y,
                                                                                                mesh.Positions[sortedVertexIndex].Z),
                                                                         F1 = 1,
                                                                         Color = new Vector4(gray),
                                                                     };
                        }
                        else
                        {
                            _points.TypedElements[vertexIndex] = new Point()
                                                                     {
                                                                         Position = new Vector3(
                                                                                                mesh.Positions[sortedVertexIndex].X,
                                                                                                mesh.Positions[sortedVertexIndex].Y,
                                                                                                mesh.Positions[sortedVertexIndex].Z),
                                                                         Orientation = new Quaternion(c.X, c.Y, c.Z, c.W),
                                                                         F1 = 1,
                                                                         Color = c,
                                                                     };
                        }
                    }

                    Log.Debug($"loaded mesh with {mesh.Colors.Count} colored points", this);
                }
                catch (Exception e)
                {
                    Log.Error("Reading vertices failed " + e);
                }

                break;
            }
            case Modes.LinesVertices:
            {
                try
                {
                    if (mesh.Lines.Count == 0)
                    {
                        Log.Warning("This mode requires the obj file to have line objects (I.e. with two points per face)", this);
                        break;
                    }
                    int segmentCount = 0;
                    int vertexCount = 0;

                    int lastVertexIndex = -1;
                    foreach (var line in mesh.Lines)
                    {
                        vertexCount++;
                        if (line.V0 != lastVertexIndex)
                        {
                            segmentCount++;
                        }

                        lastVertexIndex = line.V2;
                    }

                    int countIncludingSeparators = vertexCount + segmentCount * 2;
                    _points = new StructuredList<Point>(countIncludingSeparators);

                    var pointIndex = 0;
                    lastVertexIndex = -1;
                    foreach (var line in mesh.Lines)
                    {
                        if (pointIndex > 0 && line.V0 != lastVertexIndex)
                        {
                            _points.TypedElements[pointIndex++] = new Point()
                                                                      {
                                                                          Position = new Vector3(
                                                                                                 mesh.Positions[sortedVertexIndices[lastVertexIndex]].X,
                                                                                                 mesh.Positions[sortedVertexIndices[lastVertexIndex]].Y,
                                                                                                 mesh.Positions[sortedVertexIndices[lastVertexIndex]].Z),
                                                                          F1 = 1
                                                                      };
                            _points.TypedElements[pointIndex++] = Point.Separator();
                        }

                        _points.TypedElements[pointIndex++] = new Point()
                                                                  {
                                                                      Position = new Vector3(
                                                                                             mesh.Positions[sortedVertexIndices[line.V0]].X,
                                                                                             mesh.Positions[sortedVertexIndices[line.V0]].Y,
                                                                                             mesh.Positions[sortedVertexIndices[line.V0]].Z),
                                                                      F1 = 1
                                                                  };

                        lastVertexIndex = line.V2;
                    }

                    _points.TypedElements[pointIndex++] = new Point()
                                                              {
                                                                  Position = new Vector3(
                                                                                         mesh.Positions[sortedVertexIndices[lastVertexIndex]].X,
                                                                                         mesh.Positions[sortedVertexIndices[lastVertexIndex]].Y,
                                                                                         mesh.Positions[sortedVertexIndices[lastVertexIndex]].Z),
                                                                  F1 = 1
                                                              };

                    _points.TypedElements[pointIndex] = Point.Separator();
                    Log.Debug($"loaded mesh with {segmentCount} segments and {vertexCount} points", this);
                }
                catch (Exception e)
                {
                    Log.Error("Reading vertices failed " + e);
                }

                break;
            }

            case Modes.WireframeLines:
            {
                try
                {
                    var points = new List<Point>();
                    var usedEdges = new HashSet<int>();
                    foreach (var f in mesh.Faces)
                    {
                        AppendLineOnce(mesh, f.V0, f.V1, f.V2, points, usedEdges);
                        AppendLineOnce(mesh, f.V1, f.V2, f.V0, points, usedEdges);
                        AppendLineOnce(mesh, f.V2, f.V0, f.V1, points, usedEdges);
                    }

                    if (points.Count == 0)
                    {
                        Log.Warning("No points found", this);
                        break;
                    }

                    _points = new StructuredList<Point>(points.Count);

                    for (var index = 0; index < points.Count; index++)
                    {
                        _points.TypedElements[index] = points[index];
                    }

                    Log.Debug($"loaded mesh with {_points.Elements} points found", this);
                }
                catch (Exception e)
                {
                    Log.Error("Reading vertices failed " + e);
                }

                break;
            }
        }

        Points.Value = _points;
    }

    private void AppendLineOnce(ObjMesh mesh, int vertexIndexA, int vertexIndexB, int oppositeVertexIndex,  ICollection<Point> points, ISet<int> collectedPool)
    {
        if (vertexIndexA > vertexIndexB)
        {
            (vertexIndexB, vertexIndexA) = (vertexIndexA, vertexIndexB);
        }

            
        var hashForward = Utilities.Hash(vertexIndexA, vertexIndexB);
        if (!collectedPool.Add(hashForward))
        {
            //Log.Debug($"Skipping hash {hashForward}", this);
            return;
        }

        // Skip if opposite right angle

        var eA = Vector3.Normalize(mesh.Positions[vertexIndexA] - mesh.Positions[oppositeVertexIndex]);
        var eB = Vector3.Normalize(mesh.Positions[vertexIndexB] - mesh.Positions[oppositeVertexIndex]);

        var dot = Vector3.Dot(eA, eB);
        if (MathF.Abs(dot) < 0.05)
        {
            //Log.Debug($"Skipping triangulation line {hashForward}", this);
            return;
        }

        points.Add(new Point()
                       {
                           Position = new Vector3(
                                                  mesh.Positions[vertexIndexA].X,
                                                  mesh.Positions[vertexIndexA].Y,
                                                  mesh.Positions[vertexIndexA].Z),
                           F1 = 1
                       });

        points.Add(new Point()
                       {
                           Position = new Vector3(
                                                  mesh.Positions[vertexIndexB].X,
                                                  mesh.Positions[vertexIndexB].Y,
                                                  mesh.Positions[vertexIndexB].Z),
                           F1 = 1
                       });

        points.Add(new Point()
                       {
                           F1 = float.NaN
                       });

            
    }


    private StructuredList<Point> _points = new(0);
    private readonly Resource<ObjMesh> _meshResource;

    enum Modes
    {
        AllVertices,
        LinesVertices,
        Vertices_WithColor,
        Vertices_ColorAsGrayScale,
        Vertices_GrayscaleAsW,
        WireframeLines,
    }

    [Input(Guid = "895dab2c-e3be-4e73-9c96-0f6101cea113")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "DCACD412-1885-4A10-B073-54192F074AE8", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "0AE6B6C5-80FA-4229-B06B-D9C2AC8C2A3F", MappedType = typeof(ObjMesh.SortDirections))]
    public readonly InputSlot<int> Sorting = new();
}