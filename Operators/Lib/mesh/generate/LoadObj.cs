#nullable enable
using T3.Core.Rendering;
using T3.Core.Utils;

// ReSharper disable RedundantNameQualifier

namespace Lib.mesh.generate;

[Guid("be52b670-9749-4c0d-89f0-d8b101395227")]
internal sealed class LoadObj : Instance<LoadObj>, IDescriptiveFilename, IStatusProvider
{
    [Output(Guid = "1F4E7CAC-1F62-4633-B0F3-A3017A026753")]
    public readonly Slot<MeshBuffers?> Data = new();

    public LoadObj()
    {
        _resource = new Resource<MeshDataSet>(Path, TryCreateResource, allowDisposal: false);
        _resource.AddDependentSlots(Data);
        Data.UpdateAction += Update;
    }

    private bool TryCreateResource(FileResource file,  MeshDataSet? currentValue, [NotNullWhen(true)] out MeshDataSet? newValue, [NotNullWhen(false)]out string? failureReason)
    {
        var absolutePath = file.AbsolutePath;
        if (_useGpuCaching)
        {
            if (_meshBufferCache.TryGetValue(absolutePath, out var cachedBuffer))
            {
                newValue = cachedBuffer;
                failureReason = null;
                return true;
            }
        }

        var mesh = ObjMesh.LoadFromFile(absolutePath);
        if (mesh == null || mesh.DistinctDistinctVertices.Count == 0)
        {
            failureReason = $"Can't read file {absolutePath}";
            Log.Warning(failureReason, this);
            _warningMessage = failureReason;
            newValue = null;
            return false;
        }

        mesh.UpdateVertexSorting(_vertexSorting);

        newValue = new MeshDataSet(mesh, _scaleFactor);

        if (_useGpuCaching)
        {
            _meshBufferCache[absolutePath] = newValue;
        }
        else
        {
            currentValue?.Dispose();
        }
            
        failureReason = null;
        return true;
    }

    private float _scaleFactor;
        
    private void Update(EvaluationContext context)
    {
        if (ClearGPUCache.GetValue(context))
        {
            _meshBufferCache.Clear();
        }

        var shouldInvoke = SortVertices.DirtyFlag.IsDirty || UseGPUCaching.DirtyFlag.IsDirty;
        var scaleFactor = ScaleFactor.GetValue(context);
            
        var scaleHasChangedSignificantly = Math.Abs(scaleFactor - _scaleFactor) > 0.001f;
        shouldInvoke |= scaleHasChangedSignificantly;
            
        _scaleFactor = scaleFactor;
        _vertexSorting = SortVertices.GetEnumValue<ObjMesh.SortDirections>(context);
        _useGpuCaching = UseGPUCaching.GetValue(context);
            
        if(shouldInvoke)
        {
            _resource.MarkFileAsChanged();
        }


        if (_resource.TryGetValue(context, out var meshDataSet))
        {
            Data.Value = meshDataSet.DataBuffers;
            _warningMessage = string.Empty;
        }
        else
        {  
            _warningMessage = $"Failed loading {SourcePathSlot.Value}";
        }
    }

    public InputSlot<string> SourcePathSlot => Path;
    private ObjMesh.SortDirections _vertexSorting;
    private bool _useGpuCaching;
    

    private sealed class MeshDataSet : IDisposable
    {
        public readonly MeshBuffers DataBuffers;

        public MeshDataSet(ObjMesh mesh, float scaleFactor)
        {
            var vertexBufferWithViews = new BufferWithViews();
            var indexBufferWithViews = new BufferWithViews();
            var distinctVertices = mesh.DistinctDistinctVertices;
            var verticesCount = distinctVertices.Count;
            var faces = mesh.Faces;
            var faceCount = faces.Count;
            var sortedVertexIndices = mesh.SortedVertexIndices;
            var vertexBufferData = new PbrVertex[verticesCount];
            var indexBufferData = new Int3[faceCount];
            var reversedLookup = new int[verticesCount];

            // Create Vertex buffer
            {
                for (var vertexIndex = 0; vertexIndex < verticesCount; vertexIndex++)
                {
                    var sortedVertexIndex = sortedVertexIndices[vertexIndex];
                    var sortedVertex = distinctVertices[sortedVertexIndex];
                    reversedLookup[sortedVertexIndex] = vertexIndex;
                    vertexBufferData[vertexIndex] = new PbrVertex
                                                        {
                                                            Position = mesh.Positions[sortedVertex.PositionIndex] * scaleFactor,
                                                            Normal = mesh.Normals[sortedVertex.NormalIndex],
                                                            Tangent = mesh.VertexTangents[sortedVertexIndex],
                                                            Bitangent = mesh.VertexBinormals[sortedVertexIndex],
                                                            Texcoord = mesh.TexCoords[sortedVertex.TextureCoordsIndex],
                                                            Selection = 1,
                                                        };
                }

                Buffer? vertexBuffer = null;

                ResourceManager.SetupStructuredBuffer(vertexBufferData, PbrVertex.Stride * verticesCount, PbrVertex.Stride,
                                                      ref vertexBuffer);
                ResourceManager.CreateStructuredBufferSrv(vertexBuffer, ref vertexBufferWithViews.Srv);
                ResourceManager.CreateStructuredBufferUav(vertexBuffer, UnorderedAccessViewBufferFlags.None, ref vertexBufferWithViews.Uav);
                vertexBufferWithViews.Buffer = vertexBuffer;
            }

            // Create Index buffer
            {
                for (var faceIndex = 0; faceIndex < faceCount; faceIndex++)
                {
                    var face = faces[faceIndex];
                    var v1Index = mesh.GetVertexIndex(face.V0, face.V0n, face.V0t);
                    var v2Index = mesh.GetVertexIndex(face.V1, face.V1n, face.V1t);
                    var v3Index = mesh.GetVertexIndex(face.V2, face.V2n, face.V2t);

                    indexBufferData[faceIndex]
                        = new Int3(reversedLookup[v1Index],
                                   reversedLookup[v2Index],
                                   reversedLookup[v3Index]);
                }

                Buffer? indexBuffer = null;
                const int stride = 3 * 4;
                ResourceManager.SetupStructuredBuffer(indexBufferData, stride * faceCount, stride, ref indexBuffer);
                ResourceManager.CreateStructuredBufferSrv(indexBuffer, ref indexBufferWithViews.Srv);
                ResourceManager.CreateStructuredBufferUav(indexBuffer, UnorderedAccessViewBufferFlags.None, ref indexBufferWithViews.Uav);
                indexBufferWithViews.Buffer = indexBuffer;
            }

            DataBuffers = new MeshBuffers
                              {
                                  VertexBuffer = vertexBufferWithViews,
                                  IndicesBuffer = indexBufferWithViews
                              };
        }

        public void Dispose()
        {
            DataBuffers.Dispose();
            GC.SuppressFinalize(this);
        }
        ~MeshDataSet()
        {
            Dispose();
        }
    }

        
    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_warningMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage()
    {
        return _warningMessage;
    }

    private string _warningMessage = string.Empty;
        
        
    private static readonly Dictionary<string, MeshDataSet> _meshBufferCache = new();

    [Input(Guid = "7d576017-89bd-4813-bc9b-70214efe6a27")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "FFD22736-A600-4C97-A4A4-AD3526B8B35C")]
    public readonly InputSlot<bool> UseGPUCaching = new();

    [Input(Guid = "DDD22736-A600-4C97-A4A4-AD3526B8B35C")]
    public readonly InputSlot<bool> ClearGPUCache = new();
        
    [Input(Guid = "AA19E71D-329C-448B-901C-565BF8C0DA4F", MappedType = typeof(ObjMesh.SortDirections))]
    public readonly InputSlot<int> SortVertices = new();
        
    [Input(Guid = "C39A61B3-FB6B-4611-8F13-273F13C9C491")]
    public readonly InputSlot<float> ScaleFactor = new();

    private readonly Resource<MeshDataSet> _resource;
}