#nullable enable
using T3.Graphics.Compat;
using T3.Core.Rendering.Material;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;
using Device = T3.Graphics.Compat.Device;

namespace Lib.field.render._;

/// <summary>
/// Evaluates its command once per cube map face, with a camera whose rows are the face's right, down and forward
/// directions. A shader that builds its rays from that camera, like the raymarch cube template, fills the face
/// directly, so no projection or mirroring is involved.
/// </summary>
[Guid("a5fbe295-0e13-4589-9191-1304f33dc1ae")]
internal sealed class _RenderCubeMapFaces : Instance<_RenderCubeMapFaces>
{
    [Output(Guid = "25115e29-f1fd-49d6-ba89-4d4266fa5ff5")]
    public readonly Slot<Texture2D?> CubeMap = new();

    public _RenderCubeMapFaces()
    {
        CubeMap.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var size = Resolution.GetValue(context);
        var generateMips = GenerateMips.GetValue(context);
        var clearColor = ClearColor.GetValue(context);
        var position = Position.GetValue(context);
        var rotation = Rotation.GetValue(context);
        var material = Material.GetValue(context);
        var equiAngular = EquiAngular.GetValue(context);

        if (size <= 0 || size > Resource.MaximumTexture2DSize)
        {
            Log.Warning($"Ignoring invalid cube map size {size}", this);
            return;
        }

        var device = ResourceManager.Device;
        if (!UpdateTextures(device, size, generateMips))
        {
            CubeMap.Value = null;
            return;
        }

        var deviceContext = device.ImmediateContext;
        deviceContext.PushState(StateGroups.Rasterizer | StateGroups.OutputMerger);

        var prevWorldToCamera = context.WorldToCamera;
        var prevCameraToClipSpace = context.CameraToClipSpace;
        var prevRequestedResolution = context.RequestedResolution;
        var prevMaterial = context.PbrMaterial;
        var keepCameraBypass = context.BypassCameras;
        var hadRepeatPass = context.IntVariables.TryGetValue(RepeatPassVariableName, out var prevRepeatPass);

        var cubeToWorld = Matrix4x4.CreateFromYawPitchRoll(rotation.Y.ToRadians(), rotation.X.ToRadians(), rotation.Z.ToRadians())
                          * Matrix4x4.CreateTranslation(position);

        // Inner cameras would replace the face cameras.
        context.BypassCameras = true;
        context.CameraToClipSpace = GraphicsMath.PerspectiveFovRH(MathF.PI / 2, 1, 0.01f, 1000f);
        context.RequestedResolution = new Int2(size, size);
        if (material != null)
        {
            context.Materials.Add(material);
            context.PbrMaterial = material;
        }

        for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            var faceRtv = _faceRtvs[faceIndex];
            deviceContext.Rasterizer.SetViewport(new T3.Graphics.Viewport(0, 0, size, size, 0.0f, 1.0f));
            deviceContext.OutputMerger.SetTargets(faceRtv);
            deviceContext.ClearRenderTargetView(faceRtv, clearColor);

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

        if (generateMips)
            deviceContext.GenerateMips(_cubeMapSrv!);

        _cubeMap!.CubeMapLayout = equiAngular ? Texture2D.CubeMapLayouts.EquiAngular : Texture2D.CubeMapLayouts.Standard;
        CubeMap.Value = _cubeMap;
    }

    private bool UpdateTextures(Device device, int size, bool generateMips)
    {
        var mipLevels = generateMips ? (int)MathUtils.Log2(size) + 1 : 1;

        var isValid = _cubeMap is { IsDisposed: false }
                      && _cubeMap.Description.Width == size
                      && _cubeMap.Description.MipLevels == mipLevels;
        if (isValid)
            return true;

        DisposeTextures();
        try
        {
            _cubeMap = Texture2D.CreateTexture2D(new Texture2DDescription
                                                     {
                                                         ArraySize = FaceCount,
                                                         BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                                                         CpuAccessFlags = CpuAccessFlags.None,
                                                         Format = CubeMapFormat,
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
            for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
            {
                _faceRtvs[faceIndex] = new RenderTargetView(device, _cubeMap,
                                                            new RenderTargetViewDescription
                                                                {
                                                                    Format = CubeMapFormat,
                                                                    Dimension = RenderTargetViewDimension.Texture2DArray,
                                                                    Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource
                                                                                         {
                                                                                             MipSlice = 0,
                                                                                             FirstArraySlice = faceIndex,
                                                                                             ArraySize = 1,
                                                                                         },
                                                                });
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create cube map: {e.Message}", this);
            DisposeTextures();
            return false;
        }

        return true;
    }

    private void DisposeTextures()
    {
        for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _faceRtvs[faceIndex]);
        }

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMapSrv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMap);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        DisposeTextures();
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

    private const int FaceCount = 6;
    private const Format CubeMapFormat = Format.R16G16B16A16_Float;
    private const string RepeatPassVariableName = "__MotionBlurPass";

    private static readonly Matrix4x4[] _faceToCube =
        [
            CreateFaceToCube(-Vector3.UnitZ, -Vector3.UnitY, Vector3.UnitX),
            CreateFaceToCube(Vector3.UnitZ, -Vector3.UnitY, -Vector3.UnitX),
            CreateFaceToCube(Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            CreateFaceToCube(Vector3.UnitX, -Vector3.UnitZ, -Vector3.UnitY),
            CreateFaceToCube(Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ),
            CreateFaceToCube(-Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitZ),
        ];

    private Texture2D? _cubeMap;
    private ShaderResourceView? _cubeMapSrv;
    private readonly RenderTargetView?[] _faceRtvs = new RenderTargetView?[FaceCount];
}
