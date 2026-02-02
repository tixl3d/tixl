#nullable enable
using SharpDX.Direct3D11;
using SharpDX.WIC;
using SharpGLTF.Schema2;
using T3.Core.Rendering;
using T3.Core.Rendering.Material;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;
using Scene = SharpGLTF.Schema2.Scene;

namespace Lib.render.scene;

/// <summary>
/// Uses the GltfSharp library to load a gltf file and convert it into a <see cref="SceneSetup"/>.
/// </summary>
[Guid("00618c91-f39a-44ea-b9d8-175c996460dc")]
public class LoadGltfScene : Instance<LoadGltfScene>
                           , IDescriptiveFilename, IStatusProvider
{
    [Output(Guid = "C2D28EBE-E235-4234-8234-EB56E87ED9AF")]
    public readonly Slot<SceneSetup> ResultSetup = new();

    [Output(Guid = "9891EA8D-7DE7-4DD1-A61E-337ADC569EF3")]
    public readonly Slot<MeshBuffers> Mesh = new();

    [Output(Guid = "F33EC4C1-F07B-46B3-BEC7-34E91B8312EF")]
    public readonly Slot<PbrMaterial> Material = new();

    public LoadGltfScene()
    {
        _resource = new Resource<SceneSetup>(Path, OnFileChanged);
        _resource.AddDependentSlots(ResultSetup, Mesh, Material);        
        
        ResultSetup.UpdateAction += Update;
        Mesh.UpdateAction += Update;
    }

    private bool OnFileChanged(FileResource file, 
                               SceneSetup? currentValue, 
                               [NotNullWhen(true)]out SceneSetup? newValue, 
                               [NotNullWhen(false)]out string? failureReason)
    {
        LoadFile(file.AbsolutePath, 0, true);
        // if (BmFontDescription.TryInitializeFromFile(file.AbsolutePath, out newValue))
        // {
        failureReason = null;
        newValue = ResultSetup.Value!;
        //     return true;
        // }
        return true;
            
        // failureReason = "Failed to load font from file";
        // return false;
    }
    
    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = string.Empty;

        var materialNeedsUpdate = OffsetRoughness.DirtyFlag.IsDirty || OffsetMetallic.DirtyFlag.IsDirty;

        _offsetRoughness = OffsetRoughness.GetValue(context);
        _offsetMetallic = OffsetMetallic.GetValue(context);

        var meshChildIndex = MeshChildIndex.GetValue(context);
        
        var sceneSetup = Setup.GetValue(context);
        if (sceneSetup == null || Setup.Input.IsDefault)
        {
            sceneSetup = new SceneSetup();
            Setup.SetTypedInputValue(sceneSetup);
        }

        var filePath = Path.GetValue(context);

        _updateTriggered = TriggerUpdate.GetValue(context);
        _updateTriggered |= MathUtils.WasChanged(CombineBuffer.GetValue(context), ref _combineBuffer);
        
        TriggerUpdate.SetTypedInputValue(false);
        
        // if (filePath == null || !File.Exists(filePath))
        // {
        //     _lastErrorMessage = $"Gltf File not found: {filePath}";
        //     return;
        // }

        LoadFile(filePath, meshChildIndex, materialNeedsUpdate);
    }

    private void LoadFile(string? filePath, int meshChildIndex, bool materialNeedsUpdate)
    {
        if (filePath == null)
            return;
        
        if (LoadFileIfRequired(filePath, out var newSetup))
        {
            ResultSetup.Value?.Dispose();
            
            // TODO: Need to clarify of null reference types are allowed for slot values
            Setup.SetTypedInputValue(newSetup!); //TODO: this is weird. 
            ResultSetup.Value = newSetup!;
        }

        if (ResultSetup?.Value?.Dispatches != null && ResultSetup.Value.Dispatches.Count > 0)
        {
            var dispatchCount = ResultSetup.Value.Dispatches.Count;
            var index = meshChildIndex.Mod(dispatchCount);
            Mesh.Value = ResultSetup.Value.Dispatches[index].MeshBuffers;
            Material.Value = ResultSetup.Value.Dispatches[index].Material;
        }

        if (materialNeedsUpdate && ResultSetup?.Value?.Dispatches != null && ResultSetup.Value.Dispatches.Count > 0)
        {
            foreach (var dispatch in ResultSetup.Value.Dispatches)
            {
                if (dispatch.Material == null)
                    continue;

                dispatch.Material.Parameters.Roughness = _offsetRoughness;
                dispatch.Material.Parameters.Metal = _offsetMetallic;
                dispatch.Material.UpdateParameterBuffer();
            }
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        ResultSetup.Value?.Dispose();

        Log.Debug("Destroying LoadGltfScene");
    }

    private bool LoadFileIfRequired(string path, out SceneSetup? sceneSetup)
    {
        sceneSetup = null;

        if (!_updateTriggered && path == _lastFilePath)
            return false;

        _lastFilePath = path;

        _sceneMaterialsByName.Clear();
        _meshBuffersForPrimitives.Clear();
        sceneSetup = new SceneSetup();

        if (!TryGetFilePath(path, out var fullPath))
        {
            ShowError($"Gltf File not found: {path}");
            return true;
        }

        try
        {
            var model = ModelRoot.Load(fullPath);
            var rootNode = _combineBuffer 
                               ? ConvertToNodeStructureIntoChunks(model.DefaultScene)
                               : ConvertToNodeStructure(model.DefaultScene);

            sceneSetup.RootNodes.Clear();
            sceneSetup.RootNodes.Add(rootNode);
            sceneSetup.GenerateSceneDrawDispatches();
        }
        catch (Exception e)
        {
            ShowError($"Failed to load gltf file: {fullPath} \n{e.Message}");
            return false;
        }

        return true;
    }

    private SceneSetup.SceneNode ConvertToNodeStructure(Scene modelDefaultScene)
    {
        var rootNode = new SceneSetup.SceneNode()
                           {
                               Name = modelDefaultScene.Name
                           };
        
        ParseChildren(modelDefaultScene.VisualChildren, rootNode);
        return rootNode;
    }

    
    // Todo adjust shader implementation new stride
    [StructLayout(LayoutKind.Explicit, Size = Stride)]
    // ReSharper disable once MemberCanBePrivate.Global
    public struct MeshChunkDef
    {
        [FieldOffset(0)]
        public int StartFaceIndex;
        
        [FieldOffset(4)]
        public int FaceCount;
        
        [FieldOffset(8)]
        public int StartVertexIndex;
        
        [FieldOffset(12)]
        public int VertexCount;

        internal const int Stride = 16;
    }    
    

    /** Flatten all meshes into a single mesh buffer seperated into chunks */
    private SceneSetup.SceneNode ConvertToNodeStructureIntoChunks(Scene modelDefaultScene)
    {
        var rootNode = new SceneSetup.SceneNode()
                           {
                               Name = modelDefaultScene.Name
                           };
        
        // Count all vertices and faces
        var totalCounts = new MeshDataCounts();
        
        HashSet<MeshPrimitive> collectedMeshPrimitives = new ();
        _chunkDefIndicesForPrimitives.Clear();
        
        ComputeTotalMeshCounts(modelDefaultScene.VisualChildren, ref totalCounts, ref collectedMeshPrimitives);

        var meshDataSet = new MeshDataSet()
                              {
                                  VertexBufferData = new PbrVertex[totalCounts.VertexCount],
                                  IndexBufferData = new Int3[totalCounts.FaceCount],
                                  ChunksDefs = new List<MeshChunkDef>(),
                              };
        
        var meshBufferReference = new MeshBuffers(); // empty references that will be filled later
        
        // Fill the buffers 
        var counters = new MeshDataCounts();
        ParseChildrenIntoChunks(modelDefaultScene.VisualChildren, rootNode, ref counters, ref meshDataSet, ref meshBufferReference);

        // Create actual mesh buffers
        var faceCount = meshDataSet.IndexBufferData!.Length;
        var indexBufferData = meshDataSet.IndexBufferData;
        
        var verticesCount = meshDataSet.VertexBufferData!.Length;
        var vertexBufferData = meshDataSet.VertexBufferData;
        
        var chunkCount = meshDataSet.ChunksDefs!.Count;
        var chunkBufferData = meshDataSet.ChunksDefs.ToArray();
        
        ResourceManager.SetupStructuredBuffer(indexBufferData, 3 * 4 * faceCount, 3 * 4, ref meshBufferReference.IndicesBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(meshBufferReference.IndicesBuffer.Buffer, ref meshBufferReference.IndicesBuffer.Srv);
        ResourceManager.CreateStructuredBufferUav(meshBufferReference.IndicesBuffer.Buffer, UnorderedAccessViewBufferFlags.None,
                                                  ref meshBufferReference.IndicesBuffer.Uav);

        ResourceManager.SetupStructuredBuffer(vertexBufferData, PbrVertex.Stride * verticesCount, PbrVertex.Stride,
                                              ref meshBufferReference.VertexBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(meshBufferReference.VertexBuffer.Buffer, ref meshBufferReference.VertexBuffer.Srv);
        ResourceManager.CreateStructuredBufferUav(meshBufferReference.VertexBuffer.Buffer, UnorderedAccessViewBufferFlags.None,
                                                  ref meshBufferReference.VertexBuffer.Uav);
        
        
        ResourceManager.SetupStructuredBuffer(chunkBufferData, MeshChunkDef.Stride * chunkCount, MeshChunkDef.Stride, ref meshBufferReference.ChunkDefsBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(meshBufferReference.ChunkDefsBuffer.Buffer, ref meshBufferReference.ChunkDefsBuffer.Srv);
        ResourceManager.CreateStructuredBufferUav(meshBufferReference.ChunkDefsBuffer.Buffer, UnorderedAccessViewBufferFlags.None,
                                                  ref meshBufferReference.ChunkDefsBuffer.Uav);
        
        
        // TODO: Create instance points -> See [GetPointsFromSceneDef]
        // TODO: Separate this into a separate operator
        return rootNode;
    }
    
    
    private sealed class MeshDataSet
    {
        public PbrVertex[]? VertexBufferData;
        public Int3[]? IndexBufferData;
        public List<MeshChunkDef>? ChunksDefs;
    }

    
    private struct MeshDataCounts
    {
        public int ChunkCount;
        public int VertexCount;
        public int FaceCount;
    }
    
    private void ComputeTotalMeshCounts(IEnumerable<Node?> visualChildren, ref MeshDataCounts totalCounts, ref HashSet<MeshPrimitive> collectedMeshPrimitives)
    {
        foreach (var child in visualChildren)
        {
            if (child == null)
                continue;
            
            
            if (child.Mesh != null)
            {
                foreach (var meshPrimitive in child.Mesh.Primitives)
                {
                    if(!collectedMeshPrimitives.Add(meshPrimitive))
                        continue;
                    
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                    if(meshPrimitive == null)
                        continue;

                    if (!meshPrimitive.VertexAccessors.TryGetValue("POSITION", out var positionAccessor))
                        continue;

                    var triangleCount= meshPrimitive.GetTriangleIndices().Count();
                    var verticesCount = positionAccessor.Count;
                    
                    if(triangleCount == 0 || verticesCount == 0)
                        continue;
                    
                    totalCounts.ChunkCount++;
                    totalCounts.VertexCount += verticesCount;
                    totalCounts.FaceCount += triangleCount;
                }
            }

            ComputeTotalMeshCounts(child.VisualChildren, ref totalCounts, ref collectedMeshPrimitives);
        }
    }
    
    private static string MatrixToString(Matrix4x4 m)
    {
        return $"{m.M11:0.00}  {m.M12:0.00}  {m.M13:0.00}  {m.M13:0.00}  |  " +
               $"{m.M21:0.00}  {m.M22:0.00}  {m.M23:0.00}  {m.M23:0.00}  |  " +
               $"{m.M31:0.00}  {m.M32:0.00}  {m.M33:0.00}  {m.M33:0.00}  |  " +
               $"{m.M41:0.00}  {m.M42:0.00}  {m.M43:0.00}  {m.M43:0.00}  |  ";
    }
    
    /**
     * TODO: This is work in progress!
     * This will only add the mesh data to the meshDataSet, but not generate buffers.
     */
    private void ParseChildrenIntoChunks(IEnumerable<Node?> visualChildren, SceneSetup.SceneNode parentNode, ref MeshDataCounts counters,
                                         ref MeshDataSet meshData, ref MeshBuffers meshBufferReference)
    {
        foreach (var child in visualChildren)
        {
            if (child == null || meshData.VertexBufferData == null || meshData.IndexBufferData == null || meshData.ChunksDefs == null)
                continue;
            
            var t = child.LocalTransform.GetDecomposed();
            
            var transform = new SceneSetup.Transform
                                {
                                    Translation = t.Translation,
                                    Scale = t.Scale,
                                    Rotation = t.Rotation,
                                };
            
            var structureNode = new SceneSetup.SceneNode
                                    {
                                        Name = child.Name,
                                        MeshName = child.Mesh?.Name,
                                        // MeshBuffers = meshBuffers,
                                        Transform = transform,
                                        CombinedTransform = child.WorldMatrix,
                                    };

            parentNode.ChildNodes.Add(structureNode);

            var useStructureNodeForMesh = true;

            if (child.Mesh != null)
            {
                foreach (var meshPrimitive in child.Mesh.Primitives)
                {
                    if(meshPrimitive == null)
                        continue;
                    
                    if(!_chunkDefIndicesForPrimitives.TryGetValue(meshPrimitive, out var chunkDefIndex))
                    {
                        
                        if (!GetMeshDataFromPrimitive(meshPrimitive, out var vertexBufferData, out var indexBufferData, out var message))
                            continue;

                        Array.Copy(vertexBufferData, 0, meshData.VertexBufferData, counters.VertexCount, vertexBufferData.Length);

                        for(var faceIndex=0; faceIndex<indexBufferData.Length; faceIndex++)
                        {
                            
                            meshData.IndexBufferData[faceIndex + counters.FaceCount].X = indexBufferData[faceIndex].X + counters.VertexCount;
                            meshData.IndexBufferData[faceIndex + counters.FaceCount].Y = indexBufferData[faceIndex].Y + counters.VertexCount;
                            meshData.IndexBufferData[faceIndex + counters.FaceCount].Z = indexBufferData[faceIndex].Z + counters.VertexCount;
                        }
                        
                        var currentChunkIndex = meshData.ChunksDefs.Count;
                        meshData.ChunksDefs.Add(new MeshChunkDef()
                                                {
                                                    StartFaceIndex = counters.FaceCount,
                                                    FaceCount = indexBufferData.Length,
                                                    StartVertexIndex = counters.VertexCount,
                                                    VertexCount = vertexBufferData.Length,
                                                });
                        _chunkDefIndicesForPrimitives[meshPrimitive] = currentChunkIndex;
                        counters.VertexCount += vertexBufferData.Length;
                        counters.FaceCount += indexBufferData.Length;
                        
                        
                        chunkDefIndex = currentChunkIndex;
                    }
                    
                    var materialDef = GetOrCreateMaterialDefinition(meshPrimitive.Material);

                    if (useStructureNodeForMesh)
                    {
                        structureNode.MeshBuffers = meshBufferReference;
                        structureNode.MeshChunkIndex = chunkDefIndex;
                        structureNode.Material = materialDef;
                        useStructureNodeForMesh = false;
                        continue;
                    }
                    
                    var meshNode = new SceneSetup.SceneNode()
                                       {
                                           Name = child.Name,
                                           MeshName = child.Mesh?.Name,
                                           MeshBuffers = meshBufferReference,
                                           MeshChunkIndex = chunkDefIndex,
                                           Transform = transform,
                                           CombinedTransform = child.WorldMatrix,
                                           Material = materialDef,
                                       };
                    parentNode.ChildNodes.Add(meshNode);
                }
            }

            ParseChildrenIntoChunks(child.VisualChildren, structureNode, ref counters, ref meshData, ref meshBufferReference);
        }
    }
    
    
    private void ParseChildren(IEnumerable<Node?> visualChildren, SceneSetup.SceneNode parentNode)
    {
        foreach (var child in visualChildren)
        {
            if (child == null)
                continue;

            var t = child.LocalTransform.GetDecomposed();
            var transform = new SceneSetup.Transform
                                {
                                    Translation = t.Translation,
                                    Scale = t.Scale,
                                    Rotation = t.Rotation,
                                };

            // Pure logic node
            var structureNode = new SceneSetup.SceneNode()
                                    {
                                        Name = child.Name,
                                        MeshName = child.Mesh?.Name,
                                        // MeshBuffers = meshBuffers,
                                        Transform = transform,
                                        CombinedTransform = child.WorldMatrix,
                                    };

            parentNode.ChildNodes.Add(structureNode);

            var useStructureNodeForMesh = true;

            if (child.Mesh != null)
            {
                var meshIndex = 0;
                foreach (var meshPrimitive in child.Mesh.Primitives)
                {
                    if(meshPrimitive == null)
                        continue;

                    if (!_meshBuffersForPrimitives.TryGetValue(meshPrimitive, out var meshBuffers))
                    {
                        meshIndex++;
                        if (!TryGenerateMeshBuffersFromGltfChild(meshPrimitive, out meshBuffers, out var errorMessage))
                        {
                            ShowError(errorMessage);
                            meshBuffers = null;
                        }

                        if (meshBuffers == null)
                            continue;

                        _meshBuffersForPrimitives[meshPrimitive] = meshBuffers;
                    }
                    
                    Log.Debug($" mesh:{child.Name} {child.Mesh?.Name}  {meshIndex}");
                    
                    var materialDef = GetOrCreateMaterialDefinition(meshPrimitive.Material);

                    //Log.Debug("Material: " + materialDef);

                    if (useStructureNodeForMesh)
                    {
                        structureNode.MeshBuffers = meshBuffers;
                        structureNode.Material = materialDef;
                        useStructureNodeForMesh = false;
                        continue;
                    }

                    var meshNode = new SceneSetup.SceneNode()
                                       {
                                           Name = child.Name,
                                           MeshName = child.Mesh?.Name,
                                           MeshBuffers = meshBuffers,
                                           Transform = transform,
                                           CombinedTransform = child.WorldMatrix,
                                           Material = materialDef,
                                       };
                    parentNode.ChildNodes.Add(meshNode);
                }
            }

            ParseChildren(child.VisualChildren, structureNode);
        }
    }
    
    private readonly Resource<SceneSetup> _resource;


    #region Asset extraction
    /// <summary>
    /// Extract gltf material definitions so we can later create a PbrMaterial from this data.
    /// </summary>
    private SceneSetup.SceneMaterial? GetOrCreateMaterialDefinition(Material? gltfMaterial)
    {
        if (gltfMaterial == null)
            return null;

        var name = gltfMaterial.Name;
        if (string.IsNullOrEmpty(name))
            return null;

        if (_sceneMaterialsByName.TryGetValue(name, out var materialDef))
        {
            return materialDef;
        }

        var baseColor = new System.Numerics.Vector4(1, 1, 1, 1);
        var baseColorSrv = PbrMaterial.DefaultAlbedoColorSrv;

        var emissiveColor = Vector4.Zero;
        var emissiveSrv = PbrMaterial.DefaultEmissiveColorSrv;

        var normalSrv = PbrMaterial.DefaultNormalSrv;

        ShaderResourceView? occlusionSrv = null;
        ShaderResourceView? metallicRoughnessSrv = null;
        Texture2D? metallicRoughnessTexture = null;
        Texture2D? occlusionTexture = null;

        var roughness = 0.5f;
        var metallic = 0f;

        foreach (var channel in gltfMaterial.Channels)
        {
            Log.Debug("channel: " + channel.Key);
            switch (channel.Key)
            {
                case "BaseColor":
                {
                    baseColor = channel.Color;

                    if (TryCreateTextureFromChannel(gltfMaterial, channel, out var _, out var srv, PbrMaterial.DefaultAlbedoColorSrv))
                    {
                        baseColorSrv = srv;
                    }

                    break;
                }

                case "Emissive":
                {
                    emissiveColor = channel.Color;

                    if (TryCreateTextureFromChannel(gltfMaterial, channel, out var _, out var srv, PbrMaterial.DefaultEmissiveColorSrv))
                    {
                        emissiveSrv = srv;
                    }

                    break;
                }

                case "MetallicRoughness":
                {
                    if (TryCreateTextureFromChannel(gltfMaterial, channel, out var texture, out var srv, null))
                    {
                        metallicRoughnessTexture = texture;
                        metallicRoughnessSrv = srv;
                    }

                    foreach (var p in channel.Parameters)
                    {
                        switch (p.Name)
                        {
                            case "MetallicFactor":
                                metallic = (float)p.Value;
                                break;
                            case "RoughnessFactor":
                                roughness = (float)p.Value;
                                break;
                        }
                    }

                    break;
                }

                case "Occlusion":
                {
                    if (TryCreateTextureFromChannel(gltfMaterial, channel, out var texture, out var srv, null))
                    {
                        occlusionTexture = texture;
                        occlusionSrv = srv;
                    }

                    break;
                }

                case "Normal":
                {
                    if (TryCreateTextureFromChannel(gltfMaterial, channel, out var _, out var srv, PbrMaterial.DefaultNormalSrv))
                    {
                        normalSrv = srv;
                    }

                    break;
                }
            }
        }

        TryCreateRoughnessMetallicOcclusionTexture(
                                                   metallicRoughnessTexture, metallicRoughnessSrv,
                                                   occlusionTexture, occlusionSrv,
                                                   out var roughnessMetallicOcclusionSrv);

        metallicRoughnessTexture?.Dispose();
        metallicRoughnessSrv?.Dispose();
        occlusionTexture?.Dispose();
        occlusionSrv?.Dispose();

        var newMaterialDef = new SceneSetup.SceneMaterial
                                 {
                                     Name = name,
                                     // ColorTexture = baseColorTexture,
                                     // NormalTexture = normalTexture,
                                     PbrParameters = new PbrMaterial.PbrParameters
                                                         {
                                                             BaseColor = baseColor,
                                                             EmissiveColor = emissiveColor,
                                                             Roughness = roughness - 1 + _offsetRoughness, // glTF standard uses roughness factor?!
                                                             Specular = 1,
                                                             Metal = metallic -1 + _offsetMetallic, // glTF standard uses metallic factor?!
                                                         }
                                 };

        var newPbrMaterial = new PbrMaterial
                                 {
                                     Name = name,
                                     AlbedoMapSrv = baseColorSrv,
                                     EmissiveMapSrv = emissiveSrv,
                                     RoughnessMetallicOcclusionSrv = roughnessMetallicOcclusionSrv,
                                     NormalSrv = normalSrv,
                                     Parameters = newMaterialDef.PbrParameters,
                                 };
        newPbrMaterial.UpdateParameterBuffer();

        newMaterialDef.PbrMaterial = newPbrMaterial;

        _sceneMaterialsByName[name] = newMaterialDef;
        return newMaterialDef;
    }

    /// <summary>
    /// Setup shaders and textures required for combining roughness, metallic and occlusion textures.
    /// </summary>
    private static void PrepareCombineShaderResources(Instance instance, bool forceUpdate = false)
    {
        if (_combineChannelsComputeShaderResource == null)
        {
            const string sourcePath = "Lib:shaders/cs/CombineGltfChannels-cs.hlsl";
            const string entryPoint = "main";

            _combineChannelsComputeShaderResource = ResourceManager.CreateShaderResource<ComputeShader>(sourcePath, instance, () => entryPoint, OnShaderChanged);

            OnShaderChanged(_combineChannelsComputeShaderResource.Value);
        }
        
        if (!forceUpdate && _combineChannelsComputeShaderResource?.Value != null)
            return;
        
        OnShaderChanged(_combineChannelsComputeShaderResource!.Value);
        return;

        static void OnShaderChanged(ComputeShader? e)
        {
            if (e == null)
            {
                Log.Error($"Failed to initialize video conversion shader in {nameof(LoadGltfScene)}");
                return;
            }
            
            e.Name = "CombineGltfChannels";

            var samplerDesc = new SamplerStateDescription
                                  {
                                      Filter = Filter.MinMagMipLinear,
                                      AddressU = TextureAddressMode.Clamp,
                                      AddressV = TextureAddressMode.Clamp,
                                      AddressW = TextureAddressMode.Clamp,
                                      MipLodBias = 0,
                                      MaximumAnisotropy = 1,
                                      ComparisonFunction = Comparison.Never,
                                      MinimumLod = -999999,
                                      MaximumLod = 9999999
                                  };

            _combineChannelsSampler?.Dispose();
            _combineChannelsSampler = new SamplerState(ResourceManager.Device, samplerDesc);
        }
    }

    /// <summary>
    /// Fall back to default texture if nothing set. Otherwise use shader to create new texture.
    /// </summary>
    private void TryCreateRoughnessMetallicOcclusionTexture(Texture2D? metallicRoughnessTexture,
                                                            ShaderResourceView? metallicRoughnessSrv,
                                                            Texture2D? occlusionTexture,
                                                            ShaderResourceView? occlusionSrv,
                                                            out ShaderResourceView? resultSrv)
    {
        if (metallicRoughnessSrv == null && occlusionSrv == null)
        {
            resultSrv = PbrMaterial.DefaultRoughnessMetallicOcclusionSrv;
            return;
        }

        PrepareCombineShaderResources(this, _updateTriggered);

        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;

        // Compute max resolution
        var width = 1;
        var height = 1;

        if (metallicRoughnessTexture != null)
        {
            width = Math.Max(width, metallicRoughnessTexture.Description.Width);
            height = Math.Max(height, metallicRoughnessTexture.Description.Width);
        }

        if (occlusionTexture != null)
        {
            width = Math.Max(width, occlusionTexture.Description.Width);
            height = Math.Max(height, occlusionTexture.Description.Width);
        }

        metallicRoughnessSrv ??= PbrMaterial.BlackPixelSrv;
        occlusionSrv ??= PbrMaterial.WhitePixelSrv;

        // TODO: create and test merge compute shader

        // Keep previous setup
        var prevShader = csStage.Get();
        var prevUavs = csStage.GetUnorderedAccessViews(0, 1);
        var prevSrvs = csStage.GetShaderResources(0, 1);
        var prevSamplers = csStage.GetSamplers(0, 1);

        // Set Shader
        if (_combineChannelsComputeShaderResource != null)
        {
            var convertShader = _combineChannelsComputeShaderResource.Value;
            csStage.Set(convertShader);
        }

        var srvs = new[] { metallicRoughnessSrv, occlusionSrv };
        csStage.SetShaderResources(0, srvs);

        csStage.SetSampler(0, _combineChannelsSampler);

        // Create target texture
        var resultTextureDescription = new Texture2DDescription
                                           {
                                               BindFlags = BindFlags.UnorderedAccess | BindFlags.RenderTarget | BindFlags.ShaderResource,
                                               Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                                               Width = width,
                                               Height = height,
                                               MipLevels = 1,
                                               SampleDescription = new SampleDescription(1, 0),
                                               Usage = ResourceUsage.Default,
                                               OptionFlags = ResourceOptionFlags.None | ResourceOptionFlags.GenerateMipMaps,
                                               CpuAccessFlags = CpuAccessFlags.None,
                                               ArraySize = 1
                                           };

        var resultTexture = Texture2D.CreateTexture2D(resultTextureDescription);
        resultSrv = new ShaderResourceView(ResourceManager.Device, resultTexture);
        var resultUav = new UnorderedAccessView(ResourceManager.Device, resultTexture);
        csStage.SetUnorderedAccessView(0, resultUav, 0);

        // Dispatch
        const int threadNumX = 16, threadNumY = 16;
        var dispatchCountX = (width / threadNumX) + 1;
        var dispatchCountY = (height / threadNumY) + 1;
        deviceContext.Dispatch(dispatchCountX, dispatchCountY, 1);

        ResourceManager.Device.ImmediateContext.GenerateMips(resultSrv);

        // Restore prev setup
        csStage.SetUnorderedAccessView(0, prevUavs[0]);
        csStage.SetShaderResource(0, prevSrvs[0]);
        csStage.SetSamplers(0, prevSamplers);
        csStage.Set(prevShader);
    }

    private static Resource<T3.Core.DataTypes.ComputeShader>? _combineChannelsComputeShaderResource;

    /// <summary>
    /// Tries to create a texture from a gltf material channel.
    /// </summary>
    private static bool TryCreateTextureFromChannel(Material gltfMaterial, MaterialChannel channel,  out Texture2D? texture, out ShaderResourceView? srv,
                                                    ShaderResourceView? fallbackSrv)
    {
        texture = null;
        srv = fallbackSrv;
        if (channel.Texture == null)
        {
            return false;
        }

        var imageContent = channel.Texture.PrimaryImage.Content.Content;

        try
        {
            using var memStream = new MemoryStream(imageContent.ToArray());
            memStream.Position = 0;
            using var imagingFactory = new ImagingFactory();

            using var bitmapDecoder = new BitmapDecoder(imagingFactory, memStream, DecodeOptions.CacheOnDemand);
            using var formatConverter = new FormatConverter(imagingFactory);
            using var bitmapFrameDecode = bitmapDecoder.GetFrame(0);
            formatConverter.Initialize(bitmapFrameDecode, SharpDX.WIC.PixelFormat.Format32bppRGBA, BitmapDitherType.None, null, 0.0,
                                       BitmapPaletteType.Custom);

            texture = Texture2D.CreateFromBitmap(ResourceManager.Device, formatConverter);
            texture.Name = channel.Key;

            Log.Debug($" Created {gltfMaterial.Name}.{channel.Key} with {texture.Description.Width}×{texture.Description.Height}");
            // bitmapFrameDecode.Dispose();
            // bitmapDecoder.Dispose();
            // formatConverter.Dispose();
            // imagingFactory.Dispose();

            srv = new ShaderResourceView(ResourceManager.Device, texture);
            ResourceManager.Device.ImmediateContext.GenerateMips(srv);

            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create texture from channel {gltfMaterial.Name}.{channel.Key} : {e.Message}");
            return false;
        }
    }
    
    
    private static bool TryGenerateMeshBuffersFromGltfChild(MeshPrimitive meshPrimitive, out MeshBuffers newMesh, out string message)
    {
        // TODO: return cached mesh to reuse buffer
        newMesh = new MeshBuffers();
        message = string.Empty;
        
        if (!GetMeshDataFromPrimitive(meshPrimitive, out var vertexBufferData, out var indexBufferData, out message))
            return false;
        
        try
        {
            var faceCount = indexBufferData.Length;
            var verticesCount = vertexBufferData.Length;
            
            const int stride = 3 * 4;
            ResourceManager.SetupStructuredBuffer(indexBufferData, stride * faceCount, stride, ref newMesh.IndicesBuffer.Buffer);
            ResourceManager.CreateStructuredBufferSrv(newMesh.IndicesBuffer.Buffer, ref newMesh.IndicesBuffer.Srv);
            ResourceManager.CreateStructuredBufferUav(newMesh.IndicesBuffer.Buffer, UnorderedAccessViewBufferFlags.None,
                                                      ref newMesh.IndicesBuffer.Uav);

            ResourceManager.SetupStructuredBuffer(vertexBufferData, PbrVertex.Stride * verticesCount, PbrVertex.Stride,
                                                  ref newMesh.VertexBuffer.Buffer);
            ResourceManager.CreateStructuredBufferSrv(newMesh.VertexBuffer.Buffer, ref newMesh.VertexBuffer.Srv);
            ResourceManager.CreateStructuredBufferUav(newMesh.VertexBuffer.Buffer, UnorderedAccessViewBufferFlags.None,
                                                      ref newMesh.VertexBuffer.Uav);
        }
        catch (Exception e)
        {
            message = $"Failed loading gltf: {e.Message}";
            return false;
        }
        return true;
    }

    private static bool GetMeshDataFromPrimitive(MeshPrimitive meshPrimitive, out PbrVertex[] vertexBufferData, out Int3[] indexBufferData, out string message)
    {
        vertexBufferData = Array.Empty<PbrVertex>();
        indexBufferData = Array.Empty<Int3>();
            
        // Convert vertices
        {
            // TODO: Iterate over all primitives
            var vertexAccessors = meshPrimitive.VertexAccessors;

            // Collect positions
            if (!vertexAccessors.TryGetValue("POSITION", out var positionAccessor))
            {
                message = "Can't find POSITION attribute in gltf mesh";
                return false;
            }

            var verticesCount = positionAccessor.Count;

            if (vertexBufferData.Length != verticesCount)
                vertexBufferData = new PbrVertex[verticesCount];

            var positions = positionAccessor.AsVector3Array();

            // Collect normals
            Vector3[]? normals = null;
            if (vertexAccessors.TryGetValue("NORMAL", out var normalAccess))
            {
                normals = normalAccess.AsVector3Array().ToArray();
            }

            // Collect texture coords
            Vector2[]? texCoords = null;
            Vector2[]? texCoords2 = null;
            if (vertexAccessors.TryGetValue("TEXCOORD_0", out var texAccess))
            {
                texCoords = texAccess.AsVector2Array().ToArray();
            }
            if (vertexAccessors.TryGetValue("TEXCOORD_1", out var texAccess2))  // Check for second texture coordinate set
            {
                texCoords2 = texAccess2.AsVector2Array().ToArray();
            }
            // Write vertex buffer
            for (var vertexIndex = 0; vertexIndex < positions.Count; vertexIndex++)
            {
                var position = positions[vertexIndex];
                vertexBufferData[vertexIndex] = new PbrVertex
                                                    {
                                                        Position = new Vector3(position.X, position.Y, position.Z),
                                                        Normal = normals == null ? VectorT3.Up : normals[vertexIndex],
                                                        Tangent = VectorT3.Right,
                                                        Bitangent = VectorT3.ForwardLH,
                                                        Texcoord = texCoords == null
                                                                       ? Vector2.Zero
                                                                       : new Vector2(texCoords[vertexIndex].X,
                                                                                     1 - texCoords[vertexIndex].Y),
                                                        Texcoord2 = texCoords2 == null
                                                                       ? Vector2.Zero
                                                                       : new Vector2(texCoords2[vertexIndex].X,
                                                                                     1 - texCoords2[vertexIndex].Y),
                    Selection = 1,
                                                    };
            }
            
            if (verticesCount == 0)
            {
                message = "No vertices found";
                return false;
            }
        }

        // Convert indices
        {
            var indices = meshPrimitive.GetTriangleIndices().ToList();
            var faceCount = indices.Count;
            if (indexBufferData.Length != faceCount)
                indexBufferData = new Int3[faceCount];

            var faceIndex = 0;
            foreach (var (a, b, c) in indices)
            {
                indexBufferData[faceIndex] = new Int3(a, b, c);
                faceIndex++;

                // Calc TBN space
                var p1 = vertexBufferData[a].Position;
                var p2 = vertexBufferData[b].Position;
                var p3 = vertexBufferData[c].Position;

                // check for degenerated triangle
                if (p1 == p2 || p1 == p3 || p2 == p3) continue;

                var uv1 = vertexBufferData[a].Texcoord;
                var uv2 = vertexBufferData[b].Texcoord;
                var uv3 = vertexBufferData[c].Texcoord;

                // check for degenerated triangle
                if (uv1 == uv2 || uv1 == uv3 || uv2 == uv3) continue;

                var n1 = vertexBufferData[a].Normal;
                var n2 = vertexBufferData[b].Normal;
                var n3 = vertexBufferData[c].Normal;

                // Taken from https://github.com/vpenades/SharpGLTF/blob/master/examples/SharpGLTF.Runtime.MonoGame/NormalTangentFactories.cs
                var s = p2 - p1;
                var t = p3 - p1;

                var sUv = uv2 - uv1;
                var tUv = uv3 - uv1;
                //var tUv =  uv1 - uv3;
                //tUv.Y = 1 - tUv.Y; 

                var sx = sUv.X;
                var tx = tUv.X;
                var sy = sUv.Y;
                var ty = tUv.Y;

                var r = 1.0F / ((sx * ty) - (tx * sy));

                if (!r._IsFinite()) continue;

                var sDir = new Vector3((ty * s.X) - (sy * t.X), (ty * s.Y) - (sy * t.Y), (ty * s.Z) - (sy * t.Z)) * r;
                var tDir = new Vector3((sx * t.X) - (tx * s.X), (sx * t.Y) - (tx * s.Y), (sx * t.Z) - (tx * s.Z)) * r;

                if (!sDir._IsFinite()) continue;
                if (!tDir._IsFinite()) continue;

                // Ill-fated attempt with brute force 
                // sDir =  Vector3.Cross(n1, Vector3.UnitY);
                // tDir =  Vector3.Cross(n1, sDir);

                // Todo: Sadly this fill add significant artifacts to complex meshes

                vertexBufferData[a].Tangent = Vector3.Normalize(sDir);
                vertexBufferData[a].Bitangent = Vector3.Normalize(tDir);
            }

            if (faceCount == 0)
            {
                message = "No faces found";
                return false;
            }
        }
        
        message = string.Empty;
        return true;
    }
    #endregion

    private readonly Dictionary<string, SceneSetup.SceneMaterial> _sceneMaterialsByName = new();
    private readonly Dictionary<MeshPrimitive, MeshBuffers> _meshBuffersForPrimitives = new();
    private readonly Dictionary<MeshPrimitive, int> _chunkDefIndicesForPrimitives = new();
    
    private bool _combineBuffer;
    private float _offsetRoughness;
    private float _offsetMetallic;
    private static SamplerState? _combineChannelsSampler;
    private bool _updateTriggered;

    #region implement graph node interfaces
    InputSlot<string> IDescriptiveFilename.SourcePathSlot => Path;

    private string _lastFilePath = string.Empty;

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    string IStatusProvider.GetStatusMessage()
    {
        return _lastErrorMessage;
    }

    private void ShowError(string message)
    {
        _lastErrorMessage = message;
        Log.Warning(_lastErrorMessage, this);
    }

    private string _lastErrorMessage = string.Empty;
    #endregion


    
    
    [Input(Guid = "292e80cf-ba31-4a50-9bf4-83712430f811")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "D02F41A6-1A6B-4A6E-8D6C-A28873C79F2C")]
    public readonly InputSlot<SceneSetup> Setup = new();
    
    [Input(Guid = "57F129AE-0B2E-465D-8C0E-F6259FDD37CE")]
    public readonly InputSlot<bool> CombineBuffer = new();
    
    
    [Input(Guid = "EF7075E9-4BC2-442E-8E0C-E03667FF2E0A")]
    public readonly InputSlot<bool> TriggerUpdate = new();

    [Input(Guid = "D7AE3173-C490-47CF-8D4B-4C25102F6904")]
    public readonly InputSlot<float> OffsetRoughness = new();

    [Input(Guid = "49237499-9B1E-4371-AFAC-4E3394868370")]
    public readonly InputSlot<float> OffsetMetallic = new();

    [Input(Guid = "FB325383-754A-4702-AFF2-C19E16363460")]
    public readonly InputSlot<int> MeshChildIndex = new();
}