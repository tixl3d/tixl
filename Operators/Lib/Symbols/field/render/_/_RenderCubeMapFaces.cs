#nullable enable
using System.Runtime.InteropServices;
using T3.Core.Animation;
using T3.Graphics.Compat;
using T3.Core.Rendering.Material;
using T3.Core.Utils;
using Device = T3.Graphics.Compat.Device;

namespace Lib.field.render._;

/// <summary>
/// Evaluates its command once per cube map face, with a camera whose rows are the face's right, down and forward
/// directions. A shader that builds its rays from that camera, like the raymarch cube template, fills the face
/// directly, so no projection or mirroring is involved.
/// </summary>
/// <remarks>
/// With a tile size above one, each frame raymarches one texel per tile. The texel is picked in Bayer order, so
/// the samples complete ever finer grids, and once every texel has one, jittered samples keep refining it.
/// Samples accumulate in a history that is reprojected when the cube moves.
/// </remarks>
[Guid("a5fbe295-0e13-4589-9191-1304f33dc1ae")]
internal sealed class _RenderCubeMapFaces : Instance<_RenderCubeMapFaces>
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
        public int EquiAngular;
        public float MaxSamples;

        public float MaxSamplesWhileMoving;
        public float DisocclusionThreshold;
        public int LatticeSpacing;
        public float NeighborhoodClipping;

        public Vector4 MissColor;
    }

    [Output(Guid = "25115e29-f1fd-49d6-ba89-4d4266fa5ff5")]
    public readonly Slot<Texture2D?> CubeMap = new();

    public _RenderCubeMapFaces()
    {
        CubeMap.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // Several consumers in one frame must not advance the sampling sequence more than once.
        if (Playback.FrameCount == _lastUpdateFrame)
            return;

        _lastUpdateFrame = Playback.FrameCount;

        var size = Resolution.GetValue(context);
        var generateMips = GenerateMips.GetValue(context);
        var clearColor = ClearColor.GetValue(context);
        var position = Position.GetValue(context);
        var rotation = Rotation.GetValue(context);
        var material = Material.GetValue(context);
        var equiAngular = EquiAngular.GetValue(context);
        var reset = Reset.GetValue(context);
        var maxSamples = MaxSamples.GetValue(context).Clamp(1, 4096);
        var neighborhoodClipping = MathF.Max(0, NeighborhoodClipping.GetValue(context));

        if (size <= 0 || size > Resource.MaximumTexture2DSize)
        {
            Log.Warning($"Ignoring invalid cube map size {size}", this);
            return;
        }

        var tileSize = Math.Min(TileSize.GetValue(context).Clamp(1, MaxTileSize), size);
        tileSize = 1 << (int)MathF.Log2(tileSize);

        // Created on first use with this instance as owner: its packages are what resolve the shader's includes.
        _resolveShaderResource ??= ResourceManager.CreateShaderResource<ComputeShader>(AccumulateShaderPath, this, () => "resolveMain");
        _fillShaderResource ??= ResourceManager.CreateShaderResource<ComputeShader>(AccumulateShaderPath, this, () => "fillMain");
        var resolveShader = _resolveShaderResource.Value;
        var fillShader = _fillShaderResource.Value;
        if (resolveShader == null || fillShader == null)
        {
            Log.Warning("Failed to load cube map accumulation shaders", this);
            return;
        }

        var device = ResourceManager.Device;
        var texturesChanged = UpdateTextures(device, size, size / tileSize, generateMips, out var isValid);
        if (!isValid)
        {
            CubeMap.Value = null;
            return;
        }

        var cubeToWorld = Matrix4x4.CreateFromYawPitchRoll(rotation.Y.ToRadians(), rotation.X.ToRadians(), rotation.Z.ToRadians())
                          * Matrix4x4.CreateTranslation(position);

        var resetHistory = reset
                           || texturesChanged
                           || tileSize != _lastTileSize
                           || equiAngular != _lastEquiAngular
                           || material != _lastMaterial
                           || clearColor != _lastClearColor;
        if (resetHistory)
            _framesSinceReset = 0;

        _lastTileSize = tileSize;
        _lastEquiAngular = equiAngular;
        _lastMaterial = material;
        _lastClearColor = clearColor;

        var samplesPerTile = tileSize * tileSize;
        var sampleOffset = GetBayerOffset(tileSize, _framesSinceReset % samplesPerTile);
        var jitter = _framesSinceReset >= samplesPerTile
                         ? new Vector2(Halton(_framesSinceReset, 2), Halton(_framesSinceReset, 3)) - new Vector2(0.5f)
                         : Vector2.Zero;

        RenderSamples(context, size / tileSize, cubeToWorld, material, clearColor,
                      ((new Vector2(sampleOffset.X, sampleOffset.Y) + new Vector2(0.5f) + jitter) - new Vector2(tileSize * 0.5f)) * 2 / size);

        var completedSamplesPerTile = Math.Min(_framesSinceReset + 1, samplesPerTile);
        var completedLevels = (int)(MathF.Log2(completedSamplesPerTile) / 2 + 0.001f);

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
                                 EquiAngular = equiAngular ? 1 : 0,
                                 MaxSamples = maxSamples,
                                 MaxSamplesWhileMoving = MaxSamplesWhileMoving,
                                 DisocclusionThreshold = DisocclusionThreshold,
                                 LatticeSpacing = Math.Max(1, tileSize >> completedLevels),
                                 NeighborhoodClipping = neighborhoodClipping,
                                 MissColor = clearColor,
                             };
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
        var deviceContext = device.ImmediateContext;
        var groupCount = (size + ThreadGroupSize - 1) / ThreadGroupSize;

        Dispatch(deviceContext, resolveShader, groupCount,
                 _currentSrv, _historyColorSrvs[previous], _historyDataSrvs[previous],
                 _historyColorUavs[next], _historyDataUavs[next]);
        Dispatch(deviceContext, fillShader, groupCount,
                 _currentSrv, _historyColorSrvs[next], _historyDataSrvs[next],
                 _cubeMapUav, null);

        if (generateMips)
            deviceContext.GenerateMips(_cubeMapSrv!);

        _historyIndex = next;
        _prevCubeToWorld = cubeToWorld;
        _framesSinceReset++;

        _cubeMap!.CubeMapLayout = equiAngular ? Texture2D.CubeMapLayouts.EquiAngular : Texture2D.CubeMapLayouts.Standard;
        CubeMap.Value = _cubeMap;
    }

    private void RenderSamples(EvaluationContext context, int sampleSize, in Matrix4x4 cubeToWorld, PbrMaterial? material,
                               Vector4 clearColor, Vector2 sampleOffsetOnFace)
    {
        var deviceContext = ResourceManager.Device.ImmediateContext;
        deviceContext.PushState(StateGroups.Rasterizer | StateGroups.OutputMerger);

        var prevWorldToCamera = context.WorldToCamera;
        var prevCameraToClipSpace = context.CameraToClipSpace;
        var prevRequestedResolution = context.RequestedResolution;
        var prevMaterial = context.PbrMaterial;
        var keepCameraBypass = context.BypassCameras;
        var hadRepeatPass = context.IntVariables.TryGetValue(RepeatPassVariableName, out var prevRepeatPass);

        // Inner cameras would replace the face cameras.
        context.BypassCameras = true;

        // The template reads its face coordinates through the inverse, so this moves every ray to the texel
        // its tile samples this frame.
        context.CameraToClipSpace = Matrix4x4.CreateTranslation(-sampleOffsetOnFace.X, -sampleOffsetOnFace.Y, 0);
        context.RequestedResolution = new Int2(sampleSize, sampleSize);
        if (material != null)
        {
            context.Materials.Add(material);
            context.PbrMaterial = material;
        }

        // Alpha carries the hit distance; misses keep zero.
        var clearSample = new Vector4(clearColor.X, clearColor.Y, clearColor.Z, 0);

        for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            // A successful UpdateTextures() guarantees all face views exist.
            var faceRtv = _currentRtvs[faceIndex]!;
            deviceContext.Rasterizer.SetViewport(new T3.Graphics.Viewport(0, 0, sampleSize, sampleSize, 0.0f, 1.0f));
            deviceContext.OutputMerger.SetTargets(faceRtv);
            deviceContext.ClearRenderTargetView(faceRtv, clearSample);

            Matrix4x4.Invert(_faceToCube[faceIndex] * cubeToWorld, out var worldToFace);
            context.WorldToCamera = worldToFace;

            // Faces after the first repeat the same frame; simulations, dampers and audio reactions must not advance again.
            context.IntVariables[RepeatPassVariableName] = prevRepeatPass + faceIndex;

            // Without invalidation the subtree would return its cached result after the first face.
            DirtyFlag.GlobalInvalidationTick++;
            Command.InvalidateGraph();
            Command.GetValue(context);
        }

        if (hadRepeatPass)
        {
            context.IntVariables[RepeatPassVariableName] = prevRepeatPass;
        }
        else
        {
            context.IntVariables.Remove(RepeatPassVariableName);
        }

        if (material != null)
        {
            context.Materials.RemoveAt(context.Materials.Count - 1);
        }

        context.PbrMaterial = prevMaterial;
        context.WorldToCamera = prevWorldToCamera;
        context.CameraToClipSpace = prevCameraToClipSpace;
        context.RequestedResolution = prevRequestedResolution;
        context.BypassCameras = keepCameraBypass;
        deviceContext.PopState();
    }

    private void Dispatch(DeviceContext deviceContext, ComputeShader shader, int groupCount,
                          ShaderResourceView? srv0, ShaderResourceView? srv1, ShaderResourceView? srv2,
                          UnorderedAccessView? uav0, UnorderedAccessView? uav1)
    {
        var csStage = deviceContext.ComputeShader;
        deviceContext.PushState(StateGroups.ComputeShader);

        csStage.Set(shader);
        csStage.SetConstantBuffer(0, _paramsBuffer);
        csStage.SetShaderResource(0, srv0);
        csStage.SetShaderResource(1, srv1);
        csStage.SetShaderResource(2, srv2);
        csStage.SetUnorderedAccessView(0, uav0, 0);
        if (uav1 != null)
            csStage.SetUnorderedAccessView(1, uav1, 0);

        deviceContext.Dispatch(groupCount, groupCount, FaceCount);
        deviceContext.PopState();
    }

    /** Returns true if the textures were recreated, which invalidates the history. */
    private bool UpdateTextures(Device device, int size, int sampleSize, bool generateMips, out bool isValid)
    {
        var mipLevels = generateMips ? (int)MathUtils.Log2(size) + 1 : 1;

        isValid = _cubeMap is { IsDisposed: false }
                  && _cubeMap.Description.Width == size
                  && _cubeMap.Description.MipLevels == mipLevels
                  && _current is { IsDisposed: false }
                  && _current.Description.Width == sampleSize;
        if (isValid)
            return false;

        DisposeTextures();
        try
        {
            _cubeMap = CreateFaces(size, ColorFormat, mipLevels,
                                   BindFlags.ShaderResource | BindFlags.UnorderedAccess | BindFlags.RenderTarget,
                                   generateMips
                                       ? ResourceOptionFlags.TextureCube | ResourceOptionFlags.GenerateMipMaps
                                       : ResourceOptionFlags.TextureCube);
            _cubeMapSrv = new ShaderResourceView(device, _cubeMap);
            _cubeMapUav = new UnorderedAccessView(device, _cubeMap);

            _current = CreateFaces(sampleSize, ColorFormat, 1, BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceOptionFlags.None);
            _currentSrv = new ShaderResourceView(device, _current);
            for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
            {
                _currentRtvs[faceIndex] = new RenderTargetView(device, _current,
                                                               new RenderTargetViewDescription
                                                                   {
                                                                       Format = ColorFormat,
                                                                       Dimension = RenderTargetViewDimension.Texture2DArray,
                                                                       Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource
                                                                                            {
                                                                                                MipSlice = 0,
                                                                                                FirstArraySlice = faceIndex,
                                                                                                ArraySize = 1,
                                                                                            },
                                                                   });
            }

            for (var historyIndex = 0; historyIndex < 2; historyIndex++)
            {
                var color = CreateFaces(size, ColorFormat, 1, BindFlags.ShaderResource | BindFlags.UnorderedAccess, ResourceOptionFlags.None);
                var data = CreateFaces(size, DataFormat, 1, BindFlags.ShaderResource | BindFlags.UnorderedAccess, ResourceOptionFlags.None);
                _historyColors[historyIndex] = color;
                _historyColorSrvs[historyIndex] = new ShaderResourceView(device, color);
                _historyColorUavs[historyIndex] = new UnorderedAccessView(device, color);
                _historyDatas[historyIndex] = data;
                _historyDataSrvs[historyIndex] = new ShaderResourceView(device, data);
                _historyDataUavs[historyIndex] = new UnorderedAccessView(device, data);
            }

            isValid = true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create cube map: {e.Message}", this);
            DisposeTextures();
        }

        return true;
    }

    private static Texture2D CreateFaces(int size, Format format, int mipLevels, BindFlags bindFlags, ResourceOptionFlags optionFlags)
    {
        return Texture2D.CreateTexture2D(new Texture2DDescription
                                             {
                                                 ArraySize = FaceCount,
                                                 BindFlags = bindFlags,
                                                 CpuAccessFlags = CpuAccessFlags.None,
                                                 Format = format,
                                                 Width = size,
                                                 Height = size,
                                                 MipLevels = mipLevels,
                                                 OptionFlags = optionFlags,
                                                 SampleDescription = new SampleDescription(1, 0),
                                                 Usage = ResourceUsage.Default,
                                             });
    }

    private void DisposeTextures()
    {
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMapUav);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMapSrv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMap);

        for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _currentRtvs[faceIndex]);
        }

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _currentSrv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _current);

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

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        DisposeTextures();
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

    /** Rows are the right, down and forward directions of each face, in cube map order +X, -X, +Y, -Y, +Z, -Z. */
    private static Matrix4x4 CreateFaceToCube(Vector3 right, Vector3 down, Vector3 forward)
    {
        return new Matrix4x4(right.X, right.Y, right.Z, 0,
                             down.X, down.Y, down.Z, 0,
                             forward.X, forward.Y, forward.Z, 0,
                             0, 0, 0, 1);
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

    private enum TileSizes
    {
        _1x1 = 1,
        _2x2 = 2,
        _4x4 = 4,
        _8x8 = 8,
    }

    [Input(Guid = "7c6b3fdf-7717-45e7-8439-a4a05a445552")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "a2f38092-e013-4512-8484-5f715ac87af3", MappedType = typeof(Resolutions))]
    public readonly InputSlot<int> Resolution = new();

    [Input(Guid = "9ed937df-8d59-48ab-9095-30459e30ce02")]
    public readonly InputSlot<Vector4> ClearColor = new();

    [Input(Guid = "a7de7c54-e061-468d-9fd1-285f80b22bc4")]
    public readonly InputSlot<bool> GenerateMips = new();

    [Input(Guid = "6f2476e9-ff1f-4ad4-ad48-0d36da22202b")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "77ecebcb-d4f1-40fd-ae5b-257a23f6f078")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "e4a5a468-faf6-4800-8f38-e0d83a4e5e75")]
    public readonly InputSlot<PbrMaterial> Material = new();

    [Input(Guid = "a7ddc817-541e-45bf-bbe2-830852143120")]
    public readonly InputSlot<bool> EquiAngular = new();

    [Input(Guid = "cd8e3e81-e4a8-41aa-a79f-e10f3496ad30", MappedType = typeof(TileSizes))]
    public readonly InputSlot<int> TileSize = new();

    [Input(Guid = "2aab8a5f-30eb-4746-9862-40e49d70171b")]
    public readonly InputSlot<int> MaxSamples = new();

    [Input(Guid = "585d34f9-d2aa-4498-a9ef-ae951c6756ec")]
    public readonly InputSlot<bool> Reset = new();

    [Input(Guid = "f9df273d-2a77-4ec7-8843-b9382d83cee0")]
    public readonly InputSlot<float> NeighborhoodClipping = new();

    private const int FaceCount = 6;
    private const int MaxTileSize = 8;
    private const int ThreadGroupSize = 8;
    private const Format ColorFormat = Format.R16G16B16A16_Float;
    private const Format DataFormat = Format.R32G32_Float;
    private const string RepeatPassVariableName = "__MotionBlurPass";
    private const string AccumulateShaderPath = "Lib:shaders/3d/rendering/RaymarchCubeMapAccumulate-cs.hlsl";

    /** Caps the history while the cube moves, so reprojection errors fade within a few samples. */
    private const float MaxSamplesWhileMoving = 8;

    /** Relative distance mismatch at which a reprojected texel counts as newly uncovered. */
    private const float DisocclusionThreshold = 0.05f;

    private static readonly Int2[] _bayerOffsets1 = CreateBayerOffsets(1);
    private static readonly Int2[] _bayerOffsets2 = CreateBayerOffsets(2);
    private static readonly Int2[] _bayerOffsets4 = CreateBayerOffsets(4);
    private static readonly Int2[] _bayerOffsets8 = CreateBayerOffsets(8);

    private static readonly Matrix4x4[] _faceToCube =
        [
            CreateFaceToCube(-Vector3.UnitZ, -Vector3.UnitY, Vector3.UnitX),
            CreateFaceToCube(Vector3.UnitZ, -Vector3.UnitY, -Vector3.UnitX),
            CreateFaceToCube(Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            CreateFaceToCube(Vector3.UnitX, -Vector3.UnitZ, -Vector3.UnitY),
            CreateFaceToCube(Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ),
            CreateFaceToCube(-Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitZ),
        ];

    private Resource<ComputeShader>? _resolveShaderResource;
    private Resource<ComputeShader>? _fillShaderResource;
    private Buffer? _paramsBuffer;

    private Texture2D? _cubeMap;
    private ShaderResourceView? _cubeMapSrv;
    private UnorderedAccessView? _cubeMapUav;

    private Texture2D? _current;
    private ShaderResourceView? _currentSrv;
    private readonly RenderTargetView?[] _currentRtvs = new RenderTargetView?[FaceCount];

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
    private bool _lastEquiAngular;
    private PbrMaterial? _lastMaterial;
    private Vector4 _lastClearColor;
}
