#nullable enable
using T3.Graphics.Compat;
using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;
using Device = T3.Graphics.Compat.Device;

namespace Lib.render.shading;

[Guid("8f2cc172-318d-4b78-9db4-d3049240bec0")]
internal sealed class RenderToCubeMap : Instance<RenderToCubeMap>
{
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
        var size = Resolution.GetValue(context);
        var generateMips = GenerateMips.GetValue(context);
        var sampleCount = Multisampling.GetValue(context).Clamp(1, 8);
        var clearColor = ClearColor.GetValue(context);
        var position = Position.GetValue(context);
        var rotation = Rotation.GetValue(context);
        var clipPlanes = ClipPlanes.GetValue(context);

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

        var device = ResourceManager.Device;
        if (!UpdateTextures(device, size, format, generateMips, sampleCount))
        {
            CubeMap.Value = null;
            return;
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

        // Inner cameras would replace the face views.
        context.BypassCameras = true;
        context.CameraToClipSpace = GraphicsMath.PerspectiveFovRH(MathF.PI / 2, 1, near, far);
        context.RequestedResolution = new Int2(size, size);
        context.BackgroundColor = Vector4.One;
        context.ForegroundColor = Vector4.One;

        for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            // With MSAA every face is drawn into the one multisampled buffer and resolved into its slice right after.
            var faceRtv = sampleCount > 1 ? _multiSampledRtv : _faceRtvs[faceIndex];
            deviceContext.Rasterizer.SetViewport(new T3.Graphics.Viewport(0, 0, size, size, 0.0f, 1.0f));
            deviceContext.OutputMerger.SetTargets(_depthDsv, faceRtv);
            deviceContext.ClearRenderTargetView(faceRtv, clearColor);
            deviceContext.ClearDepthStencilView(_depthDsv, DepthStencilClearFlags.Depth, 1.0f, 0);

            context.ObjectToWorld = prevObjectToWorld;
            context.WorldToCamera = worldToCubeCenter * _cameraToFaceViews[faceIndex];
            context.IntVariables[FaceVariableName] = faceIndex;

            // Faces after the first repeat the same frame; simulations, dampers and audio reactions must not advance again.
            context.IntVariables[RepeatPassVariableName] = prevRepeatPass + faceIndex;

            // Without invalidation the subtree would return its cached result after the first face.
            DirtyFlag.GlobalInvalidationTick++;
            Command.InvalidateGraph();
            Command.GetValue(context);

            if (sampleCount > 1)
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

        CopyFacesIntoCubeMap(deviceContext, copyShader, size);

        if (generateMips)
            deviceContext.GenerateMips(_cubeMapSrv!);

        CubeMap.Value = _cubeMap;
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

    private bool UpdateTextures(Device device, int size, Format format, bool generateMips, int sampleCount)
    {
        var mipLevels = generateMips ? (int)MathUtils.Log2(size) + 1 : 1;

        var isValid = _cubeMap is { IsDisposed: false }
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
                                                       BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
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

    private void DisposeTextures()
    {
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMapUav);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMapSrv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMap);

        for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _faceRtvs[faceIndex]);
        }

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

    private const int FaceCount = 6;
    private const int CopyThreadGroupSize = 16;
    private const string FaceVariableName = "CubeFace";
    private const string RepeatPassVariableName = "__MotionBlurPass";
    private const string CopyShaderPath = "Lib:shaders/3d/rendering/CopyFacesToCubeMap-cs.hlsl";

    private static readonly Matrix4x4[] _cameraToFaceViews = CreateCameraToFaceViews();

    private readonly Resource<ComputeShader> _copyShaderResource;
    private readonly Resource<ComputeShader> _copyFxaaShaderResource;

    private Texture2D? _cubeMap;
    private ShaderResourceView? _cubeMapSrv;
    private UnorderedAccessView? _cubeMapUav;

    private Texture2D? _faces;
    private ShaderResourceView? _facesSrv;
    private readonly RenderTargetView?[] _faceRtvs = new RenderTargetView?[FaceCount];

    private Texture2D? _multiSampled;
    private RenderTargetView? _multiSampledRtv;

    private Texture2D? _depth;
    private DepthStencilView? _depthDsv;
}
