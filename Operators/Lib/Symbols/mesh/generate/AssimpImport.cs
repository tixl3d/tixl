#nullable enable
using System.Diagnostics;
using System.Threading;
using Lib.Utils.AssimpIo;
using T3.Graphics.Compat;
using T3.Core.Rendering;
using T3.Core.Rendering.Material;
using T3.Core.Utils;

namespace Lib.mesh.generate;

/// <summary>
/// Loads 3D model files through Assimp (FBX, glTF, OBJ, Collada, STL, PLY, 3DS, Blend and many more)
/// as GPU mesh, points, procedural geometry and skeleton with animation clips.
/// </summary>
/// <remarks>
/// Parsing and conversion run on a worker thread, so large files never stall the render loop; the
/// previous result stays visible until the new one is ready. Only the GPU upload of a finished
/// load happens in <see cref="Update"/>. Results of superseded loads are dropped.
/// </remarks>
[Guid("E0B5B99D-C44F-434D-920D-422E88C75870")]
internal sealed class AssimpImport : Instance<AssimpImport>, IDescriptiveFilename, IStatusProvider
{
    [Output(Guid = "77A21584-B35B-46B6-8056-1FEB4196C0B7")]
    public readonly Slot<MeshBuffers?> MeshData = new();

    [Output(Guid = "3F6D2A8E-91C4-4B57-A0E3-6C18D92B7F45")]
    public readonly Slot<MeshGeometry?> Geometry = new();

    [Output(Guid = "9342C958-3D1F-40B0-B5E0-696812704812")]
    public readonly Slot<BufferWithViews?> Points = new();

    [Output(Guid = "C81E5F27-4A9D-4E36-B2F0-7D53A1C69E08")]
    public readonly Slot<SceneSetup?> Setup = new();

    [Output(Guid = "B7D3E1A9-4C62-4F85-9E07-2A6C8D5F31B4")]
    public readonly Slot<PbrMaterial?> Material = new();

    [Output(Guid = "5B2E9C61-D847-4F1A-93B6-E0A4C72F158D")]
    public readonly Slot<BufferWithViews?> SkinWeights = new();

    [Output(Guid = "A94F3D17-6E28-4C85-B1D9-28F7E05B3A6C")]
    public readonly Slot<BufferWithViews?> SkeletonPoints = new();

    [Output(Guid = "A1B2C3D4-5E6F-4A7B-8C9D-0E1F2A3B4C5D")]
    public readonly Slot<string> Metadata = new();

    [Output(Guid = "B2C3D4E5-6F7A-4B8C-9D1E-1F2B3C4D5E6F")]
    public readonly Slot<int> VertexCount = new();

    [Output(Guid = "C3D4E5F6-7A8B-4C9D-1E2F-2B3C4D5E6F7A")]
    public readonly Slot<int> FaceCount = new();

    [Output(Guid = "D4E5F6A7-8B9C-4D1E-2F3B-3C4D5E6F7A8B")]
    public readonly Slot<int> MeshCount = new();

    [Output(Guid = "E5F6A7B8-9C1D-4E2F-3B4C-4D5E6F7A8B9C")]
    public readonly Slot<Vector3> BoundsMin = new();

    [Output(Guid = "F6A7B8C9-1D2E-4F3B-4C5D-5E6F7A8B9C1D")]
    public readonly Slot<Vector3> BoundsMax = new();

    [Output(Guid = "A7B8C9D1-2E3F-4B4C-5D6E-6F7A8B9C1D2E")]
    public readonly Slot<string> MeshNames = new();

    [Output(Guid = "1D7C4E92-B35A-4F08-8E61-C29A07D4B3F5")]
    public readonly Slot<int> BoneCount = new();

    [Output(Guid = "6E83A5C0-2F19-4D7B-A4E2-93B1F60C8D27")]
    public readonly Slot<int> AnimationCount = new();

    [Output(Guid = "D2F07B39-8C64-4A15-9E3B-51A8E6C2D04F")]
    public readonly Slot<string> AnimationNames = new();

    public AssimpImport()
    {
        _fileResource = new Resource<FileVersion>(FilePath, TryCreateFileVersion, allowDisposal: false);

        _allOutputs =
            [
                MeshData, Geometry, Points, Setup, Material, SkinWeights, SkeletonPoints, Metadata, VertexCount, FaceCount, MeshCount,
                BoundsMin, BoundsMax, MeshNames, BoneCount, AnimationCount, AnimationNames,
            ];

        _fileResource.AddDependentSlots(_allOutputs);

        MeshData.UpdateAction += Update;
        Geometry.UpdateAction += Update;
        Points.UpdateAction += Update;
        Setup.UpdateAction += Update;
        Material.UpdateAction += Update;
        SkinWeights.UpdateAction += Update;
        SkeletonPoints.UpdateAction += Update;
        Metadata.UpdateAction += Update;
        VertexCount.UpdateAction += Update;
        FaceCount.UpdateAction += Update;
        MeshCount.UpdateAction += Update;
        BoundsMin.UpdateAction += Update;
        BoundsMax.UpdateAction += Update;
        MeshNames.UpdateAction += Update;
        BoneCount.UpdateAction += Update;
        AnimationCount.UpdateAction += Update;
        AnimationNames.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            UpdateLoading(context);
        }
        catch (Exception e)
        {
            // Never let a loading problem escape into graph evaluation
            _statusLevel = IStatusProvider.StatusLevel.Error;
            _statusMessage = $"Unexpected import failure: {e.Message}";
            Log.Error(_statusMessage, this);
        }

        WriteOutputs();

        // Poll each frame only while a load is in flight
        var trigger = _pendingLoad != null ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
        foreach (var output in _allOutputs)
        {
            output.DirtyFlag.Trigger = trigger;
        }
    }

    private void UpdateLoading(EvaluationContext context)
    {
        var settings = new AssimpImportSettings(PostProcessFlags.GetEnumValue<AssimpPostProcessPresets>(context),
                                                Scale.GetValue(context),
                                                Normalize.GetValue(context),
                                                AxisConversionMode.GetEnumValue<AssimpAxisConversions>(context),
                                                FlipNormals.GetValue(context),
                                                SelectedMeshIndex.GetValue(context),
                                                ImportMaterials.GetValue(context));

        var isolateImport = IsolateImport.GetValue(context);
        var isolationChanged = isolateImport != _isIsolated;
        _isIsolated = isolateImport;

        var reloadRequested = MathUtils.WasTriggered(TriggerReload.GetValue(context), ref _reloadTriggered);
        if (reloadRequested)
        {
            TriggerReload.SetTypedInputValue(false);
            _reloadTriggered = false;
        }

        // A new FileVersion instance is created whenever the path changes or the file is modified on disk
        var fileVersion = _fileResource.GetValue(context);
        if (fileVersion == null)
        {
            CancelPendingLoad();
            _requestedFile = null;
            _isReloadRequested = false;
            ClearModel();
            var path = FilePath.Value;
            _statusLevel = string.IsNullOrWhiteSpace(path) ? IStatusProvider.StatusLevel.Notice : IStatusProvider.StatusLevel.Warning;
            _statusMessage = string.IsNullOrWhiteSpace(path) ? "No file selected" : $"File not found: {path}";
            return;
        }

        if (!ReferenceEquals(fileVersion, _requestedFile))
        {
            // A different file or a new state of it: the running load is obsolete
            CancelPendingLoad();
        }

        if (isolationChanged)
        {
            // The running load uses the old mode; switch it over now so the toggle is visible
            CancelPendingLoad();
        }

        _isReloadRequested |= reloadRequested;
        _requestedFile = fileVersion;

        TryApplyCompletedLoad();

        // At most one load per operator runs. Settings that change while it runs (e.g. an animated Scale)
        // are picked up by the next one, so intermediate results keep showing up.
        if (_pendingLoad == null && (isolationChanged || _isReloadRequested || !IsLoaded(fileVersion, settings)))
            StartLoad(fileVersion, settings, reparse: _isReloadRequested);
    }

    private bool IsLoaded(FileVersion fileVersion, in AssimpImportSettings settings)
    {
        return ReferenceEquals(fileVersion, _loadedFile) && settings == _loadedSettings;
    }

    #region background loading
    private void StartLoad(FileVersion fileVersion, AssimpImportSettings settings, bool reparse)
    {
        _isReloadRequested = false;
        var request = new LoadRequest(++_requestGeneration, fileVersion, settings, reparse, _isIsolated, _materialSource);
        _pendingLoad = request;

        var cancellationToken = request.Cancellation.Token;
        Task.Run(() => RunLoad(request, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Runs on a worker thread. Touches only the request, the parsed-scene cache and the completion
    /// slot, each guarded by its own lock. A cancelled load can still be inside the native import
    /// (which can't be interrupted) while the next one starts, hence the locks.
    /// </summary>
    private void RunLoad(LoadRequest request, CancellationToken cancellationToken)
    {
        LoadOutcome outcome;
        try
        {
            AssimpModelData data;
            object materialIdentity;
            var isIsolated = request.IsIsolated && AssimpIsolatedImport.IsAvailable;
            if (isIsolated)
            {
                // A crash or hang of the native importer only takes down the child process
                materialIdentity = request.File;
                data = AssimpIsolatedImport.Load(request.File.AbsolutePath, request.Settings, CanReuseMaterials(request, materialIdentity),
                                                 cancellationToken);
            }
            else
            {
                var scenes = GetOrImportScenes(request, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                materialIdentity = scenes;
                data = AssimpModelLoader.Convert(scenes, request.Settings, CanReuseMaterials(request, scenes), cancellationToken);
            }

            outcome = new LoadOutcome(request.Generation, data, materialIdentity, isIsolated, request.IsIsolated, null);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            outcome = new LoadOutcome(request.Generation, null, null, false, request.IsIsolated, e.Message);
        }

        lock (_completionLock)
        {
            if (_completedLoad == null || _completedLoad.Generation < outcome.Generation)
                _completedLoad = outcome;
        }
    }

    /// <summary>Decoding textures dominates conversion time; transform-only changes keep the uploaded materials.</summary>
    private static bool CanReuseMaterials(LoadRequest request, object materialIdentity)
    {
        var previous = request.PreviousMaterials;
        return previous != null
               && !request.Reparse // [TriggerReload] exists for changed textures next to an unchanged model file
               && request.Settings.ImportMaterials
               && ReferenceEquals(previous.Identity, materialIdentity)
               && previous.SelectedMeshIndex == request.Settings.SelectedMeshIndex
               && previous.Preset == request.Settings.Preset;
    }

    /// <summary>Reuses the parsed file while only conversion settings change, so tweaking Scale doesn't re-read the file.</summary>
    private AssimpImportedScenes GetOrImportScenes(LoadRequest request, CancellationToken cancellationToken)
    {
        lock (_sceneCacheLock)
        {
            if (!request.Reparse
                && _cachedScenes != null
                && ReferenceEquals(_cachedScenesFile, request.File)
                && _cachedScenes.Preset == request.Settings.Preset)
            {
                return _cachedScenes;
            }
        }

        var scenes = AssimpModelLoader.Import(request.File.AbsolutePath, request.Settings.Preset, cancellationToken);
        lock (_sceneCacheLock)
        {
            _cachedScenes = scenes;
            _cachedScenesFile = request.File;
        }

        return scenes;
    }

    private void TryApplyCompletedLoad()
    {
        LoadOutcome? outcome;
        lock (_completionLock)
        {
            outcome = _completedLoad;
            _completedLoad = null;
        }

        var request = _pendingLoad;
        if (outcome == null || request == null || outcome.Generation != request.Generation)
            return;

        _pendingLoad = null;

        if (outcome.Data == null)
        {
            // Keep showing the last model while a file is rewritten in place; a different file must not look loaded
            if (request.File.AbsolutePath != _loadedFile?.AbsolutePath)
                ClearModel();

            // Remember the failed state, so it isn't retried every frame
            _loadedFile = request.File;
            _loadedSettings = request.Settings;
            _statusLevel = IStatusProvider.StatusLevel.Error;
            _statusMessage = $"Failed loading {Path.GetFileName(request.File.AbsolutePath)}: {outcome.ErrorMessage}";
            Log.Warning(_statusMessage, this);
            return;
        }

        try
        {
            UploadModel(outcome.Data);
            _metadata = BuildMetadata(outcome.Data, outcome.WasIsolated, outcome.IsolationRequested);
            if (!request.Settings.ImportMaterials)
            {
                _materialSource = null;
            }
            else if (!outcome.Data.ReusesPreviousMaterials)
            {
                _materialSource = new MaterialSource(outcome.MaterialIdentity!, request.Settings.SelectedMeshIndex, request.Settings.Preset);
            }
        }
        catch (Exception e)
        {
            _statusLevel = IStatusProvider.StatusLevel.Error;
            _statusMessage = $"Failed creating GPU buffers for {Path.GetFileName(request.File.AbsolutePath)}: {e.Message}";
            Log.Error(_statusMessage, this);
            return;
        }
        finally
        {
            // Also after a failure, so a broken state isn't retried every frame
            _loadedFile = request.File;
            _loadedSettings = request.Settings;
        }

        var data = outcome.Data;
        if (data.Warnings.Count > 0)
        {
            _statusLevel = IStatusProvider.StatusLevel.Warning;
            _statusMessage = string.Join("\n", data.Warnings);
        }
        else
        {
            _statusLevel = IStatusProvider.StatusLevel.Success;
            _statusMessage = $"Loaded in {request.Stopwatch.Elapsed.TotalSeconds:0.00}s";
        }
    }

    private void CancelPendingLoad()
    {
        if (_pendingLoad == null)
            return;

        // Not disposed: the worker may still read the token, and a source without timers holds no native resources
        _pendingLoad.Cancellation.Cancel();
        _pendingLoad = null;
    }
    #endregion

    #region gpu resources
    /// <summary>Render thread only. Buffers are reused across loads; views survive data-only updates.</summary>
    private void UploadModel(AssimpModelData data)
    {
        try
        {
            UploadBuffers(data);
        }
        catch
        {
            // A half-updated set of buffers (e.g. weights from the previous model) must not reach the outputs
            ClearModel();
            throw;
        }

        if (!data.ReusesPreviousMaterials)
        {
            // The previous materials stay alive until the new ones exist, so a failure leaves nothing dangling
            var previousMaterials = _materials;
            _materials = new GpuMaterials();
            try
            {
                _materials.Create(data.Materials);
            }
            finally
            {
                previousMaterials.Dispose();
            }

            foreach (var material in data.Materials)
            {
                material.DropMaps();
            }
        }

        _model = data;
        _setup = CreateSceneSetup(data);

        // The GPU buffers own the data now; keeping the CPU copies would double the model's footprint
        data.DropUploadedBuffers();
    }

    private void UploadBuffers(AssimpModelData data)
    {
        if (data.Vertices.Length > 0 && data.Triangles.Length > 0)
        {
            ResourceManager.SetupBufferWithViews(data.Vertices, ref _meshBuffers.VertexBuffer);
            ResourceManager.SetupBufferWithViews(data.Triangles, ref _meshBuffers.IndicesBuffer);
            ResourceManager.SetupBufferWithViews(data.Chunks, ref _meshBuffers.ChunkDefsBuffer);
            _hasMesh = true;
        }
        else
        {
            _hasMesh = false;
        }

        _hasPoints = data.Points.Length > 0;
        if (_hasPoints)
            ResourceManager.SetupBufferWithViews(data.Points, ref _pointBuffer);

        _hasSkin = data.SkinWeights != null && data.Skeleton != null && _hasMesh;
        if (_hasSkin)
        {
            ResourceManager.SetupBufferWithViews(data.SkinWeights!, ref _skinWeightsBuffer);
            ResourceManager.SetupBufferWithViews(data.SkeletonRestPoints, ref _skeletonPointBuffer);
        }
    }

    /// <summary>
    /// The setup feeds [SampleGltfAnimation] / [PoseToSkinMatrices] with the skeleton and clips, and
    /// [DrawScene] with one draw per run of chunks sharing a material. It shares this operator's
    /// buffers and materials and is never disposed by it, because <see cref="SceneSetup.Dispose()"/>
    /// would release them.
    /// </summary>
    private SceneSetup? CreateSceneSetup(AssimpModelData data)
    {
        if (!_hasMesh)
            return null;

        var setup = new SceneSetup();
        if (_hasSkin)
        {
            setup.Skeletons.Add(data.Skeleton!);
            setup.AnimationClips.AddRange(data.AnimationClips);
        }

        var skinWeights = _hasSkin ? _skinWeightsBuffer! : null!;
        var skeletonIndex = _hasSkin ? 0 : -1;
        setup.RootNodes.Add(new SceneSetup.SceneNode
                                {
                                    Name = "Model",
                                    CombinedTransform = Matrix4x4.Identity,
                                    Transform = new SceneSetup.Transform { Rotation = Quaternion.Identity, Scale = Vector3.One },
                                    MeshBuffers = _meshBuffers,
                                    MeshChunkIndex = 0,
                                    SkinWeights = skinWeights,
                                    SkeletonIndex = skeletonIndex,
                                });

        var chunks = data.Chunks;
        var chunkIndex = 0;
        while (chunkIndex < chunks.Length)
        {
            var materialIndex = data.ChunkMaterialIndices[chunkIndex];
            var startFace = chunks[chunkIndex].StartFaceIndex;
            var faceCount = 0;
            var firstChunkIndex = chunkIndex;
            while (chunkIndex < chunks.Length && data.ChunkMaterialIndices[chunkIndex] == materialIndex)
            {
                faceCount += chunks[chunkIndex].FaceCount;
                chunkIndex++;
            }

            if (faceCount == 0)
                continue;

            setup.Dispatches.Add(new SceneSetup.SceneDrawDispatch
                                     {
                                         MeshBuffers = _meshBuffers,
                                         VertexStartIndex = startFace * 3,
                                         VertexCount = faceCount * 3,
                                         Material = _materials.Get(materialIndex)!,
                                         CombinedTransform = Matrix4x4.Identity,
                                         ChunkIndex = firstChunkIndex,
                                         Scale = Vector3.One,
                                         SkinWeights = skinWeights,
                                         SkeletonIndex = skeletonIndex,
                                     });
        }

        return setup;
    }

    private void ClearModel()
    {
        _materials.Dispose();
        _materials = new GpuMaterials();
        _materialSource = null;
        _model = null;
        _setup = null;
        _hasMesh = false;
        _hasPoints = false;
        _hasSkin = false;
        _loadedFile = null;
        _metadata = string.Empty;
        lock (_sceneCacheLock)
        {
            _cachedScenes = null;
            _cachedScenesFile = null;
        }
    }

    private void WriteOutputs()
    {
        var model = _model;
        MeshData.Value = _hasMesh ? _meshBuffers : null;
        Geometry.Value = model?.Geometry;
        Points.Value = _hasPoints ? _pointBuffer : null;
        Setup.Value = _setup;
        Material.Value = model != null && model.ChunkMaterialIndices.Length > 0 ? _materials.Get(model.ChunkMaterialIndices[0]) : null;
        SkinWeights.Value = _hasSkin ? _skinWeightsBuffer : null;
        SkeletonPoints.Value = _hasSkin ? _skeletonPointBuffer : null;
        Metadata.Value = _metadata;
        VertexCount.Value = model?.VertexCount ?? 0;
        FaceCount.Value = model?.FaceCount ?? 0;
        MeshCount.Value = model?.UsedMeshInstanceCount ?? 0;
        BoundsMin.Value = model?.BoundsMin ?? Vector3.Zero;
        BoundsMax.Value = model?.BoundsMax ?? Vector3.Zero;
        MeshNames.Value = model?.MeshNames ?? string.Empty;
        BoneCount.Value = model?.BoneCount ?? 0;
        AnimationCount.Value = model?.AnimationClips.Count ?? 0;
        AnimationNames.Value = model?.AnimationNames ?? string.Empty;
    }

    private static string BuildMetadata(AssimpModelData data, bool wasIsolated, bool isolationRequested)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Vertices: {data.VertexCount:N0}");
        builder.AppendLine($"Triangles: {data.FaceCount:N0}");
        builder.AppendLine($"Polygons: {data.Geometry?.FaceCount ?? 0:N0}");
        builder.AppendLine($"Meshes: {data.UsedMeshInstanceCount} used, {data.FileMeshCount} in file");
        builder.AppendLine($"Bounds Min: ({data.BoundsMin.X:F2}, {data.BoundsMin.Y:F2}, {data.BoundsMin.Z:F2})");
        builder.AppendLine($"Bounds Max: ({data.BoundsMax.X:F2}, {data.BoundsMax.Y:F2}, {data.BoundsMax.Z:F2})");
        if (data.Skeleton != null)
            builder.AppendLine($"Bones: {data.BoneCount} ({data.Skeleton.ParentIndices.Length} joints)");

        if (data.AnimationClips.Count > 0)
            builder.AppendLine($"Animations: {data.AnimationNames}");

        if (wasIsolated)
        {
            builder.Append("Imported in a separate process");
        }
        else
        {
            if (isolationRequested)
            {
                builder.AppendLine($"Imported in-process: {AssimpIsolatedImport.UnavailableReason}");
            }
            else
            {
                builder.AppendLine("Imported in-process");
            }

            builder.Append($"Assimp {AssimpNative.Version}");
        }
        return builder.ToString();
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        CancelPendingLoad();
        _fileResource.Dispose();
        _meshBuffers.Dispose();
        _pointBuffer?.Dispose();
        _skinWeightsBuffer?.Dispose();
        _skeletonPointBuffer?.Dispose();
        _materials.Dispose();
    }
    #endregion

    /// <summary>
    /// Textures, views and parameter buffers created for one loaded model. Shared default views of
    /// <see cref="PbrMaterial"/> are referenced but never owned.
    /// </summary>
    private sealed class GpuMaterials : IDisposable
    {
        /// <summary>Render thread only.</summary>
        public void Create(List<AssimpMaterialData> materials)
        {
            foreach (var material in materials)
            {
                var pbrMaterial = new PbrMaterial
                                      {
                                          Name = material.Name,
                                          AlbedoMapSrv = CreateView(material.BaseColorMap) ?? PbrMaterial.DefaultAlbedoColorSrv,
                                          EmissiveMapSrv = CreateView(material.EmissiveMap) ?? PbrMaterial.DefaultEmissiveColorSrv,
                                          NormalSrv = CreateView(material.NormalMap) ?? PbrMaterial.DefaultNormalSrv,
                                          RoughnessMetallicOcclusionSrv = CreateView(material.RoughnessMetallicOcclusionMap)
                                                                          ?? PbrMaterial.DefaultRoughnessMetallicOcclusionSrv,
                                          Parameters = new PbrMaterial.PbrParameters
                                                           {
                                                               BaseColor = material.BaseColor,
                                                               EmissiveColor = material.EmissiveColor,
                                                               Roughness = material.Roughness,
                                                               Specular = 1,
                                                               Metal = material.Metallic,
                                                           },
                                      };
                pbrMaterial.UpdateParameterBuffer();
                _materials.Add(pbrMaterial);
            }
        }

        public PbrMaterial? Get(int index) => (uint)index < (uint)_materials.Count ? _materials[index] : null;

        public void Dispose()
        {
            foreach (var material in _materials)
            {
                material.ParameterBuffer?.Dispose();
                material.ParameterBuffer = null!;
            }

            foreach (var view in _views)
            {
                view.Dispose();
            }

            foreach (var texture in _textures)
            {
                texture.Dispose();
            }

            _materials.Clear();
            _views.Clear();
            _textures.Clear();
        }

        /** One view per decoded image; images shared by several materials are decoded once and get one texture. */
        private ShaderResourceView? CreateView(AssimpTextureImage? image)
        {
            if (image == null)
                return null;

            if (_viewsByImage.TryGetValue(image, out var existing))
                return existing;

            var texture = Texture2D.CreateFromRgba(ResourceManager.Device, image.Width, image.Height, image.Rgba);
            _textures.Add(texture);
            var view = new ShaderResourceView(ResourceManager.Device, texture);
            _views.Add(view);
            _viewsByImage[image] = view;
            return view;
        }

        private readonly List<PbrMaterial> _materials = [];
        private readonly List<Texture2D> _textures = [];
        private readonly List<ShaderResourceView> _views = [];
        private readonly Dictionary<AssimpTextureImage, ShaderResourceView> _viewsByImage = new(ReferenceEqualityComparer.Instance);
    }

    #region file resource
    /// <summary>Identity token for one state of the file; replaced by the resource on every change.</summary>
    private sealed record FileVersion(string AbsolutePath);

    private static bool TryCreateFileVersion(FileResource file,
                                             FileVersion? currentValue,
                                             [NotNullWhen(true)] out FileVersion? newValue,
                                             [NotNullWhen(false)] out string? failureReason)
    {
        var absolutePath = file.AbsolutePath;
        if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
        {
            newValue = null;
            failureReason = $"File not found: {absolutePath}";
            return false;
        }

        newValue = new FileVersion(absolutePath);
        failureReason = null;
        return true;
    }

    public InputSlot<string> SourcePathSlot => FilePath;
    #endregion

    #region status provider
    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return _pendingLoad != null && _statusLevel != IStatusProvider.StatusLevel.Error ? IStatusProvider.StatusLevel.Notice : _statusLevel;
    }

    /// <summary>Formatted on demand: the editor only asks while the status is visible.</summary>
    public string GetStatusMessage()
    {
        var pendingLoad = _pendingLoad;
        return pendingLoad != null
                   ? $"Loading {Path.GetFileName(pendingLoad.File.AbsolutePath)}... {pendingLoad.Stopwatch.Elapsed.TotalSeconds:0.0}s"
                   : _statusMessage;
    }
    #endregion

    private sealed class LoadRequest(int generation, FileVersion file, AssimpImportSettings settings, bool reparse, bool isIsolated,
                                     MaterialSource? previousMaterials)
    {
        public readonly MaterialSource? PreviousMaterials = previousMaterials;
        public readonly bool IsIsolated = isIsolated;
        public readonly int Generation = generation;
        public readonly FileVersion File = file;
        public readonly AssimpImportSettings Settings = settings;

        /** Set by TriggerReload, which must read the file again even if it looks unchanged. */
        public readonly bool Reparse = reparse;
        public readonly CancellationTokenSource Cancellation = new();
        public readonly Stopwatch Stopwatch = Stopwatch.StartNew();
    }

    /// <param name="MaterialIdentity">What the materials were read from: the parsed scenes in-process, the file version when isolated.</param>
    /// <param name="IsolationRequested">The setting at the time the load started, for the metadata line.</param>
    private sealed record LoadOutcome(int Generation, AssimpModelData? Data, object? MaterialIdentity, bool WasIsolated, bool IsolationRequested,
                                      string? ErrorMessage);

    /** What the uploaded materials were built from, to decide whether the next load can keep them. */
    private sealed record MaterialSource(object Identity, int SelectedMeshIndex, AssimpPostProcessPresets Preset);

    private readonly Resource<FileVersion> _fileResource;
    private readonly ISlot[] _allOutputs;

    private FileVersion? _requestedFile;
    private bool _isReloadRequested;
    private bool _isIsolated = true;
    private int _requestGeneration;
    private volatile LoadRequest? _pendingLoad;
    private bool _reloadTriggered;

    private FileVersion? _loadedFile;
    private AssimpImportSettings _loadedSettings;

    private readonly object _sceneCacheLock = new();
    private AssimpImportedScenes? _cachedScenes;
    private FileVersion? _cachedScenesFile;

    private readonly object _completionLock = new();
    private LoadOutcome? _completedLoad;

    private AssimpModelData? _model;
    private SceneSetup? _setup;
    private string _metadata = string.Empty;

    private readonly MeshBuffers _meshBuffers = new();
    private GpuMaterials _materials = new();
    private MaterialSource? _materialSource;
    private BufferWithViews? _pointBuffer;
    private BufferWithViews? _skinWeightsBuffer;
    private BufferWithViews? _skeletonPointBuffer;
    private bool _hasMesh;
    private bool _hasPoints;
    private bool _hasSkin;

    private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;
    private string _statusMessage = "No file selected";

    [Input(Guid = "B569D0CC-9F84-419C-A3DB-91406DF6A3F8")]
    public readonly InputSlot<string> FilePath = new();

    [Input(Guid = "6AFF02C4-35C8-4875-BB25-D2B10ADFC276", MappedType = typeof(AssimpPostProcessPresets))]
    public readonly InputSlot<int> PostProcessFlags = new((int)AssimpPostProcessPresets.Basic);

    [Input(Guid = "7A3B8C2D-5E9F-4A7B-8C9D-0E1F2A3B4C5D")]
    public readonly InputSlot<float> Scale = new(1.0f);

    [Input(Guid = "8B4C9D3E-6F0A-4B8C-9D1E-1F2B3C4D5E6F")]
    public readonly InputSlot<bool> Normalize = new(true);

    [Input(Guid = "9C5D0E4F-7A1B-5C9D-2E3F-3B4C5D6E7F8A", MappedType = typeof(AssimpAxisConversions))]
    public readonly InputSlot<int> AxisConversionMode = new((int)AssimpAxisConversions.None);

    [Input(Guid = "1A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D")]
    public readonly InputSlot<bool> FlipNormals = new(false);

    [Input(Guid = "2B3C4D5E-6F7A-8B9C-1D2E-3F4A5B6C7D8E")]
    public readonly InputSlot<int> SelectedMeshIndex = new(-1);

    [Input(Guid = "9D2B6F48-E1C7-4A35-B806-7F3E52C0A9D1")]
    public readonly InputSlot<bool> ImportMaterials = new(true);

    [Input(Guid = "E46A0D8B-35F1-4C27-9B93-C1D85A7E2F60")]
    public readonly InputSlot<bool> IsolateImport = new(true);

    [Input(Guid = "4E9A1C73-D6B2-4F58-8A07-B3C5E91D26F4")]
    public readonly InputSlot<bool> TriggerReload = new();
}
