#nullable enable
using System.Runtime.InteropServices;
using T3.Core.Animation;
using T3.Graphics.Compat;
using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;
using Device = T3.Graphics.Compat.Device;

namespace Lib.render.shading;

/// <remarks>
/// With a tile size above one or more than one sample, the faces render progressively: each frame renders only one
/// texel per tile, picked in Bayer order so the samples complete ever finer grids, and once every texel has one,
/// jittered samples keep refining it. Samples accumulate in a history that is reprojected when the cube moves.
/// </remarks>
[Guid("8f2cc172-318d-4b78-9db4-d3049240bec0")]
internal sealed class RenderToCubeMap : Instance<RenderToCubeMap>
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AccumulateParams
    {
        public Matrix4x4 CubeToWorld;
        public Matrix4x4 WorldToPrevCube;

        public int Size;
        public int TileSize;
        public Int2 SampleOffset;

        public int ResetHistory;
        public int IsMoving;
        public float MaxSamples;
        public float MaxSamplesWhileMoving;

        public float DisocclusionThreshold;
        public int LatticeSpacing;
        public float NeighborhoodClipping;
        public float NearClip;

        public float FarClip;
        private Vector3 _padding;
    }

    [Output(Guid = "f511ab15-1641-44ef-ab2f-5a2f67a57478")]
    public readonly Slot<Texture2D?> CubeMap = new();

    public RenderToCubeMap()
    {
        CubeMap.UpdateAction += Update;
        _copyShaderResource = ResourceManager.CreateShaderResource<ComputeShader>(CopyShaderPath, null, () => "main");
        _copyFxaaShaderResource = ResourceManager.CreateShaderResource<ComputeShader>(CopyShaderPath, null, () => "mainFxaa");
    }

    private void Update(EvaluationContext context)
    {
        // Several consumers in one frame must not advance the sampling sequence more than once.
        if (Playback.FrameCount == _lastUpdateFrame)
            return;

        _lastUpdateFrame = Playback.FrameCount;

        var size = Resolution.GetValue(context);
        var generateMips = GenerateMips.GetValue(context);
        var sampleCount = Multisampling.GetValue(context).Clamp(1, 8);
        var clearColor = ClearColor.GetValue(context);
        var position = Position.GetValue(context);
        var rotation = Rotation.GetValue(context);
        var clipPlanes = ClipPlanes.GetValue(context);
        var maxSamples = MaxSamples.GetValue(context).Clamp(1, 4096);
        var reset = Reset.GetValue(context);
        var neighborhoodClipping = MathF.Max(0, NeighborhoodClipping.GetValue(context));

        var format = TextureFormat.GetValue(context);
        if (format == Format.Unknown)
        {
            Log.Warning("Texture format unknown is not supported. Falling back to default", this);
            format = TextureFormat.TypedDefaultValue.Value;
            TextureFormat.SetTypedInputValue(format);
        }

        if (size <= 0 || size > Resource.MaximumTexture2DSize)
        {
            Log.Warning($"Ignoring invalid cube map size {size}", this);
            return;
        }

        var postAntiAliasing = (PostAntiAliasingModes)PostAntiAliasing.GetValue(context);
        var copyShader = postAntiAliasing == PostAntiAliasingModes.Fxaa ? _copyFxaaShaderResource.Value : _copyShaderResource.Value;
        if (copyShader == null)
        {
            Log.Warning("Failed to load cube map copy shader", this);
            return;
        }

        var tileSize = Math.Min(TileSize.GetValue(context).Clamp(1, MaxTileSize), size);
        tileSize = 1 << (int)MathF.Log2(tileSize);
        var isProgressive = tileSize > 1 || maxSamples > 1;

        // Jittered accumulation already smooths edges, so the progressive faces skip multisampling.
        if (isProgressive)
            sampleCount = 1;

        var device = ResourceManager.Device;
        if (!UpdateTextures(device, size, format, generateMips, sampleCount, isProgressive))
        {
            CubeMap.Value = null;
            return;
        }

        var progressiveTexturesChanged = false;
        if (isProgressive)
        {
            _accumulateShaderResource ??= ResourceManager.CreateShaderResource<ComputeShader>(AccumulateShaderPath, this, () => "resolveMain");
            _fillShaderResource ??= ResourceManager.CreateShaderResource<ComputeShader>(AccumulateShaderPath, this, () => "fillMain");
            if (_accumulateShaderResource.Value == null || _fillShaderResource.Value == null)
            {
                Log.Warning("Failed to load cube map accumulation shaders", this);
                return;
            }

            if (!UpdateProgressiveTextures(device, size, size / tileSize, format, out progressiveTexturesChanged))
            {
                CubeMap.Value = null;
                return;
            }
        }
        else
        {
            DisposeProgressiveTextures();
        }

        var deviceContext = device.ImmediateContext;
        deviceContext.PushState(StateGroups.Rasterizer | StateGroups.OutputMerger);

        var prevObjectToWorld = context.ObjectToWorld;
        var prevWorldToCamera = context.WorldToCamera;
        var prevCameraToClipSpace = context.CameraToClipSpace;
        var prevRequestedResolution = context.RequestedResolution;
        var keepCameraBypass = context.BypassCameras;
        var keepBackgroundColor = context.BackgroundColor;
        var keepForegroundColor = context.ForegroundColor;
        var hadFaceVariable = context.IntVariables.TryGetValue(FaceVariableName, out var prevFace);
        var hadRepeatPass = context.IntVariables.TryGetValue(RepeatPassVariableName, out var prevRepeatPass);

        Matrix4x4 worldToCubeCenter;
        var near = clipPlanes.X;
        var far = clipPlanes.Y;
        if (TryGetCamera(context, out var camera))
        {
            worldToCubeCenter = camera.WorldToCamera;
            TryGetClipPlanes(camera.CameraToClipSpace, ref near, ref far);
        }
        else
        {
            var cubeToWorldRotation = Matrix4x4.CreateFromYawPitchRoll(rotation.Y.ToRadians(), rotation.X.ToRadians(), rotation.Z.ToRadians());
            worldToCubeCenter = Matrix4x4.CreateTranslation(-position) * Matrix4x4.Transpose(cubeToWorldRotation);
        }

        // Progressive frames render one texel per tile; the lens shift moves every pixel onto the texel its tile
        // samples this frame.
        var renderSize = size / tileSize;
        var samplesPerTile = tileSize * tileSize;
        var resetHistory = reset
                           || progressiveTexturesChanged
                           || tileSize != _lastTileSize
                           || clearColor != _lastClearColor;
        if (resetHistory)
            _framesSinceReset = 0;

        _lastTileSize = tileSize;
        _lastClearColor = clearColor;

        var sampleOffset = GetBayerOffset(tileSize, _framesSinceReset % samplesPerTile);
        var jitter = maxSamples > 1 && _framesSinceReset >= samplesPerTile
                         ? new Vector2(Halton(_framesSinceReset, 2), Halton(_framesSinceReset, 3)) - new Vector2(0.5f)
                         : Vector2.Zero;
        var faceToClipSpace = GraphicsMath.PerspectiveFovRH(MathF.PI / 2, 1, near, far);
        if (isProgressive)
        {
            var shiftInTexels = new Vector2(sampleOffset.X, sampleOffset.Y) + new Vector2(0.5f) + jitter - new Vector2(tileSize * 0.5f);
            faceToClipSpace.M31 = shiftInTexels.X * 2 / size;
            faceToClipSpace.M32 = -shiftInTexels.Y * 2 / size;
        }

        // Inner cameras would replace the face views.
        context.BypassCameras = true;
        context.CameraToClipSpace = faceToClipSpace;
        context.RequestedResolution = new Int2(renderSize, renderSize);
        context.BackgroundColor = Vector4.One;
        context.ForegroundColor = Vector4.One;

        for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            // With MSAA every face is drawn into the one multisampled buffer and resolved into its slice right after.
            // Progressive faces keep their depth, which the accumulation needs to reproject.
            // A successful UpdateTextures() guarantees these views exist.
            var faceRtv = (isProgressive ? _sampleRtvs[faceIndex]
                           : sampleCount > 1 ? _multiSampledRtv
                           : _faceRtvs[faceIndex])!;
            var faceDsv = (isProgressive ? _sampleDepthDsvs[faceIndex] : _depthDsv)!;
            deviceContext.Rasterizer.SetViewport(new T3.Graphics.Viewport(0, 0, renderSize, renderSize, 0.0f, 1.0f));
            deviceContext.OutputMerger.SetTargets(faceDsv, faceRtv);
            deviceContext.ClearRenderTargetView(faceRtv, clearColor);
            deviceContext.ClearDepthStencilView(faceDsv, DepthStencilClearFlags.Depth, 1.0f, 0);

            context.ObjectToWorld = prevObjectToWorld;
            context.WorldToCamera = worldToCubeCenter * _cameraToFaceViews[faceIndex];
            context.IntVariables[FaceVariableName] = faceIndex;

            // Faces after the first repeat the same frame; simulations, dampers and audio reactions must not advance again.
            context.IntVariables[RepeatPassVariableName] = prevRepeatPass + faceIndex;

            // Without invalidation the subtree would return its cached result after the first face.
            DirtyFlag.GlobalInvalidationTick++;
            Command.InvalidateGraph();
            Command.GetValue(context);

            if (!isProgressive && sampleCount > 1)
                deviceContext.ResolveSubresource(_multiSampled!, 0, _faces!, faceIndex, format);
        }

        if (hadFaceVariable)
        {
            context.IntVariables[FaceVariableName] = prevFace;
        }
        else
        {
            context.IntVariables.Remove(FaceVariableName);
        }

        if (hadRepeatPass)
        {
            context.IntVariables[RepeatPassVariableName] = prevRepeatPass;
        }
        else
        {
            context.IntVariables.Remove(RepeatPassVariableName);
        }

        context.ObjectToWorld = prevObjectToWorld;
        context.WorldToCamera = prevWorldToCamera;
        context.CameraToClipSpace = prevCameraToClipSpace;
        context.RequestedResolution = prevRequestedResolution;
        context.BypassCameras = keepCameraBypass;
        context.BackgroundColor = keepBackgroundColor;
        context.ForegroundColor = keepForegroundColor;
        deviceContext.PopState();

        if (isProgressive)
        {
            var completedSamplesPerTile = Math.Min(_framesSinceReset + 1, samplesPerTile);
            var completedLevels = (int)(MathF.Log2(completedSamplesPerTile) / 2 + 0.001f);
            Matrix4x4.Invert(worldToCubeCenter, out var cubeToWorld);
            Matrix4x4.Invert(_prevCubeToWorld, out var worldToPrevCube);
            var parameters = new AccumulateParams
                                 {
                                     CubeToWorld = Matrix4x4.Transpose(cubeToWorld),
                                     WorldToPrevCube = Matrix4x4.Transpose(worldToPrevCube),
                                     Size = size,
                                     TileSize = tileSize,
                                     SampleOffset = sampleOffset,
                                     ResetHistory = resetHistory ? 1 : 0,
                                     IsMoving = cubeToWorld != _prevCubeToWorld ? 1 : 0,
                                     MaxSamples = maxSamples,
                                     MaxSamplesWhileMoving = MaxSamplesWhileMoving,
                                     DisocclusionThreshold = DisocclusionThreshold,
                                     LatticeSpacing = Math.Max(1, tileSize >> completedLevels),
                                     NeighborhoodClipping = neighborhoodClipping,
                                     NearClip = near,
                                     FarClip = far,
                                 };
            Accumulate(deviceContext, parameters, size);

            _prevCubeToWorld = cubeToWorld;
            _framesSinceReset++;
        }

        CopyFacesIntoCubeMap(deviceContext, copyShader, size);

        if (generateMips)
            deviceContext.GenerateMips(_cubeMapSrv!);

        CubeMap.Value = _cubeMap;
    }

    /** Blends this frame's samples into the history and writes the filled faces for the copy into the cube map. */
    private void Accumulate(DeviceContext deviceContext, in AccumulateParams parameters, int size)
    {
        if (_paramsBuffer == null)
        {
            ResourceManager.SetupConstBuffer(parameters, ref _paramsBuffer);
        }
        else
        {
            ResourceManager.UpdateConstBuffer(parameters, _paramsBuffer);
        }

        var previous = _historyIndex;
        var next = 1 - _historyIndex;
        var groupCount = (size + AccumulateThreadGroupSize - 1) / AccumulateThreadGroupSize;

        Dispatch(deviceContext, _accumulateShaderResource!.Value!, groupCount,
                 _historyColorSrvs[previous], _historyDataSrvs[previous],
                 _historyColorUavs[next], _historyDataUavs[next]);
        Dispatch(deviceContext, _fillShaderResource!.Value!, groupCount,
                 _historyColorSrvs[next], _historyDataSrvs[next],
                 _facesUav, null);

        _historyIndex = next;
    }

    private void Dispatch(DeviceContext deviceContext, ComputeShader shader, int groupCount,
                          ShaderResourceView? sourceColor, ShaderResourceView? sourceData,
                          UnorderedAccessView? resultColor, UnorderedAccessView? resultData)
    {
        var csStage = deviceContext.ComputeShader;
        deviceContext.PushState(StateGroups.ComputeShader);

        csStage.Set(shader);
        csStage.SetConstantBuffer(0, _paramsBuffer);
        csStage.SetShaderResource(0, _samplesSrv);
        csStage.SetShaderResource(1, sourceColor);
        csStage.SetShaderResource(2, sourceData);
        csStage.SetShaderResource(3, _sampleDepthSrv);
        csStage.SetUnorderedAccessView(0, resultColor, 0);
        if (resultData != null)
            csStage.SetUnorderedAccessView(1, resultData, 0);

        deviceContext.Dispatch(groupCount, groupCount, FaceCount);
        deviceContext.PopState();
    }

    private void CopyFacesIntoCubeMap(DeviceContext deviceContext, ComputeShader copyShader, int size)
    {
        var csStage = deviceContext.ComputeShader;
        deviceContext.PushState(StateGroups.ComputeShader);

        csStage.Set(copyShader);
        csStage.SetShaderResource(0, _facesSrv);
        csStage.SetSampler(0, ResourceManager.DefaultSamplerState);
        csStage.SetUnorderedAccessView(0, _cubeMapUav, 0);
        var groupCount = (size + CopyThreadGroupSize - 1) / CopyThreadGroupSize;
        deviceContext.Dispatch(groupCount, groupCount, FaceCount);

        deviceContext.PopState();
    }

    private bool TryGetCamera(EvaluationContext context, [NotNullWhen(true)] out ICameraPropertiesProvider? camera)
    {
        camera = null;
        if (!CameraReference.HasInputConnections)
        {
            CameraReference.DirtyFlag.Clear();
            return false;
        }

        camera = CameraReference.GetValue(context) as ICameraPropertiesProvider;
        return camera != null;
    }

    /** Recovers near and far from a perspective projection; an orthographic one keeps the given planes. */
    private static void TryGetClipPlanes(in Matrix4x4 cameraToClipSpace, ref float near, ref float far)
    {
        var isPerspective = MathF.Abs(cameraToClipSpace.M34 + 1) < 0.0001f;
        if (!isPerspective || cameraToClipSpace.M33 == 0 || cameraToClipSpace.M33 == -1)
            return;

        var extractedNear = cameraToClipSpace.M43 / cameraToClipSpace.M33;
        var extractedFar = cameraToClipSpace.M43 / (cameraToClipSpace.M33 + 1);
        if (extractedNear <= 0 || extractedFar <= extractedNear || !float.IsFinite(extractedFar))
            return;

        near = extractedNear;
        far = extractedFar;
    }

    private bool UpdateTextures(Device device, int size, Format format, bool generateMips, int sampleCount, bool isProgressive)
    {
        var mipLevels = generateMips ? (int)MathUtils.Log2(size) + 1 : 1;

        var isValid = _cubeMap is { IsDisposed: false }
                      && (_facesUav != null) == isProgressive
                      && _cubeMap.Description.Width == size
                      && _cubeMap.Description.Format == format
                      && _cubeMap.Description.MipLevels == mipLevels
                      && (_multiSampled?.Description.SampleDescription.Count ?? 1) == sampleCount;
        if (isValid)
            return true;

        DisposeTextures();
        try
        {
            _cubeMap = Texture2D.CreateTexture2D(new Texture2DDescription
                                                     {
                                                         ArraySize = FaceCount,
                                                         BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess | BindFlags.RenderTarget,
                                                         CpuAccessFlags = CpuAccessFlags.None,
                                                         Format = format,
                                                         Width = size,
                                                         Height = size,
                                                         MipLevels = mipLevels,
                                                         OptionFlags = generateMips
                                                                           ? ResourceOptionFlags.TextureCube | ResourceOptionFlags.GenerateMipMaps
                                                                           : ResourceOptionFlags.TextureCube,
                                                         SampleDescription = new SampleDescription(1, 0),
                                                         Usage = ResourceUsage.Default,
                                                     });
            _cubeMapSrv = new ShaderResourceView(device, _cubeMap);
            _cubeMapUav = new UnorderedAccessView(device, _cubeMap);

            _faces = Texture2D.CreateTexture2D(new Texture2DDescription
                                                   {
                                                       ArraySize = FaceCount,
                                                       BindFlags = isProgressive
                                                                       ? BindFlags.RenderTarget | BindFlags.ShaderResource | BindFlags.UnorderedAccess
                                                                       : BindFlags.RenderTarget | BindFlags.ShaderResource,
                                                       CpuAccessFlags = CpuAccessFlags.None,
                                                       Format = format,
                                                       Width = size,
                                                       Height = size,
                                                       MipLevels = 1,
                                                       OptionFlags = ResourceOptionFlags.None,
                                                       SampleDescription = new SampleDescription(1, 0),
                                                       Usage = ResourceUsage.Default,
                                                   });
            _facesSrv = new ShaderResourceView(device, _faces);
            if (isProgressive)
                _facesUav = new UnorderedAccessView(device, _faces);

            for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
            {
                _faceRtvs[faceIndex] = new RenderTargetView(device, _faces,
                                                            new RenderTargetViewDescription
                                                                {
                                                                    Format = format,
                                                                    Dimension = RenderTargetViewDimension.Texture2DArray,
                                                                    Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource
                                                                                         {
                                                                                             MipSlice = 0,
                                                                                             FirstArraySlice = faceIndex,
                                                                                             ArraySize = 1,
                                                                                         },
                                                                });
            }

            if (sampleCount > 1)
            {
                _multiSampled = Texture2D.CreateTexture2D(new Texture2DDescription
                                                              {
                                                                  ArraySize = 1,
                                                                  BindFlags = BindFlags.RenderTarget,
                                                                  CpuAccessFlags = CpuAccessFlags.None,
                                                                  Format = format,
                                                                  Width = size,
                                                                  Height = size,
                                                                  MipLevels = 1,
                                                                  OptionFlags = ResourceOptionFlags.None,
                                                                  SampleDescription = new SampleDescription(sampleCount, 0),
                                                                  Usage = ResourceUsage.Default,
                                                              });
                _multiSampledRtv = new RenderTargetView(device, _multiSampled,
                                                        new RenderTargetViewDescription
                                                            {
                                                                Format = format,
                                                                Dimension = RenderTargetViewDimension.Texture2DMultisampled,
                                                            });
            }

            _depth = Texture2D.CreateTexture2D(new Texture2DDescription
                                                   {
                                                       ArraySize = 1,
                                                       BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
                                                       CpuAccessFlags = CpuAccessFlags.None,
                                                       Format = Format.R32_Typeless,
                                                       Width = size,
                                                       Height = size,
                                                       MipLevels = 1,
                                                       OptionFlags = ResourceOptionFlags.None,
                                                       SampleDescription = new SampleDescription(sampleCount, 0),
                                                       Usage = ResourceUsage.Default,
                                                   });
            _depthDsv = new DepthStencilView(device, _depth,
                                             new DepthStencilViewDescription
                                                 {
                                                     Format = Format.D32_Float,
                                                     Dimension = sampleCount > 1
                                                                     ? DepthStencilViewDimension.Texture2DMultisampled
                                                                     : DepthStencilViewDimension.Texture2D,
                                                 });
        }
        catch (Exception e)
        {
            // Formats without compute write support (e.g. sRGB) fail here.
            Log.Error($"Failed to create cube map with format {format}: {e.Message}", this);
            DisposeTextures();
            return false;
        }

        return true;
    }

    /** The low resolution samples with their depth, and the history they accumulate in. Returns false if they could not be created. */
    private bool UpdateProgressiveTextures(Device device, int size, int sampleSize, Format format, out bool wereRecreated)
    {
        wereRecreated = false;
        var isValid = _samples is { IsDisposed: false }
                      && _samples.Description.Width == sampleSize
                      && _samples.Description.Format == format
                      && _historyColors[0] is { IsDisposed: false }
                      && _historyColors[0]!.Description.Width == size;
        if (isValid)
            return true;

        wereRecreated = true;
        DisposeProgressiveTextures();
        try
        {
            _samples = CreateFaces(sampleSize, format, BindFlags.RenderTarget | BindFlags.ShaderResource);
            _samplesSrv = new ShaderResourceView(device, _samples);
            _sampleDepth = CreateFaces(sampleSize, Format.R32_Typeless, BindFlags.DepthStencil | BindFlags.ShaderResource);
            _sampleDepthSrv = new ShaderResourceView(device, _sampleDepth,
                                                     new ShaderResourceViewDescription
                                                         {
                                                             Format = Format.R32_Float,
                                                             Dimension = ShaderResourceViewDimension.Texture2DArray,
                                                             Texture2DArray = new ShaderResourceViewDescription.Texture2DArrayResource
                                                                                  {
                                                                                      MostDetailedMip = 0,
                                                                                      MipLevels = 1,
                                                                                      FirstArraySlice = 0,
                                                                                      ArraySize = FaceCount,
                                                                                  },
                                                         });

            for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
            {
                _sampleRtvs[faceIndex] = new RenderTargetView(device, _samples,
                                                              new RenderTargetViewDescription
                                                                  {
                                                                      Format = format,
                                                                      Dimension = RenderTargetViewDimension.Texture2DArray,
                                                                      Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource
                                                                                           {
                                                                                               MipSlice = 0,
                                                                                               FirstArraySlice = faceIndex,
                                                                                               ArraySize = 1,
                                                                                           },
                                                                  });
                _sampleDepthDsvs[faceIndex] = new DepthStencilView(device, _sampleDepth,
                                                                   new DepthStencilViewDescription
                                                                       {
                                                                           Format = Format.D32_Float,
                                                                           Dimension = DepthStencilViewDimension.Texture2DArray,
                                                                           Texture2DArray = new DepthStencilViewDescription.Texture2DArrayResource
                                                                                                {
                                                                                                    MipSlice = 0,
                                                                                                    FirstArraySlice = faceIndex,
                                                                                                    ArraySize = 1,
                                                                                                },
                                                                       });
            }

            for (var historyIndex = 0; historyIndex < 2; historyIndex++)
            {
                var color = CreateFaces(size, HistoryColorFormat, BindFlags.ShaderResource | BindFlags.UnorderedAccess);
                var data = CreateFaces(size, HistoryDataFormat, BindFlags.ShaderResource | BindFlags.UnorderedAccess);
                _historyColors[historyIndex] = color;
                _historyColorSrvs[historyIndex] = new ShaderResourceView(device, color);
                _historyColorUavs[historyIndex] = new UnorderedAccessView(device, color);
                _historyDatas[historyIndex] = data;
                _historyDataSrvs[historyIndex] = new ShaderResourceView(device, data);
                _historyDataUavs[historyIndex] = new UnorderedAccessView(device, data);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create progressive cube map buffers: {e.Message}", this);
            DisposeProgressiveTextures();
            return false;
        }

        return true;
    }

    private static Texture2D CreateFaces(int size, Format format, BindFlags bindFlags)
    {
        return Texture2D.CreateTexture2D(new Texture2DDescription
                                             {
                                                 ArraySize = FaceCount,
                                                 BindFlags = bindFlags,
                                                 CpuAccessFlags = CpuAccessFlags.None,
                                                 Format = format,
                                                 Width = size,
                                                 Height = size,
                                                 MipLevels = 1,
                                                 OptionFlags = ResourceOptionFlags.None,
                                                 SampleDescription = new SampleDescription(1, 0),
                                                 Usage = ResourceUsage.Default,
                                             });
    }

    private void DisposeProgressiveTextures()
    {
        for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _sampleRtvs[faceIndex]);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _sampleDepthDsvs[faceIndex]);
        }

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _samplesSrv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _samples);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _sampleDepthSrv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _sampleDepth);

        for (var historyIndex = 0; historyIndex < 2; historyIndex++)
        {
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _historyColorUavs[historyIndex]);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _historyColorSrvs[historyIndex]);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _historyColors[historyIndex]);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _historyDataUavs[historyIndex]);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _historyDataSrvs[historyIndex]);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _historyDatas[historyIndex]);
        }
    }

    private void DisposeTextures()
    {
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMapUav);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMapSrv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMap);

        for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _faceRtvs[faceIndex]);
        }

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _facesUav);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _facesSrv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _faces);

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledRtv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampled);

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _depthDsv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _depth);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        DisposeTextures();
        DisposeProgressiveTextures();
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _paramsBuffer);
    }

    private static Int2 GetBayerOffset(int tileSize, int sampleIndex)
    {
        var offsets = tileSize switch
                          {
                              1 => _bayerOffsets1,
                              2 => _bayerOffsets2,
                              4 => _bayerOffsets4,
                              _ => _bayerOffsets8,
                          };
        return offsets[sampleIndex];
    }

    /** Positions in a tile ordered so that the first 4^n of them form a regular grid. */
    private static Int2[] CreateBayerOffsets(int tileSize)
    {
        var offsets = new Int2[tileSize * tileSize];
        for (var y = 0; y < tileSize; y++)
        {
            for (var x = 0; x < tileSize; x++)
            {
                // The lowest coordinate bits become the most significant digits, so coarse grids come first.
                var index = 0;
                for (var bit = 1; bit < tileSize; bit <<= 1)
                {
                    var xBit = (x & bit) != 0 ? 1 : 0;
                    var yBit = (y & bit) != 0 ? 1 : 0;
                    index = index * 4 + (xBit ^ yBit) * 2 + yBit;
                }

                offsets[index] = new Int2(x, y);
            }
        }

        return offsets;
    }

    private static float Halton(int index, int radix)
    {
        var result = 0f;
        var fraction = 1f / radix;
        for (var i = index + 1; i > 0; i /= radix)
        {
            result += i % radix * fraction;
            fraction /= radix;
        }

        return result;
    }

    /** Camera-space rotations into the six faces, in cube map order +X, -X, +Y, -Y, +Z, -Z. */
    private static Matrix4x4[] CreateCameraToFaceViews()
    {
        return
            [
                GraphicsMath.LookAtRH(Vector3.Zero, Vector3.UnitX, Vector3.UnitY),
                GraphicsMath.LookAtRH(Vector3.Zero, -Vector3.UnitX, Vector3.UnitY),
                GraphicsMath.LookAtRH(Vector3.Zero, Vector3.UnitY, -Vector3.UnitZ),
                GraphicsMath.LookAtRH(Vector3.Zero, -Vector3.UnitY, Vector3.UnitZ),
                GraphicsMath.LookAtRH(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY),
                GraphicsMath.LookAtRH(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY),
            ];
    }

    private enum TileSizes
    {
        _1x1 = 1,
        _2x2 = 2,
        _4x4 = 4,
        _8x8 = 8,
    }

    private enum PostAntiAliasingModes
    {
        None = 0,
        Fxaa = 1,
    }

    private enum Samples
    {
        No_MSAA = 1,
        MSAA_2Samples = 2,
        MSAA_4Samples = 4,
        MSAA_8Samples = 8,
    }

    private enum Resolutions
    {
        _4 = 4,
        _16 = 16,
        _32 = 32,
        _64 = 64,
        _128 = 128,
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096
    }

    [Input(Guid = "f3ebee9c-250f-4e74-9773-ad3fcba4f43b")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "9e04c55a-daa7-4014-8347-70efb65bdc8f", MappedType = typeof(Resolutions))]
    public readonly InputSlot<int> Resolution = new();

    [Input(Guid = "7bc55c92-cd9b-4217-b958-9a06ec994364")]
    public readonly InputSlot<Vector4> ClearColor = new();

    [Input(Guid = "9199affe-5613-4cfd-a09e-0b79e6b108f0")]
    public readonly InputSlot<Format> TextureFormat = new();

    [Input(Guid = "cad58ac3-b764-4e46-8b85-c0b05598aab1")]
    public readonly InputSlot<bool> GenerateMips = new();

    [Input(Guid = "4b8e2f61-9c3a-4d7e-b5f0-2a6c8d1e3f97", MappedType = typeof(Samples))]
    public readonly InputSlot<int> Multisampling = new();

    [Input(Guid = "c27d5a94-1e8b-4f3c-a6d2-8b9e0f4a7c15", MappedType = typeof(PostAntiAliasingModes))]
    public readonly InputSlot<int> PostAntiAliasing = new();

    [Input(Guid = "5cf3ed9a-03d9-4443-aa8f-727d929af6c2")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "f52ba308-2ce7-4d50-a3ff-2df86160063a")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "d7181b91-ee60-4ac2-9e70-b99bc357b52b")]
    public readonly InputSlot<Vector2> ClipPlanes = new();

    [Input(Guid = "f292b2fe-c340-40a0-9fd3-d1e19702f0bb")]
    public readonly InputSlot<Object> CameraReference = new();

    [Input(Guid = "ec428f51-8929-495f-b548-946d4290e416", MappedType = typeof(TileSizes))]
    public readonly InputSlot<int> TileSize = new();

    [Input(Guid = "05e9a142-8821-4f0e-85f8-de3a6aa6a401")]
    public readonly InputSlot<int> MaxSamples = new();

    [Input(Guid = "75cbea04-c06b-4794-ad8d-a1426a351c3e")]
    public readonly InputSlot<float> NeighborhoodClipping = new();

    [Input(Guid = "0bebe9a7-ad9f-466a-899c-19e886c8940e")]
    public readonly InputSlot<bool> Reset = new();

    private const int FaceCount = 6;
    private const int CopyThreadGroupSize = 16;
    private const string FaceVariableName = "CubeFace";
    private const string RepeatPassVariableName = "__MotionBlurPass";
    private const string CopyShaderPath = "Lib:shaders/3d/rendering/CopyFacesToCubeMap-cs.hlsl";
    private const string AccumulateShaderPath = "Lib:shaders/3d/rendering/RenderCubeMapAccumulate-cs.hlsl";
    private const int MaxTileSize = 8;
    private const int AccumulateThreadGroupSize = 8;
    private const Format HistoryColorFormat = Format.R16G16B16A16_Float;
    private const Format HistoryDataFormat = Format.R32G32_Float;

    /** Caps the history while the cube moves, so reprojection errors fade within a few samples. */
    private const float MaxSamplesWhileMoving = 8;

    /** Relative distance mismatch at which a reprojected texel counts as newly uncovered. */
    private const float DisocclusionThreshold = 0.05f;

    private static readonly Int2[] _bayerOffsets1 = CreateBayerOffsets(1);
    private static readonly Int2[] _bayerOffsets2 = CreateBayerOffsets(2);
    private static readonly Int2[] _bayerOffsets4 = CreateBayerOffsets(4);
    private static readonly Int2[] _bayerOffsets8 = CreateBayerOffsets(8);

    private static readonly Matrix4x4[] _cameraToFaceViews = CreateCameraToFaceViews();

    private readonly Resource<ComputeShader> _copyShaderResource;
    private readonly Resource<ComputeShader> _copyFxaaShaderResource;

    private Texture2D? _cubeMap;
    private ShaderResourceView? _cubeMapSrv;
    private UnorderedAccessView? _cubeMapUav;

    private Texture2D? _faces;
    private ShaderResourceView? _facesSrv;
    private UnorderedAccessView? _facesUav;
    private readonly RenderTargetView?[] _faceRtvs = new RenderTargetView?[FaceCount];

    private Texture2D? _multiSampled;
    private RenderTargetView? _multiSampledRtv;

    private Texture2D? _depth;
    private DepthStencilView? _depthDsv;

    private Resource<ComputeShader>? _accumulateShaderResource;
    private Resource<ComputeShader>? _fillShaderResource;
    private Buffer? _paramsBuffer;

    private Texture2D? _samples;
    private ShaderResourceView? _samplesSrv;
    private readonly RenderTargetView?[] _sampleRtvs = new RenderTargetView?[FaceCount];
    private Texture2D? _sampleDepth;
    private ShaderResourceView? _sampleDepthSrv;
    private readonly DepthStencilView?[] _sampleDepthDsvs = new DepthStencilView?[FaceCount];

    private readonly Texture2D?[] _historyColors = new Texture2D?[2];
    private readonly ShaderResourceView?[] _historyColorSrvs = new ShaderResourceView?[2];
    private readonly UnorderedAccessView?[] _historyColorUavs = new UnorderedAccessView?[2];
    private readonly Texture2D?[] _historyDatas = new Texture2D?[2];
    private readonly ShaderResourceView?[] _historyDataSrvs = new ShaderResourceView?[2];
    private readonly UnorderedAccessView?[] _historyDataUavs = new UnorderedAccessView?[2];
    private int _historyIndex;

    private int _lastUpdateFrame = -1;
    private int _framesSinceReset;
    private Matrix4x4 _prevCubeToWorld;
    private int _lastTileSize;
    private Vector4 _lastClearColor;
}
