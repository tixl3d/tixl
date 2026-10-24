#nullable enable
using System.Runtime.InteropServices;
using T3.Graphics.Compat;
using T3.Core.Utils;
using Device = T3.Graphics.Compat.Device;

namespace Lib.render.shading;

[Guid("182bdf23-84d7-4ae4-b806-5f1a20229ce0")]
internal sealed class CubeMapToEquirectangular : Instance<CubeMapToEquirectangular>
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ConvertParams
    {
        public Vector2 Size;
        public float Orientation;
        public float Lod;

        public int EquiAngular;
        private Vector3 _padding;
    }

    [Output(Guid = "43584ce0-a2c1-490d-8822-cf8d2fded3ce")]
    public readonly Slot<Texture2D?> Texture = new();

    public CubeMapToEquirectangular()
    {
        Texture.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var cubeMap = CubeMap.GetValue(context);
        var resolution = Resolution.GetValue(context);
        var orientation = Orientation.GetValue(context);
        var generateMips = GenerateMips.GetValue(context);

        if (cubeMap == null || cubeMap.IsDisposed || (cubeMap.Description.OptionFlags & ResourceOptionFlags.TextureCube) == 0)
        {
            Log.Warning("Requires a cube map, e.g. from [RenderToCubeMap] or [TextureToCubeMap]", this);
            Texture.Value = null;
            return;
        }

        // An equirectangular image needs four times the face width around and two times down.
        var faceSize = cubeMap.Description.Width;
        if (resolution.Width <= 0 || resolution.Height <= 0)
            resolution = new Int2(faceSize * 4, faceSize * 2);

        if (resolution.Width > Resource.MaximumTexture2DSize || resolution.Height > Resource.MaximumTexture2DSize)
        {
            Log.Warning($"Ignoring invalid size {resolution}", this);
            return;
        }

        // Created on first use with this instance as owner: its packages are what resolve the shader's includes.
        _shaderResource ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, this, () => "main");
        var shader = _shaderResource.Value;
        if (shader == null)
        {
            Log.Warning("Failed to load conversion shader", this);
            return;
        }

        var device = ResourceManager.Device;
        if (!UpdateTexture(device, resolution, generateMips))
        {
            Texture.Value = null;
            return;
        }

        // Pick the cube's mip whose texels match the output's, so a small image does not alias.
        var lod = MathF.Log2(2f * faceSize / resolution.Height).Clamp(0, cubeMap.Description.MipLevels - 1);
        var parameters = new ConvertParams
                             {
                                 Size = new Vector2(resolution.Width, resolution.Height),
                                 Orientation = orientation,
                                 Lod = lod,
                                 EquiAngular = cubeMap.CubeMapLayout == Texture2D.CubeMapLayouts.EquiAngular ? 1 : 0,
                             };
        if (_paramsBuffer == null)
        {
            ResourceManager.SetupConstBuffer(parameters, ref _paramsBuffer);
        }
        else
        {
            ResourceManager.UpdateConstBuffer(parameters, _paramsBuffer);
        }

        var deviceContext = device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;
        deviceContext.PushState(StateGroups.ComputeShader);

        csStage.Set(shader);
        csStage.SetConstantBuffer(0, _paramsBuffer);
        csStage.SetShaderResource(0, SrvManager.GetSrvForTexture(cubeMap));
        csStage.SetSampler(0, ResourceManager.DefaultSamplerState);
        csStage.SetUnorderedAccessView(0, _textureUav, 0);
        deviceContext.Dispatch((resolution.Width + ThreadGroupSize - 1) / ThreadGroupSize,
                               (resolution.Height + ThreadGroupSize - 1) / ThreadGroupSize, 1);

        deviceContext.PopState();

        if (generateMips)
            deviceContext.GenerateMips(_textureSrv!);

        Texture.Value = _texture;
    }

    private bool UpdateTexture(Device device, Int2 resolution, bool generateMips)
    {
        var mipLevels = generateMips ? (int)MathUtils.Log2(Math.Max(resolution.Width, resolution.Height)) + 1 : 1;
        var isValid = _texture is { IsDisposed: false }
                      && _texture.Description.Width == resolution.Width
                      && _texture.Description.Height == resolution.Height
                      && _texture.Description.MipLevels == mipLevels;
        if (isValid)
            return true;

        DisposeTexture();
        try
        {
            _texture = Texture2D.CreateTexture2D(new Texture2DDescription
                                                     {
                                                         ArraySize = 1,
                                                         BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess | BindFlags.RenderTarget,
                                                         CpuAccessFlags = CpuAccessFlags.None,
                                                         Format = Format.R16G16B16A16_Float,
                                                         Width = resolution.Width,
                                                         Height = resolution.Height,
                                                         MipLevels = mipLevels,
                                                         OptionFlags = generateMips ? ResourceOptionFlags.GenerateMipMaps : ResourceOptionFlags.None,
                                                         SampleDescription = new SampleDescription(1, 0),
                                                         Usage = ResourceUsage.Default,
                                                     });
            _textureSrv = new ShaderResourceView(device, _texture);
            _textureUav = new UnorderedAccessView(device, _texture);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create texture: {e.Message}", this);
            DisposeTexture();
            return false;
        }

        return true;
    }

    private void DisposeTexture()
    {
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _textureUav);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _textureSrv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _texture);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        DisposeTexture();
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _paramsBuffer);
    }

    [Input(Guid = "2f5ca948-6e1f-4189-a98b-f8baa676d6da")]
    public readonly InputSlot<Texture2D> CubeMap = new();

    [Input(Guid = "cbb5fa2a-cc44-43d9-935f-4a0de771ebb3")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "56b58333-61cb-44d3-a99d-3c6973f9cb01")]
    public readonly InputSlot<float> Orientation = new();

    [Input(Guid = "0e30f49d-260a-4dac-aa2e-e7e05f0090c7")]
    public readonly InputSlot<bool> GenerateMips = new();

    private const int ThreadGroupSize = 8;
    private const string ShaderPath = "Lib:shaders/3d/rendering/CubeMapToEquirectangular-cs.hlsl";

    private Resource<ComputeShader>? _shaderResource;
    private Buffer? _paramsBuffer;
    private Texture2D? _texture;
    private ShaderResourceView? _textureSrv;
    private UnorderedAccessView? _textureUav;
}
