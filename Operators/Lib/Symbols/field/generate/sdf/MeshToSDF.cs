#nullable enable
using T3.Core.Animation;
using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;
using T3.Graphics.Compat;
using Device = T3.Graphics.Compat.Device;

namespace Lib.field.generate.sdf;

/// <summary>
/// Bakes a signed distance field volume from a triangle mesh using GPU compute shaders
/// (voxelization + jump flooding) and provides it as an SDF shader-graph field that can
/// be rendered with [RaymarchField] etc.
///
/// The algorithm is a port of <see href="https://github.com/Fire-Aalt/com.firealt.mesh-to-sdf">com.firealt.mesh-to-sdf</see>.
///
/// Robustness contract:
/// - Thread safe: all shared state and GPU work is serialized through a single
///   <see cref="_stateLock"/>; the frame guard sits inside it so two evaluation threads
///   can never start duplicate bakes. The status-bar reads are lock-free by design (the
///   fields they touch are volatile) so the UI never waits for a running bake step.
/// - Memory safe: every allocation is bounds-checked before it is used, all resource
///   creation is all-or-nothing (a failed allocation releases the partial set and stays
///   retryable), and the only CPU readback is the fixed 24-byte mesh-bounds struct.
/// - Non blocking: bakes are split into GPU work units that are spread over several frames
///   (see <see cref="BakeFrames"/>). The published texture always contains the last
///   <em>completed</em> bake - the work happens in a separate scratch volume and is handed
///   over with a single GPU copy. The only readback is the 24-byte mesh bounds read when
///   <see cref="AutoFitVolume"/> starts a bake.
/// - Unkillable: every GPU batch is wrapped in exception handling that aborts the bake,
///   fully unbinds the compute stage and reports the failure through the status bar.
///   Every kernel loop is bounded (no data-dependent loop lengths), all numeric inputs are
///   sanitized against NaN/Infinity, and the face count is validated against the D3D11
///   dispatch cap - so a bad mesh or a bad parameter can fail a bake but cannot produce an
///   unbounded GPU loop or crash the application. The exact closest-point mode additionally
///   splits each slice into row bands, so no single dispatch can outrun the OS GPU watchdog
///   and reset the device. Deliberately expensive exact-mode bakes are reported as a warning.
///   A failed bake is retried only after an input changes.
/// - Peak GPU memory scales with <see cref="Resolution"/>: seven volumes at 30 bytes per
///   voxel total ~8 MB at 64, ~63 MB at 128 and ~500 MB at 256.
/// </summary>
[Guid("13f60f14-0553-422b-85de-d7eb8d92912c")]
internal sealed class MeshToSDF : Instance<MeshToSDF>, IGraphNodeOp, IStatusProvider
{
    // All outputs use the Always trigger: bakes are spread over several frames, and a
    // static composition never re-dirties its inputs on its own - the output window
    // would just keep showing cached frames while a bake stalls mid-queue (with the
    // published volume already cleared by the rebake). With Always, each rendered frame
    // re-runs Update (deduplicated by _lastUpdateFrame) so ProcessBakeSteps can pump the
    // queued work units.
    [Output(Guid = "4620f091-a393-4a7b-9eb4-4dbb28f183ed", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<ShaderGraphNode> Result = new();

    [Output(Guid = "2667fcb5-2e48-4154-a7f0-7f172b77e4c8", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Texture3dWithViews> SdfTexture = new();

    /** Increments with each completed bake or re-smooth. Can be used to trigger followup operators. */
    [Output(Guid = "49bbd706-83af-4a99-9640-2f5464640260", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<float> BakeCount = new();

    /** Progress of the running bake (0..1). Stays at 1 while idle. */
    [Output(Guid = "39685a0a-013a-4d21-b925-52f605731b84", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<float> BakeProgress = new();

    /** True while a bake is being spread over frames. */
    [Output(Guid = "6f802b9a-738b-45c1-af85-422c120c0edd", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<bool> BakeInProgress = new();

    public MeshToSDF()
    {
        ShaderNode = new ShaderGraphNode(this);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
        SdfTexture.UpdateAction += Update;
        BakeCount.UpdateAction += Update;
        BakeProgress.UpdateAction += Update;
        BakeInProgress.UpdateAction += Update;

        _clearVoxelsShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "ClearVoxels");
        _clearSdfShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "ClearSdf");
        _meshToVoxelShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "MeshToVoxel");
        _classifyInsideXShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "ClassifyInsideX");
        _classifyInsideYShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "ClassifyInsideY");
        _classifyInsideZShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "ClassifyInsideZ");
        _preprocessShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "Preprocess");
        _jfaShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "Jfa");
        _postprocessShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "Postprocess");
        _closestPointShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "ClosestPointBrute");
        _applySignShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "ApplyInsideSign");
        _initScatterShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "InitSdfScatter");
        _bakeScatterShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "BakeSDFScatter");
        _finalizeScatterShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "FinalizeSdfScatter");
        _smoothFirstShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "SmoothFirstPass");
        _smoothPassShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "SmoothPass");
        _smoothCopyBackShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "SmoothCopyBack");
        _computeBoundsShader = ResourceManager.CreateShaderResource<ComputeShader>(BakeShaderPath, this, () => "ComputeBounds");
    }

    private enum ConversionModes
    {
        JumpFlood_Signed,
        ExactClosestPoint,
        ExactClosestPointScatter,
        JumpFlood_Unsigned,
    }

    private void Update(EvaluationContext context)
    {
        // Evaluation could in principle be re-entered from another playback context while
        // GPU work is being enqueued. The lock makes every state mutation and GPU dispatch
        // atomic. Monitor locks are reentrant, so same-thread recursion stays safe.
        //
        // The frame guard belongs *inside* the lock: two evaluation threads racing on the
        // same frame could otherwise both pass it and enqueue duplicate bakes.
        lock (_stateLock)
        {
            if (_lastUpdateFrame == Playback.FrameCount)
                return;

            _lastUpdateFrame = Playback.FrameCount;

            try
            {
                UpdateInternal(context);
            }
            catch (Exception e)
            {
                // Never let an exception escape into the evaluation - abort any running
                // bake, leave the pipeline clean and report through the status bar.
                AbortBake($"Update failed: {e.Message}");
                Log.Warning($"{nameof(MeshToSDF)}: update failed: {e}", this);
            }
        }
    }

    private void UpdateInternal(EvaluationContext context)
    {
        if (Bake.DirtyFlag.IsDirty)
        {
            Bake.DirtyFlag.Clear();
            _bakeRequested |= Bake.GetValue(context);
        }

        var meshIsDirty = Mesh.DirtyFlag.IsDirty;
        var mesh = Mesh.GetValue(context);

        // Every numeric input is sanitized: a NaN or Infinity coming from a connected
        // field would otherwise propagate into the constant buffer, and a NaN in the
        // shader's sample-count/loop math is the one thing that can stall the GPU.
        var resolution = (int)Sanitize(Resolution.GetValue(context), 128f, 16f, 256f);
        var center = Sanitize(Center.GetValue(context), Vector3.Zero);
        var size = Sanitize(Size.GetValue(context), Vector3.One);
        var sampleDensity = Sanitize(SampleDensity.GetValue(context), 8f, 0.5f, 64f);
        var mode = (ConversionModes)Math.Clamp(Mode.GetValue(context), 0, 3);
        var smoothing = Sanitize(Smoothing.GetValue(context), 0f, 0f, 1f);
        var smoothingRadius = Sanitize(SmoothingRadius.GetValue(context), 2f, 0.25f, 8f);
        var bakeFrames = (int)Sanitize(BakeFrames.GetValue(context), 8f, 1f, 300f);
        var invert = Invert.GetValue(context);
        var autoFit = AutoFitVolume.GetValue(context);

        var smoothingChanged = MathF.Abs(_lastSmoothing - smoothing) > 0.005f
                               || MathF.Abs(_lastSmoothingRadius - smoothingRadius) > 0.01f;
        // Auto-fit bakes ignore Center/Size, and its completion step writes the fitted box into them -
        // comparing them would read that write-back as a change and bake everything a second time.
        var boxChanged = !autoFit
                         && (DistanceSquared(_lastCenter, center) > 0.0001f
                             || DistanceSquared(_lastSize, size) > 0.0001f);
        var volumeChanged = _lastResolution != resolution
                            || boxChanged
                            || MathF.Abs(_lastSampleDensity - sampleDensity) > 0.01f
                            || _lastMode != mode
                            || _lastInvert != invert
                            || _lastAutoFit != autoFit;

        // Null-safe mesh validation (BufferWithViews.Srv can legitimately be null).
        var hasValidMesh = mesh != null
                           && mesh.VertexBuffer?.Srv is { IsDisposed: false }
                           && mesh.IndicesBuffer?.Srv is { IsDisposed: false }
                           && mesh.FaceCount > 0;

        // Any change to the baked inputs clears a previous bake failure so it is retried
        // once - but a persistent failure never falls into a per-frame retry loop.
        // A mesh change during an in-flight bake is coalesced: restarting the step queue
        // every frame meant the bake never reached its finalize step, and each restart paid
        // the bounds readback. The change is latched instead of dropped, because reading
        // Mesh.GetValue() above already consumed the slot's dirty flag - without the latch
        // the new mesh would never be baked (AutoRebake would only fire on the *next* change).
        var autoRebake = AutoRebake.GetValue(context);
        if (meshIsDirty && _bakeSteps != null && autoRebake)
            _meshChangedDuringBake = true;

        var rebakeRequested = _bakeRequested
                              || (autoRebake && (meshIsDirty || _meshChangedDuringBake) && _bakeSteps == null)
                              || volumeChanged;
        if (rebakeRequested)
            _bakeFailed = false;

        var shouldStartBake = hasValidMesh && !_bakeFailed
                              && (rebakeRequested || _needsFirstBake);

        if (shouldStartBake)
        {
            _needsFirstBake = false;
            Mesh.DirtyFlag.Clear(); // consumed by this bake; a new change re-triggers
            try
            {
                StartBake(mesh!, resolution, center, size, sampleDensity, mode, smoothing, smoothingRadius, bakeFrames, invert, autoFit);
                // Consume the latch only when a bake actually started; an aborted start
                // leaves it in place so the change is still baked after the input is fixed.
                if (_bakeSteps != null)
                    _meshChangedDuringBake = false;
            }
            catch (Exception e)
            {
                AbortBake($"Baking failed: {e.Message}");
                Log.Warning($"{nameof(MeshToSDF)}: starting the bake failed: {e}", this);
            }
        }
        else if (_bakeSteps == null && hasValidMesh && !_bakeFailed && smoothingChanged && _lastBakeSucceeded)
        {
            // smoothing-only change: re-apply from the cached unsmoothed volume (no re-bake)
            StartResmooth(smoothing, smoothingRadius, bakeFrames);
        }

        ProcessBakeSteps();

        if (!hasValidMesh && _lastErrorMessage == string.Empty && !_lastBakeSucceeded)
        {
            _lastErrorMessage = "Connect a mesh (e.g. from LoadObj)";
        }

        // Consume a manual Bake pulse only once there is a mesh to bake. Keeping it while
        // no mesh is connected means pulsing Bake first and wiring the mesh afterwards
        // still triggers exactly one bake (and then stops, so it can't loop on a failure).
        if (hasValidMesh)
            _bakeRequested = false;

        // The field shader samples through the Center/Size graph params. When auto-fit
        // is on, the completion step writes the fitted box into those inputs, so the
        // uniforms switch exactly when the baked volume is published (never mid-bake).
        ShaderNode.Update(context);
        if (_sdfViews != null)
        {
            SdfTexture.Value = _sdfViews;
        }

        BakeProgress.Value = _bakeProgress;
        BakeInProgress.Value = _bakeSteps != null;
    }

    #region bake scheduling
    /** Enqueues the GPU work units for a full bake. Work units are executed few-per-frame by ProcessBakeSteps. */
    private void StartBake(MeshBuffers mesh, int resolution, Vector3 center, Vector3 size, float sampleDensity,
                           ConversionModes mode, float smoothing, float smoothingRadius, int bakeFrames, bool invert,
                           bool autoFit)
    {
        var device = ResourceManager.Device;
        if (device == null)
        {
            // Deliberately *not* snapshotted: a missing device is transient (startup,
            // device reset) and should be retried as soon as it appears.
            AbortBake("No graphics device");
            return;
        }

        // Snapshot the attempted settings before any validation early-out: if this bake
        // fails, the inputs no longer compare as "changed" and the failure can't fall
        // into a per-frame retry loop. The user retries by changing an input or pulsing Bake.
        _lastResolution = resolution;
        _lastCenter = center;
        _lastSize = size;
        _lastSampleDensity = sampleDensity;
        _lastMode = mode;
        _lastInvert = invert;
        _lastAutoFit = autoFit;
        _lastSmoothing = smoothing;
        _lastSmoothingRadius = smoothingRadius;

        var clearShader = _clearVoxelsShader.Value;
        var clearSdfShader = _clearSdfShader.Value;
        var meshToVoxelShader = _meshToVoxelShader.Value;
        var classifyX = _classifyInsideXShader.Value;
        var classifyY = _classifyInsideYShader.Value;
        var classifyZ = _classifyInsideZShader.Value;
        var preprocessShader = _preprocessShader.Value;
        var jfaShader = _jfaShader.Value;
        var postprocessShader = _postprocessShader.Value;
        var closestPointShader = _closestPointShader.Value;
        var applySignShader = _applySignShader.Value;
        var initScatterShader = _initScatterShader.Value;
        var bakeScatterShader = _bakeScatterShader.Value;
        var finalizeScatterShader = _finalizeScatterShader.Value;
        var smoothFirstShader = _smoothFirstShader.Value;
        var smoothPassShader = _smoothPassShader.Value;
        var smoothCopyBackShader = _smoothCopyBackShader.Value;

        if (clearShader == null || clearSdfShader == null || meshToVoxelShader == null || classifyX == null || classifyY == null
            || classifyZ == null || preprocessShader == null || jfaShader == null || postprocessShader == null
            || closestPointShader == null || applySignShader == null
            || initScatterShader == null || bakeScatterShader == null || finalizeScatterShader == null
            || _computeBoundsShader.Value == null
            || smoothFirstShader == null || smoothPassShader == null || smoothCopyBackShader == null)
        {
            Log.Warning($"{nameof(MeshToSDF)}: can't compile bake compute shaders", this);
            AbortBake("Can't compile bake compute shaders");
            return;
        }

        var faceCount = mesh.FaceCount;
        if (faceCount > MaxFaces)
        {
            // D3D11 rejects a dispatch above 65535 thread groups. The busiest kernel uses
            // 64 threads per group, so this is the hard ceiling - fail with a readable
            // message instead of letting Dispatch throw an opaque device error.
            var warnKey = $"faces:{faceCount}";
            if (_lastFailureWarnKey != warnKey)
            {
                _lastFailureWarnKey = warnKey;
                Log.Warning($"{nameof(MeshToSDF)}: mesh has {faceCount:N0} faces; the GPU bake supports at most {MaxFaces:N0}", this);
            }

            // Abort rather than just flag: a queue left over from a bake that is still in
            // flight would otherwise run to completion, clear the error and publish stale
            // settings over this failure.
            AbortBake($"Mesh has {faceCount:N0} faces; the GPU bake supports at most {MaxFaces:N0}");
            return;
        }

        // Capture everything the work units need. If any input changes mid-bake the
        // queue is simply rebuilt from scratch on a later frame.
        var context = device.ImmediateContext;
        var csStage = context.ComputeShader;
        var vertexSrv = mesh.VertexBuffer.Srv!;
        var indexSrv = mesh.IndicesBuffer.Srv!;

        // Auto-fit the volume to the mesh bounding box. This is the only synchronous
        // readback in the operator: a single 24-byte staging read when a bake starts.
        var fitted = false;
        if (autoFit)
        {
            var (fitCenter, fitSize) = ComputeMeshBounds(device, csStage, context, vertexSrv, indexSrv, faceCount);
            if (fitSize.X > 0)
            {
                center = fitCenter;
                size = fitSize;
                fitted = true;
            }
            else
            {
                Log.Warning($"{nameof(MeshToSDF)}: auto-fit failed, baking with the manual volume instead", this);
            }
        }

        // The effective box is only known after auto-fit. Deriving the voxel scale from the
        // pre-fit manual Size (as the original port did) made dims = fittedSize * Resolution
        // / manualSize: an oversized volume whose size depended on the unrelated manual box,
        // and distances scaled by manualSize instead of the fitted one.
        var maxAxisSize = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        if (!(maxAxisSize > 0.0001f))
        {
            AbortBake("Volume size must be positive");
            return;
        }

        var voxelScale = resolution / maxAxisSize;
        // Clamp each axis to Resolution: the longest axis is resolution by construction, and
        // the clamp keeps float rounding from ever allocating a volume one voxel over the
        // documented memory budget.
        var dims = new Int3(Math.Clamp((int)MathF.Ceiling(MathF.Abs(size.X) * voxelScale), 1, resolution),
                            Math.Clamp((int)MathF.Ceiling(MathF.Abs(size.Y) * voxelScale), 1, resolution),
                            Math.Clamp((int)MathF.Ceiling(MathF.Abs(size.Z) * voxelScale), 1, resolution));

        if (mode == ConversionModes.ExactClosestPoint)
        {
            var voxels = (long)dims.X * dims.Y * dims.Z;
            var iterations = voxels * faceCount;
            if (iterations > 2_000_000_000L)
            {
                // Only warn once per mesh/resolution combination - scrubbing parameters
                // would otherwise restart bakes (and repeat this warning) every frame.
                var expensiveKey = $"{faceCount}:{dims.X}x{dims.Y}x{dims.Z}";
                if (_lastExpensiveWarnKey != expensiveKey)
                {
                    _lastExpensiveWarnKey = expensiveKey;
                    Log.Warning($"{nameof(MeshToSDF)}: ExactClosestPoint is expensive for this bake ({faceCount} faces x {dims.X}x{dims.Y}x{dims.Z} voxels) - consider JumpFlood mode, a lower Resolution, or more BakeFrames", this);
                }
            }
        }

        var volumesRecreated = EnsureVolumes(dims);
        if (volumesRecreated)
        {
            // The published volume (and with it the SRV bound in the field graph) was
            // replaced. Flag the graph node so the field shader code and its resource
            // references are regenerated - without this, downstream shaders can keep
            // sampling the disposed previous volume (renders solid or empty).
            ShaderNode.FlagCodeChanged();
        }
        if (_sdf == null || _bakeSdf == null || _rawSdf == null || _voxelsA == null || _voxelsB == null || _insideMask == null || _sdfScratch == null)
        {
            AbortBake("Failed to create volume textures");
            return;
        }

        var voxelsA = _voxelsA;
        var voxelsB = _voxelsB;
        var insideMask = _insideMask;
        var bakeTarget = _bakeSdf;
        var rawSdf = _rawSdf;
        var scratch = _sdfScratch;

        _params.ClearValue = Sqrt3; // far-field value for InitSdfScatter/ClearSdf, in stored units
        _params.VoxelSize = dims;
        _params.KernelParam = (uint)faceCount;
        _params.VoxelOrigin = center - size / 2f;
        _params.VoxelScale = voxelScale;
        _params.SampleDensity = sampleDensity;
        _params.DistanceNormalization = MathF.Max(1, resolution);
        _params.InsideVoteThreshold = InsideVoteThreshold;
        _params.MaxSamples = MaxSamplesPerTriangle;
        _params.Signed = mode == ConversionModes.JumpFlood_Unsigned ? 0f : 1f;
        _params.SliceZ = 0;
        _params.BandRowStart = 0;
        _params.Smoothing = 0;
        _params.SmoothingRadius = smoothingRadius;
        _params.Invert = invert ? 1f : 0f;
        UpdateParamsBuffer(device);

        var steps = new Queue<Action>();

        if (volumesRecreated)
        {
            // Give the published volume a defined "everything empty" state until the
            // first bake completes. The scratch target is always fully overwritten.
            var sdfPublished = _sdf!;
            steps.Enqueue(() =>
                          {
                              csStage.Set(clearSdfShader);
                              csStage.SetUnorderedAccessView(UavSdfWrite, sdfPublished.Uav);
                              try
                              {
                                  DispatchVolume(context, dims);
                              }
                              finally
                              {
                                  csStage.SetUnorderedAccessView(UavSdfWrite, null);
                                  csStage.Set(null);
                              }
                          });
        }

        // 1. Clear occupancy and inside mask ------------------------------------
        steps.Enqueue(() =>
                      {
                          csStage.Set(clearShader);
                          csStage.SetUnorderedAccessView(0, voxelsA.Uav);
                          csStage.SetUnorderedAccessView(1, insideMask.Uav);
                          try
                          {
                              DispatchVolume(context, dims);
                          }
                          finally
                          {
                              csStage.SetUnorderedAccessView(0, null);
                              csStage.SetUnorderedAccessView(1, null);
                              csStage.Set(null);
                          }
                      });

        // 2. Rasterize triangle samples into occupancy ---------------------------
        steps.Enqueue(() =>
                      {
                          csStage.Set(meshToVoxelShader);
                          csStage.SetShaderResource(0, vertexSrv);
                          csStage.SetShaderResource(1, indexSrv);
                          csStage.SetUnorderedAccessView(0, voxelsA.Uav);
                          try
                          {
                              const int trisPerGroup = 128;
                              context.Dispatch((faceCount + trisPerGroup - 1) / trisPerGroup, 1, 1);
                          }
                          finally
                          {
                              csStage.SetShaderResource(0, null);
                              csStage.SetShaderResource(1, null);
                              csStage.SetUnorderedAccessView(0, null);
                              csStage.Set(null);
                          }
                      });

        // 3. Inside mask by parity voting along each axis ------------------------
        //    (not needed for unsigned jump flood)
        if (mode != ConversionModes.JumpFlood_Unsigned)
        {
            steps.Enqueue(() =>
                          {
                              csStage.Set(classifyX);
                              csStage.SetShaderResource(TexVoxelRead, voxelsA.Srv);
                              csStage.SetUnorderedAccessView(UavInsideMask, insideMask.Uav);
                              try
                              {
                                  context.Dispatch(1, CeilDiv(dims.Y, 8), CeilDiv(dims.Z, 8));
                              }
                              finally
                              {
                                  csStage.SetShaderResource(TexVoxelRead, null);
                                  csStage.SetUnorderedAccessView(UavInsideMask, null);
                                  csStage.Set(null);
                              }
                          });
            steps.Enqueue(() =>
                          {
                              csStage.Set(classifyY);
                              csStage.SetShaderResource(TexVoxelRead, voxelsA.Srv);
                              csStage.SetUnorderedAccessView(UavInsideMask, insideMask.Uav);
                              try
                              {
                                  context.Dispatch(CeilDiv(dims.X, 8), 1, CeilDiv(dims.Z, 8));
                              }
                              finally
                              {
                                  csStage.SetShaderResource(TexVoxelRead, null);
                                  csStage.SetUnorderedAccessView(UavInsideMask, null);
                                  csStage.Set(null);
                              }
                          });
            steps.Enqueue(() =>
                          {
                              csStage.Set(classifyZ);
                              csStage.SetShaderResource(TexVoxelRead, voxelsA.Srv);
                              csStage.SetUnorderedAccessView(UavInsideMask, insideMask.Uav);
                              try
                              {
                                  context.Dispatch(CeilDiv(dims.X, 8), CeilDiv(dims.Y, 8), 1);
                              }
                              finally
                              {
                                  csStage.SetShaderResource(TexVoxelRead, null);
                                  csStage.SetUnorderedAccessView(UavInsideMask, null);
                                  csStage.Set(null);
                              }
                          });
        }

        switch (mode)
        {
            case ConversionModes.ExactClosestPointScatter:
            {
                // Exact distances scattered per triangle (AMD TressFX algorithm, the
                // one vvvv's VL.Fuse.DomainExtensions MeshToSDF uses). Distances are
                // exact within a ~15 cell shell around the mesh.
                steps.Enqueue(() =>
                              {
                                  csStage.Set(initScatterShader);
                                  csStage.SetUnorderedAccessView(UavScratch, scratch.Uav);
                                  try
                                  {
                                      DispatchVolume(context, dims);
                                  }
                                  finally
                                  {
                                      csStage.SetUnorderedAccessView(UavScratch, null);
                                      csStage.Set(null);
                                  }
                              });
                steps.Enqueue(() =>
                              {
                                  _params.KernelParam = (uint)faceCount;
                                  UpdateParamsBuffer(device);
                                  csStage.Set(bakeScatterShader);
                                  csStage.SetShaderResource(0, vertexSrv);
                                  csStage.SetShaderResource(1, indexSrv);
                                  csStage.SetUnorderedAccessView(UavScratch, scratch.Uav);
                                  try
                                  {
                                      context.Dispatch((faceCount + 63) / 64, 1, 1);
                                  }
                                  finally
                                  {
                                      csStage.SetShaderResource(0, null);
                                      csStage.SetShaderResource(1, null);
                                      csStage.SetUnorderedAccessView(UavScratch, null);
                                      csStage.Set(null);
                                  }
                              });
                steps.Enqueue(() =>
                              {
                                  csStage.Set(finalizeScatterShader);
                                  csStage.SetShaderResource(TexScratchRead, scratch.Srv);
                                  csStage.SetShaderResource(TexInsideMaskRead, insideMask.Srv);
                                  csStage.SetUnorderedAccessView(UavSdfWrite, bakeTarget.Uav);
                                  try
                                  {
                                      DispatchVolume(context, dims);
                                  }
                                  finally
                                  {
                                      csStage.SetShaderResource(TexScratchRead, null);
                                      csStage.SetShaderResource(TexInsideMaskRead, null);
                                      csStage.SetUnorderedAccessView(UavSdfWrite, null);
                                      csStage.Set(null);
                                  }
                              });
                break;
            }

            case ConversionModes.ExactClosestPoint:
            {
                // 4. Exact distance from every voxel center to the nearest triangle.
                //    Each z slice is its own work unit so the bake can be spread over
                //    frames and the GPU can be preempted in between. Inside a slice the
                //    rows are split into bands, because the work per dispatch is
                //    rows * dims.X * faceCount and a single dispatch that runs longer than
                //    the OS GPU watchdog (~2s) resets the device and takes the app with it.
                var rowsPerBand = ComputeRowsPerBand(dims, faceCount);
                for (var z = 0; z < dims.Z; z++)
                {
                    for (var rowStart = 0; rowStart < dims.Y; rowStart += rowsPerBand)
                    {
                        var sliceZ = z;
                        var bandStart = rowStart;
                        var bandRows = Math.Min(rowsPerBand, dims.Y - rowStart);
                        steps.Enqueue(() =>
                                      {
                                          _params.SliceZ = sliceZ;
                                          _params.BandRowStart = bandStart;
                                          UpdateParamsBuffer(device);
                                          csStage.Set(closestPointShader);
                                          csStage.SetShaderResource(0, vertexSrv);
                                          csStage.SetShaderResource(1, indexSrv);
                                          csStage.SetUnorderedAccessView(UavVoxelWrite, voxelsB.Uav);
                                          try
                                          {
                                              context.Dispatch(CeilDiv(dims.X, 8), CeilDiv(bandRows, 8), 1);
                                          }
                                          finally
                                          {
                                              csStage.SetShaderResource(0, null);
                                              csStage.SetShaderResource(1, null);
                                              csStage.SetUnorderedAccessView(UavVoxelWrite, null);
                                              csStage.Set(null);
                                          }
                                      });
                    }
                }

                // 5. Apply the inside mask sign ----------------------------------
                steps.Enqueue(() =>
                              {
                                  _params.SliceZ = 0;
                                  UpdateParamsBuffer(device);
                                  csStage.Set(applySignShader);
                                  csStage.SetShaderResource(TexVoxelRead, voxelsB.Srv);
                                  csStage.SetShaderResource(TexInsideMaskRead, insideMask.Srv);
                                  csStage.SetUnorderedAccessView(UavSdfWrite, bakeTarget.Uav);
                                  try
                                  {
                                      DispatchVolume(context, dims);
                                  }
                                  finally
                                  {
                                      csStage.SetShaderResource(TexVoxelRead, null);
                                      csStage.SetShaderResource(TexInsideMaskRead, null);
                                      csStage.SetUnorderedAccessView(UavSdfWrite, null);
                                      csStage.Set(null);
                                  }
                              });
                break;
            }

            default:
            {
                // 4. Jump flooding: spread nearest surface seeds ---------------------
                steps.Enqueue(() =>
                              {
                                  _params.KernelParam = 0;
                                  UpdateParamsBuffer(device);
                                  csStage.Set(preprocessShader);
                                  csStage.SetShaderResource(TexVoxelRead, voxelsA.Srv);
                                  csStage.SetUnorderedAccessView(UavVoxelWrite, voxelsB.Uav);
                                  try
                                  {
                                      DispatchVolume(context, dims);
                                  }
                                  finally
                                  {
                                      csStage.SetShaderResource(TexVoxelRead, null);
                                      csStage.SetUnorderedAccessView(UavVoxelWrite, null);
                                      csStage.Set(null);
                                  }
                              });

                var read = voxelsB;
                var write = voxelsA;
                var maxSide = Math.Max(dims.X, Math.Max(dims.Y, dims.Z));
                for (var offset = Math.Max(1, NextPowerOfTwo(maxSide) / 2); offset >= 1; offset /= 2)
                {
                    var floodOffset = offset;
                    var readTexture = read;
                    var writeTexture = write;
                    steps.Enqueue(() =>
                                  {
                                      _params.KernelParam = (uint)floodOffset;
                                      UpdateParamsBuffer(device);
                                      csStage.Set(jfaShader);
                                      csStage.SetShaderResource(TexVoxelRead, readTexture.Srv);
                                      csStage.SetUnorderedAccessView(UavVoxelWrite, writeTexture.Uav);
                                      try
                                      {
                                          DispatchVolume(context, dims);
                                      }
                                      finally
                                      {
                                          csStage.SetShaderResource(TexVoxelRead, null);
                                          csStage.SetUnorderedAccessView(UavVoxelWrite, null);
                                          csStage.Set(null);
                                      }
                                  });
                    (read, write) = (write, read);
                }

                // 5. Combine seed distances with the inside mask sign ----------------
                steps.Enqueue(() =>
                              {
                                  _params.KernelParam = 0;
                                  UpdateParamsBuffer(device);
                                  csStage.Set(postprocessShader);
                                  csStage.SetShaderResource(TexVoxelRead, read.Srv);
                                  csStage.SetShaderResource(TexInsideMaskRead, insideMask.Srv);
                                  csStage.SetUnorderedAccessView(UavSdfWrite, bakeTarget.Uav);
                                  try
                                  {
                                      DispatchVolume(context, dims);
                                  }
                                  finally
                                  {
                                      csStage.SetShaderResource(TexVoxelRead, null);
                                      csStage.SetShaderResource(TexInsideMaskRead, null);
                                      csStage.SetUnorderedAccessView(UavSdfWrite, null);
                                      csStage.Set(null);
                                  }
                              });
                break;
            }
        }

        // 6. Cache the unsmoothed volume, then smooth it (all modes) -------------
        steps.Enqueue(() =>
                      {
                          context.CopyResource(bakeTarget.Texture, rawSdf.Texture);
                      });

        if (smoothing > 0.001f)
        {
            AppendSmoothingSteps(steps, device, csStage, context, bakeTarget, voxelsA, voxelsB, dims,
                                 smoothFirstShader, smoothPassShader, smoothCopyBackShader, smoothing, smoothingRadius);
        }

        // 7. Hand the finished volume over to the published texture with one copy.
        //    Downstream shaders only ever see complete bakes.
        var publishedSdf = _sdf!;
        var finalCenter = center;
        var finalSize = size;
        var centerInput = Center;
        var sizeInput = Size;
        // The largest actual volume dimension. With auto-fit it must equal Resolution
        // (the fitted box is scaled so its longest axis spans the full voxel budget).
        var maxDim = Math.Max(dims.X, Math.Max(dims.Y, dims.Z));
        steps.Enqueue(() =>
                      {
                          context.CopyResource(bakeTarget.Texture, publishedSdf.Texture);
                          if (fitted)
                          {
                              // Publish the fitted box through the graph-param inputs so the
                              // field uniforms switch together with the baked volume.
                              centerInput.SetTypedInputValue(finalCenter);
                              sizeInput.SetTypedInputValue(finalSize);
                          }
                          _lastBakeSucceeded = true;
                          _bakeFailed = false;
                          _bakeSteps = null;
                          _bakeProgress = 1f;
                          BakeCount.Value = ++_bakeCount;
                          _lastErrorMessage = string.Empty;
                          Log.Debug($"{nameof(MeshToSDF)}: baked SDF volume ({mode}, {maxDim} voxels max axis, {faceCount} faces, {sampleDensity:0.#} samples/voxel², smoothing {smoothing:0.##}/{smoothingRadius:0.##}, volume center {finalCenter:F2} size {finalSize:F2}{(fitted ? ", auto-fitted" : "")})", this);
                      });

        _bakeSteps = steps;
        _bakeStepsTotal = steps.Count;
        _bakeMeshVertexSrv = vertexSrv;
        _bakeMeshIndexSrv = indexSrv;
        _stepsPerFrame = Math.Max(1, CeilDiv(_bakeStepsTotal, bakeFrames));
        _bakeProgress = 0f;
    }

    /** Enqueues a smoothing-only pass from the cached unsmoothed volume (smoothing changes without re-bake). */
    private void StartResmooth(float smoothing, float smoothingRadius, int bakeFrames)
    {
        var device = ResourceManager.Device;
        var smoothFirstShader = _smoothFirstShader.Value;
        var smoothPassShader = _smoothPassShader.Value;
        var smoothCopyBackShader = _smoothCopyBackShader.Value;

        if (device == null || smoothFirstShader == null || smoothPassShader == null || smoothCopyBackShader == null)
            return;

        if (_rawSdf is not { Srv.IsDisposed: false } || _sdf is not { Srv.IsDisposed: false }
            || _bakeSdf is not { Srv.IsDisposed: false }
            || _voxelsA is not { Srv.IsDisposed: false } || _voxelsB is not { Srv.IsDisposed: false })
            return;

        var context = device.ImmediateContext;
        var csStage = context.ComputeShader;
        var bakeTarget = _bakeSdf!;
        var rawSdf = _rawSdf;
        var publishedSdf = _sdf!;
        var voxelsA = _voxelsA;
        var voxelsB = _voxelsB;
        var dims = _dims;

        // snapshot first so a failed re-smooth can't retry in a per-frame loop
        _lastSmoothing = smoothing;
        _lastSmoothingRadius = smoothingRadius;

        var steps = new Queue<Action>();
        // restore the unsmoothed volume into the scratch target, then smooth it there
        steps.Enqueue(() => { context.CopyResource(rawSdf.Texture, bakeTarget.Texture); });
        AppendSmoothingSteps(steps, device, csStage, context, bakeTarget, voxelsA, voxelsB, dims,
                             smoothFirstShader, smoothPassShader, smoothCopyBackShader, smoothing, smoothingRadius);
        steps.Enqueue(() =>
                      {
                          context.CopyResource(bakeTarget.Texture, publishedSdf.Texture);
                          _bakeSteps = null;
                          _bakeProgress = 1f;
                          BakeCount.Value = ++_bakeCount;
                      });

        // A re-smooth doesn't touch the mesh - clear the captured views so the
        // mid-bake guard can't trip on mesh buffers replaced since the last bake.
        _bakeMeshVertexSrv = null;
        _bakeMeshIndexSrv = null;

        _bakeSteps = steps;
        _bakeStepsTotal = steps.Count;
        _stepsPerFrame = Math.Max(1, CeilDiv(_bakeStepsTotal, bakeFrames));
        _bakeProgress = 0f;
    }

    /** Shared smoothing passes: bakeTarget -> voxelsA -> voxelsB -> bakeTarget. Must be called within a running bake setup. */
    private void AppendSmoothingSteps(Queue<Action> steps, Device device, ComputeShaderStage csStage,
                                      DeviceContext context, VolumeTexture bakeTarget, VolumeTexture voxelsA, VolumeTexture voxelsB,
                                      Int3 dims, ComputeShader smoothFirstShader, ComputeShader smoothPassShader, ComputeShader smoothCopyBackShader,
                                      float smoothing, float smoothingRadius)
    {
        steps.Enqueue(() =>
                      {
                          _params.Smoothing = smoothing;
                          _params.SmoothingRadius = smoothingRadius;
                          UpdateParamsBuffer(device);
                          csStage.Set(smoothFirstShader);
                          csStage.SetSampler(0, ResourceManager.DefaultSamplerState);
                          // the neighbor average samples VoxelRead (t2) - bind the source volume there too
                          csStage.SetShaderResource(TexVoxelRead, bakeTarget.Srv);
                          csStage.SetShaderResource(TexSdfRead, bakeTarget.Srv);
                          csStage.SetUnorderedAccessView(UavVoxelWrite, voxelsA.Uav);
                          try
                          {
                              DispatchVolume(context, dims);
                          }
                          finally
                          {
                              csStage.SetShaderResource(TexVoxelRead, null);
                              csStage.SetShaderResource(TexSdfRead, null);
                              csStage.SetUnorderedAccessView(UavVoxelWrite, null);
                              csStage.SetSampler(0, null);
                              csStage.Set(null);
                          }
                      });

        steps.Enqueue(() =>
                      {
                          _params.Smoothing = smoothing;
                          _params.SmoothingRadius = smoothingRadius;
                          UpdateParamsBuffer(device);
                          csStage.Set(smoothPassShader);
                          csStage.SetSampler(0, ResourceManager.DefaultSamplerState);
                          csStage.SetShaderResource(TexVoxelRead, voxelsA.Srv);
                          csStage.SetUnorderedAccessView(UavVoxelWrite, voxelsB.Uav);
                          try
                          {
                              DispatchVolume(context, dims);
                          }
                          finally
                          {
                              csStage.SetShaderResource(TexVoxelRead, null);
                              csStage.SetUnorderedAccessView(UavVoxelWrite, null);
                              csStage.SetSampler(0, null);
                              csStage.Set(null);
                          }
                      });

        steps.Enqueue(() =>
                      {
                          _params.Smoothing = smoothing;
                          _params.SmoothingRadius = smoothingRadius;
                          UpdateParamsBuffer(device);
                          csStage.Set(smoothCopyBackShader);
                          csStage.SetShaderResource(TexVoxelRead, voxelsB.Srv);
                          csStage.SetUnorderedAccessView(UavSdfWrite, bakeTarget.Uav);
                          try
                          {
                              DispatchVolume(context, dims);
                          }
                          finally
                          {
                              csStage.SetShaderResource(TexVoxelRead, null);
                              csStage.SetUnorderedAccessView(UavSdfWrite, null);
                              csStage.Set(null);
                          }
                      });
    }

    /**
     * Runs the queued bake work units, at most _stepsPerFrame per evaluation, so heavy
     * bakes never monopolize a frame. Any failure aborts the remaining units, unbinds
     * the compute stage and reports the error - it never crashes or blocks the app.
     */
    private void ProcessBakeSteps()
    {
        if (_bakeSteps == null)
            return;

        var device = ResourceManager.Device;
        if (device == null)
        {
            AbortBake("No graphics device");
            return;
        }

        try
        {
            // Other operators' compute passes steal cbuffer slot b0 between frames, so the
            // buffer must be (re)bound every frame before running this frame's work units.
            if (_paramsBuffer == null)
                throw new OperationCanceledException("params buffer was released mid-bake");
            var csStage = device.ImmediateContext.ComputeShader;
            csStage.SetConstantBuffer(0, _paramsBuffer);

            var budget = _stepsPerFrame;
            while (_bakeSteps != null && _bakeSteps.Count > 0 && budget-- > 0)
            {
                // The mesh buffers or volumes could have been disposed mid-bake
                // (e.g. an upstream operator recreated them). Bail out cleanly.
                if (_bakeMeshVertexSrv is { IsDisposed: true } || _bakeMeshIndexSrv is { IsDisposed: true }
                    || _voxelsA is not { Srv.IsDisposed: false }
                    || _voxelsB is not { Srv.IsDisposed: false }
                    || _sdf is not { Srv.IsDisposed: false }
                    || _bakeSdf is not { Srv.IsDisposed: false })
                {
                    throw new OperationCanceledException("bake resources were released mid-bake");
                }

                var step = _bakeSteps.Dequeue();
                step();
            }
        }
        catch (OperationCanceledException e)
        {
            // Benign: the mesh or volumes were replaced while the bake was spread over
            // frames. Abort quietly - a new bake starts as soon as inputs are valid again.
            AbortBake("Bake aborted because its inputs were released mid-bake. Change an input or pulse Bake to rebake.");
            Log.Debug($"{nameof(MeshToSDF)}: bake aborted: {e.Message}", this);
        }
        catch (Exception e)
        {
            AbortBake($"Baking failed: {e.Message}");
            Log.Warning($"{nameof(MeshToSDF)}: baking failed: {e}", this);
        }
        finally
        {
            if (_bakeSteps is { Count: 0 })
            {
                // Defensive: the finalize step should have completed the queue. Treat a
                // drained-but-unfinalized queue as done so the op never stays "baking".
                _bakeSteps = null;
                _bakeProgress = 1f;
            }

            UpdateBakeProgress();
            UnbindComputeStage(device.ImmediateContext);
        }
    }

    /** Aborts a running bake: clears the queue, blocks auto-retries and reports the reason. */
    private void AbortBake(string reason)
    {
        _bakeSteps = null;
        _bakeProgress = 1f;
        _bakeFailed = true;
        _lastErrorMessage = reason;

        var device = ResourceManager.Device;
        if (device != null)
        {
            try
            {
                UnbindComputeStage(device.ImmediateContext);
            }
            catch
            {
                // the device itself is broken - nothing left to clean up
            }
        }
    }

    /** Unbinds everything this operator ever binds on the compute stage so a failed or finished bake can never poison the pipeline. */
    private static void UnbindComputeStage(DeviceContext context)
    {
        try
        {
            var csStage = context.ComputeShader;
            for (var i = 0; i < 8; i++)
            {
                csStage.SetShaderResource(i, null);
                csStage.SetUnorderedAccessView(i, null);
            }

            for (var i = 0; i < 4; i++)
            {
                csStage.SetSampler(i, null);
            }

            for (var i = 0; i < 4; i++)
            {
                csStage.SetConstantBuffer(i, null);
            }

            csStage.Set(null);
        }
        catch
        {
            // a broken device must not turn cleanup into a crash
        }
    }

    private void UpdateBakeProgress()
    {
        if (_bakeSteps == null || _bakeStepsTotal <= 0)
        {
            _bakeProgress = 1f;
            return;
        }

        var done = _bakeStepsTotal - _bakeSteps.Count;
        _bakeProgress = Math.Clamp((float)done / _bakeStepsTotal, 0f, 0.99f);
    }
    #endregion

    /** All volume kernels use [numthreads(8,8,8)]. */
    private static void DispatchVolume(DeviceContext context, Int3 dims)
    {
        context.Dispatch(CeilDiv(dims.X, 8), CeilDiv(dims.Y, 8), CeilDiv(dims.Z, 8));
    }

    /**
     * Rows of one z slice that a single ClosestPointBrute dispatch may cover.
     * The work of a dispatch is rows * dims.X * faceCount distance tests; keeping that
     * near 1e9 keeps each dispatch well inside the OS GPU watchdog window (~2s), so a
     * heavy exact-mode bake is slow but can never reset the graphics device.
     */
    private static int ComputeRowsPerBand(Int3 dims, int faceCount)
    {
        const long testsPerDispatchBudget = 1_000_000_000L;
        var testsPerRow = (long)dims.X * Math.Max(1, faceCount);
        var rows = testsPerDispatchBudget / testsPerRow;
        return (int)Math.Clamp(rows, 1, dims.Y);
    }

    private static float DistanceSquared(Vector3 a, Vector3 b)
    {
        var d = a - b;
        return d.X * d.X + d.Y * d.Y + d.Z * d.Z;
    }

    /** Returns a finite, clamped value - or the fallback when the input is NaN/Infinity. */
    private static float Sanitize(float value, float fallback, float min, float max)
        => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    /** Returns the vector only when all components are finite, otherwise the fallback. */
    private static Vector3 Sanitize(Vector3 value, Vector3 fallback)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) ? value : fallback;

    /**
     * Reduces the mesh bounding box on the GPU (ComputeBounds kernel) and reads the
     * 24-byte result back through a staging buffer. Called once per bake start when
     * AutoFitVolume is enabled. Returns size.X <= 0 when the readback fails; the caller
     * then keeps the user's Center/Size.
     */
    private (Vector3 Center, Vector3 Size) ComputeMeshBounds(Device device, ComputeShaderStage csStage,
                                                             DeviceContext context, ShaderResourceView vertexSrv,
                                                             ShaderResourceView indexSrv, int faceCount)
    {
        var boundsShader = _computeBoundsShader.Value;
        if (boundsShader == null)
            return (Vector3.Zero, Vector3.Zero);

        try
        {
            if (_boundsBuffer == null || _boundsStaging == null)
            {
                Utilities.Dispose(ref _boundsUav);
                Utilities.Dispose(ref _boundsBuffer);
                Utilities.Dispose(ref _boundsStaging);
                _boundsBuffer = new Buffer(device, new BufferDescription
                                                     {
                                                         SizeInBytes = 24,
                                                         Usage = ResourceUsage.Default,
                                                         BindFlags = BindFlags.UnorderedAccess,
                                                         CpuAccessFlags = CpuAccessFlags.None,
                                                         OptionFlags = ResourceOptionFlags.BufferStructured,
                                                         StructureByteStride = 12,
                                                     });
                _boundsStaging = new Buffer(device, new BufferDescription
                                                    {
                                                        SizeInBytes = 24,
                                                        Usage = ResourceUsage.Staging,
                                                        BindFlags = BindFlags.None,
                                                        CpuAccessFlags = CpuAccessFlags.Read,
                                                        OptionFlags = ResourceOptionFlags.BufferStructured,
                                                        StructureByteStride = 12,
                                                    });
                _boundsUav = new UnorderedAccessView(device, _boundsBuffer);
            }

            _params.KernelParam = (uint)faceCount;
            UpdateParamsBuffer(device);
            // This runs inside StartBake, before ProcessBakeSteps binds the params
            // buffer for the frame - without binding it here the kernel would read
            // another operator's cbuffer and see KernelParam == 0.
            csStage.SetConstantBuffer(0, _paramsBuffer);
            csStage.Set(boundsShader);
            csStage.SetShaderResource(0, vertexSrv);
            csStage.SetShaderResource(1, indexSrv);
            csStage.SetUnorderedAccessView(UavBounds, _boundsUav);
            try
            {
                context.Dispatch(1, 1, 1);
            }
            finally
            {
                // Unbind even when the dispatch throws: a half-bound compute stage would
                // otherwise stay behind for every following operator in the frame.
                csStage.SetUnorderedAccessView(UavBounds, null);
                csStage.SetShaderResource(0, null);
                csStage.SetShaderResource(1, null);
                csStage.Set(null);
                csStage.SetConstantBuffer(0, null);
            }

            context.CopyResource(_boundsBuffer, _boundsStaging);
            var box = context.MapSubresource(_boundsStaging, 0, MapMode.Read, MapFlags.None);
            try
            {
                var bounds = System.Runtime.InteropServices.Marshal.PtrToStructure<BoundsData>(box.DataPointer);
                var size = bounds.Max - bounds.Min;
                if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || !float.IsFinite(size.Z)
                    || size.X <= 0.0001f || size.Y <= 0.0001f || size.Z <= 0.0001f)
                    return (Vector3.Zero, Vector3.Zero);

                // Pad ~5% so the surface doesn't sit exactly on the volume border
                // (raymarching and the border clamp need a little air).
                var paddedSize = size * 1.05f;
                var center = (bounds.Min + bounds.Max) / 2f;
                return (center, paddedSize);
            }
            finally
            {
                context.UnmapSubresource(_boundsStaging, 0);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"{nameof(MeshToSDF)}: mesh bounds readback failed: {e.Message}", this);
            return (Vector3.Zero, Vector3.Zero);
        }
    }

    private bool EnsureVolumes(Int3 dims)
    {
        if (_dims == dims && _voxelsA != null && _voxelsB != null && _insideMask != null
            && _sdf != null && _bakeSdf != null && _sdfScratch != null && _rawSdf != null)
            return false;

        // Build the new set first and only replace the current one once all seven volumes
        // exist. Disposing the published volume up front would throw away the last good
        // bake when the new allocation fails (e.g. out of memory at 256), leaving downstream
        // shaders sampling a disposed SRV until the next successful bake.
        var created = new List<VolumeTexture>(7);
        try
        {
            var voxelsA = CreateVolume(dims, Format.R16G16B16A16_Float);
            created.Add(voxelsA);
            var voxelsB = CreateVolume(dims, Format.R16G16B16A16_Float);
            created.Add(voxelsB);
            var insideMask = CreateVolume(dims, Format.R32_Float);
            created.Add(insideMask);
            var sdf = CreateVolume(dims, Format.R16_Float);
            created.Add(sdf);
            var bakeSdf = CreateVolume(dims, Format.R16_Float);
            created.Add(bakeSdf);
            var sdfScratch = CreateVolume(dims, Format.R32_UInt);
            created.Add(sdfScratch);
            var rawSdf = CreateVolume(dims, Format.R16_Float);
            created.Add(rawSdf);

            // Success - now it is safe to release the previous set.
            DisposeVolumes();

            _voxelsA = voxelsA;
            _voxelsB = voxelsB;
            _insideMask = insideMask;
            _sdf = sdf;
            _bakeSdf = bakeSdf;
            _sdfScratch = sdfScratch;
            _rawSdf = rawSdf;
            _dims = dims;

            _sdfViews = new Texture3dWithViews
                            {
                                Texture = sdf.Texture,
                                Srv = sdf.Srv,
                                Uav = sdf.Uav,
                            };
            return true;
        }
        catch
        {
            // Release only what this attempt created; the previously published volume and
            // _dims are left untouched so the last completed bake stays valid and the bake
            // can be retried once memory is available.
            foreach (var volume in created)
            {
                try
                {
                    volume.Dispose();
                }
                catch
                {
                    // a failed disposal must not mask the allocation error
                }
            }

            throw;
        }
    }

    private static VolumeTexture CreateVolume(Int3 dims, Format format)
    {
        var device = ResourceManager.Device;
        var texture3D = Texture3D.CreateTexture3D(new Texture3DDescription
                                                      {
                                                          Width = dims.X,
                                                          Height = dims.Y,
                                                          Depth = dims.Z,
                                                          MipLevels = 1,
                                                          Format = format,
                                                          Usage = ResourceUsage.Default,
                                                          BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                                                          CpuAccessFlags = CpuAccessFlags.None,
                                                          OptionFlags = ResourceOptionFlags.None,
                                                      });

        // The parameter-less view constructors derive format and Texture3D dimension from the resource
        var srv = new ShaderResourceView(device, texture3D);
        var uav = new UnorderedAccessView(device, texture3D);

        return new VolumeTexture(texture3D, srv, uav);
    }

    private void UpdateParamsBuffer(Device device)
    {
        if (_paramsBuffer == null)
        {
            _paramsBuffer = new Buffer(device, new BufferDescription
                                                   {
                                                       Usage = ResourceUsage.Default,
                                                       SizeInBytes = ParamsSizeInBytes,
                                                       BindFlags = BindFlags.ConstantBuffer,
                                                       CpuAccessFlags = CpuAccessFlags.None,
                                                   });
        }

        device.ImmediateContext.UpdateSubresource(ref _params, _paramsBuffer);
    }

    private void DisposeVolumes()
    {
        _sdfViews = null;
        Utilities.Dispose(ref _voxelsA);
        Utilities.Dispose(ref _voxelsB);
        Utilities.Dispose(ref _insideMask);
        Utilities.Dispose(ref _sdf);
        Utilities.Dispose(ref _bakeSdf);
        Utilities.Dispose(ref _sdfScratch);
        Utilities.Dispose(ref _rawSdf);
    }

    private sealed class VolumeTexture : IDisposable
    {
        public VolumeTexture(Texture3D texture, ShaderResourceView srv, UnorderedAccessView uav)
        {
            Texture = texture;
            Srv = srv;
            Uav = uav;
        }

        public readonly Texture3D Texture;
        public readonly ShaderResourceView Srv;
        public readonly UnorderedAccessView Uav;

        public void Dispose()
        {
            Srv.Dispose();
            Uav.Dispose();
            Texture.Dispose();
        }
    }

    private static int CeilDiv(int value, int divisor) => (value + divisor - 1) / divisor;

    private static int NextPowerOfTwo(int value)
    {
        var p = 1;
        while (p < value)
        {
            p <<= 1;
        }

        return p;
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Definitions.Append($$"""
                               float sdMeshToSdf{{ShaderNode}}(float3 p, float3 center, float3 size, float offset) {
                                   float3 extent = max(size, 0.0001);
                                   float maxAxis = max(extent.x, max(extent.y, extent.z));
                                   float3 q = (p - center) / extent;
                                   float d = {{ShaderNode}}SdfVolume.SampleLevel(ClampedSampler, clamp(q + 0.5, 0.0, 1.0), 0) * maxAxis;
                                   float3 outDelta = max(abs(q) - 0.5, 0.0) * extent;
                                   d += length(outDelta) + offset;
                                   return d;
                               }
                               """);
    }

    bool IGraphNodeOp.TryBuildCustomCode(CodeAssembleContext c)
    {
        c.AppendCall($"""
                      f{c}.w = sdMeshToSdf{ShaderNode}(p{c}.xyz, {ShaderNode}Center, {ShaderNode}Size, {ShaderNode}Offset);
                      f{c}.xyz = p{c}.w < 0.5 ?  p{c}.xyz : 1;
                      """);
        return true;
    }

    void IGraphNodeOp.AppendShaderResources(ref List<ShaderGraphNode.SrvBufferReference> list)
    {
        // Shader assembly runs on the render thread while a bake may be finishing on
        // another one. The lock prevents handing out an SRV that is disposed a moment
        // later by DisposeVolumes (a disposed view in a draw call is a hard crash).
        lock (_stateLock)
        {
            if (_sdf?.Srv == null || _sdf.Srv.IsDisposed)
                return;

            foreach (var x in list)
            {
                if (x.Srv == _sdf.Srv)
                    return;
            }

            list.Add(new ShaderGraphNode.SrvBufferReference($"Texture3D<float> {ShaderNode}SdfVolume", _sdf.Srv));
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        lock (_stateLock)
        {
            _bakeSteps = null;
            DisposeVolumes();
            Utilities.Dispose(ref _boundsUav);
            Utilities.Dispose(ref _boundsBuffer);
            Utilities.Dispose(ref _boundsStaging);
            Utilities.Dispose(ref _paramsBuffer);
        }
    }

    #region Implementation of IStatusProvider
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        _bakeSteps != null ? IStatusProvider.StatusLevel.Tip
        : string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success
        : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() =>
        _bakeSteps != null ? $"Baking volume... {_bakeProgress:P0}" : _lastErrorMessage;

    /** Polled from the UI thread without the lock - volatile so the latest value is visible. */
    private volatile string _lastErrorMessage = string.Empty;
    #endregion

    private const string BakeShaderPath = "Lib:shaders/cs/mesh-to-sdf-bake.hlsl";
    private const int InsideVoteThreshold = 3;
    private const int MaxSamplesPerTriangle = 8192;

    /**
     * D3D11 caps each Dispatch dimension at 65535 thread groups. The most parallel
     * kernels use 64 threads per group (scatter) and 128 (voxelize), so 64 * 65535 is
     * the face count that can still be dispatched as a single one-dimensional grid.
     */
    private const int MaxFaces = 64 * 65535;

    // Far-field distance in stored (volume-normalized) units for cells the scatter never
    // reaches: the exact volume-space diagonal. A huge value here (e.g. 10) turns into
    // giant raymarch steps that overshoot the whole volume from outside, so rays skip
    // the mesh entirely; the diagonal is the largest distance the sampler can honestly
    // report and keeps marching stable from any camera distance.
    private const float Sqrt3 = 1.7320508f;

    /** Serializes all shared state and GPU work of this operator (see class summary). */
    private readonly object _stateLock = new();

    // Shader resource registers of the bake compute shader
    private const int TexVoxelRead = 2;
    private const int TexInsideMaskRead = 3;
    private const int TexScratchRead = 4;
    private const int TexSdfRead = 5;
    private const int UavVoxelWrite = 0;
    private const int UavInsideMask = 1;
    private const int UavSdfWrite = 2;
    private const int UavScratch = 3;
    private const int UavBounds = 4;

    private VolumeTexture? _voxelsA;
    private VolumeTexture? _voxelsB;
    private VolumeTexture? _insideMask;
    private VolumeTexture? _sdf;
    private VolumeTexture? _bakeSdf;   // scratch target: the running bake writes here, the result is copied into _sdf on completion
    private VolumeTexture? _sdfScratch;
    private VolumeTexture? _rawSdf;
    private Texture3dWithViews? _sdfViews;
    private Int3 _dims;

    // GPU mesh-bounds reduction (auto-fit) with its staging readback buffer
    private Buffer? _boundsBuffer;
    private Buffer? _boundsStaging;
    private UnorderedAccessView? _boundsUav;

    // Bake work units, spread over frames by ProcessBakeSteps (null while idle).
    // Volatile because the status bar polls it from the UI thread without taking
    // _stateLock - it must see the latest reference, but it must never block the UI
    // behind a running GPU step.
    private volatile Queue<Action>? _bakeSteps;
    private int _bakeStepsTotal;
    private int _stepsPerFrame;
    private ShaderResourceView? _bakeMeshVertexSrv;
    private ShaderResourceView? _bakeMeshIndexSrv;
    private float _bakeProgress = 1f;

    private int _lastUpdateFrame = -1;
    private bool _bakeRequested;
    private bool _needsFirstBake = true;
    private bool _bakeFailed;
    /** Set when AutoRebake observes a mesh change while a bake is still in flight. */
    private bool _meshChangedDuringBake;
    private float _bakeCount;
    private string? _lastExpensiveWarnKey;
    private string? _lastFailureWarnKey;

    private int _lastResolution = -1;
    private Vector3 _lastCenter = new(float.NaN, float.NaN, float.NaN);
    private Vector3 _lastSize = new(float.NaN, float.NaN, float.NaN);
    private float _lastSampleDensity = -1;
    private ConversionModes _lastMode = (ConversionModes)(-1);
    private bool _lastInvert;
    private bool _lastAutoFit = true;
    private float _lastSmoothing = -1;
    private float _lastSmoothingRadius = -1;
    private bool _lastBakeSucceeded;

    private BakeParams _params;
    private Buffer? _paramsBuffer;
    private const int ParamsSizeInBytes = 80; // 16-byte aligned cbuffer

    [StructLayout(LayoutKind.Sequential)]
    private struct BoundsData
    {
        public Vector3 Min;
        public Vector3 Max;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BakeParams
    {
        public Int3 VoxelSize;
        public uint KernelParam;
        public Vector3 VoxelOrigin;
        public float VoxelScale;
        public float SampleDensity;
        public float DistanceNormalization;
        public int InsideVoteThreshold;
        public int MaxSamples;
        public float ClearValue;
        public float Signed;
        public float SliceZ;
        public float Smoothing;
        public float SmoothingRadius;
        public float Invert;
        /** First row of the z slice handled by this ClosestPointBrute dispatch. */
        public float BandRowStart;
        public float Padding5;
    }

    [Input(Guid = "a55ebcaa-0809-4299-8d08-bf1781aaf10d")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "d66b7b80-096b-41e0-967d-bc7a0fbcf3d8")]
    public readonly InputSlot<float> Resolution = new();

    [GraphParam]
    [Input(Guid = "7ddb3297-50df-4a92-8f8a-6823b306a305")]
    public readonly InputSlot<Vector3> Center = new();

    [GraphParam]
    [Input(Guid = "61089b3b-8f93-4f09-9937-aa6505c6e1f4")]
    public readonly InputSlot<Vector3> Size = new();

    /** When enabled (default) each bake fits the volume box to the mesh bounding box, ignoring Center/Size. */
    [Input(Guid = "2778c24f-99fc-45b8-b0af-ca657e99cdb2")]
    public readonly InputSlot<bool> AutoFitVolume = new(true);

    [Input(Guid = "514e150f-ac58-4a7e-9377-9ea2d1067c9c")]
    public readonly InputSlot<float> SampleDensity = new();

    [Input(Guid = "6f409060-a626-4cdd-b02d-16634ee6e4e5", MappedType = typeof(ConversionModes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "115ac2e8-3157-49e4-b544-14a885bbe8b0")]
    public readonly InputSlot<float> Smoothing = new();

    [Input(Guid = "4c385ad2-9248-4b53-bdfe-8965aeacc770")]
    public readonly InputSlot<float> SmoothingRadius = new();

    [Input(Guid = "b9c90672-04e7-4a87-8dcd-41d9d9b3eecb")]
    public readonly InputSlot<float> BakeFrames = new();

    [Input(Guid = "676f5929-9077-4a0c-b532-c39deaf5ed3e")]
    public readonly InputSlot<bool> Invert = new();

    [GraphParam]
    [Input(Guid = "2707d838-e482-41c5-a982-088309551c22")]
    public readonly InputSlot<float> Offset = new();

    [Input(Guid = "4c53055d-1dae-4328-b097-5c629a3bdb49")]
    public readonly InputSlot<bool> Bake = new();

    [Input(Guid = "2792f002-4d4e-4a18-b68d-36d6c01352e5")]
    public readonly InputSlot<bool> AutoRebake = new();

    private readonly Resource<ComputeShader> _clearVoxelsShader;
    private readonly Resource<ComputeShader> _clearSdfShader;
    private readonly Resource<ComputeShader> _meshToVoxelShader;
    private readonly Resource<ComputeShader> _classifyInsideXShader;
    private readonly Resource<ComputeShader> _classifyInsideYShader;
    private readonly Resource<ComputeShader> _classifyInsideZShader;
    private readonly Resource<ComputeShader> _preprocessShader;
    private readonly Resource<ComputeShader> _jfaShader;
    private readonly Resource<ComputeShader> _postprocessShader;
    private readonly Resource<ComputeShader> _closestPointShader;
    private readonly Resource<ComputeShader> _applySignShader;
    private readonly Resource<ComputeShader> _initScatterShader;
    private readonly Resource<ComputeShader> _bakeScatterShader;
    private readonly Resource<ComputeShader> _finalizeScatterShader;
    private readonly Resource<ComputeShader> _smoothFirstShader;
    private readonly Resource<ComputeShader> _smoothPassShader;
    private readonly Resource<ComputeShader> _smoothCopyBackShader;
    private readonly Resource<ComputeShader> _computeBoundsShader;
}
