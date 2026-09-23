#nullable enable
using System.Runtime.InteropServices;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using T3.Core.DataTypes;
using T3.Core.Output;
using T3.Core.Rendering;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.render.output;

/// <summary>
/// Paints an output's canvas from the room: the stage mesh is rasterized where each surface lands on the canvas,
/// and every pixel looks from the viewer through its point on the wall into an environment — an equirectangular
/// image or a cube map. One scene, rendered once, lands continuous across every wall the way a spectator at
/// the viewer's position would see it. Wire: [StageGeometry] → [GeometryToMesh] → this → [SendToOutput].
/// </summary>
[Guid("533eb7f8-f18d-41bf-a094-167e0557eae8")]
internal sealed class DrawStageCanvas : Instance<DrawStageCanvas>, IStatusProvider
{
    [Output(Guid = "6befcc3f-1d7d-4d94-85da-633723eb3d79")]
    public readonly Slot<Texture2D?> Output = new();

    public DrawStageCanvas()
    {
        Output.UpdateAction = Update;
        _vertexShader = ResourceManager.CreateShaderResource<VertexShader>(ShaderPath, this, () => "vsMain");
        _pixelShader = ResourceManager.CreateShaderResource<PixelShader>(ShaderPath, this, () => "psMain");
    }

    private void Update(EvaluationContext context)
    {
        var mesh = Mesh.GetValue(context);
        var image = Image.GetValue(context);
        var cubeMap = CubeMap.GetValue(context);
        var viewer = ViewerPosition.GetValue(context);
        var resolution = Resolution.GetValue(context);
        var color = Color.GetValue(context);
        var rotateY = RotateY.GetValue(context);
        var flip = FlipHorizontal.GetValue(context);

        var vs = _vertexShader.GetValue(context);
        var ps = _pixelShader.GetValue(context);
        if (vs == null || ps == null)
        {
            _lastError = "Shader failed to compile";
            Output.Value = null;
            return;
        }

        if (mesh?.VertexBuffer?.Srv == null || mesh.IndicesBuffer?.Srv == null || mesh.FaceCount == 0)
        {
            _lastError = "No stage mesh — connect [StageGeometry] through [GeometryToMesh]";
            Output.Value = null;
            return;
        }

        var useCubeMap = cubeMap is { IsDisposed: false } && (cubeMap.Description.OptionFlags & ResourceOptionFlags.TextureCube) != 0;
        if (!useCubeMap && image is not { IsDisposed: false })
        {
            _lastError = "No environment — connect an equirectangular Image or a CubeMap";
            Output.Value = null;
            return;
        }

        // The canvas size: as set, else the referenced output's, else the default output size.
        if (resolution.Width <= 0 || resolution.Height <= 0)
        {
            var output = ActiveSetup.FindOutput(OutputRef.GetValue(context));
            resolution = output?.ResolvedResolution ?? new T3.Core.DataTypes.Vector.Int2(1920, 1080);
        }

        resolution = new T3.Core.DataTypes.Vector.Int2(Math.Clamp(resolution.Width, 1, 16384), Math.Clamp(resolution.Height, 1, 16384));
        EnsureTarget(resolution);
        _lastError = null;

        var parameters = new Parameters
                             {
                                 ViewerPosition = viewer,
                                 UseCubeMap = useCubeMap ? 1 : 0,
                                 Color = color,
                                 RotateY = rotateY,
                                 FlipHorizontal = flip ? 1 : 0,
                             };
        ResourceManager.SetupConstBuffer(parameters, ref _parameterBuffer);

        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;

        // Everything touched is put back afterwards: this op renders on the side, inside whatever draw the graph is in.
        var prevTargets = deviceContext.OutputMerger.GetRenderTargets(1, out var prevDepth);
        var prevViewports = deviceContext.Rasterizer.GetViewports<RawViewportF>();
        var prevTopology = deviceContext.InputAssembler.PrimitiveTopology;
        var prevRasterizer = deviceContext.Rasterizer.State;
        var prevBlend = deviceContext.OutputMerger.BlendState;
        var prevDepthState = deviceContext.OutputMerger.DepthStencilState;
        var prevVs = deviceContext.VertexShader.Get();
        var prevPs = deviceContext.PixelShader.Get();
        var prevGs = deviceContext.GeometryShader.Get();

        deviceContext.OutputMerger.SetTargets((DepthStencilView?)null, _targetView);
        deviceContext.ClearRenderTargetView(_targetView, new SharpDX.Color4(0, 0, 0, 0));
        deviceContext.Rasterizer.SetViewport(0, 0, resolution.Width, resolution.Height);
        // On the canvas a face's winding says nothing: a turned or mirrored patch flips it, and so does the floor.
        _cullNoneState ??= new RasterizerState(device, new RasterizerStateDescription
                                                           {
                                                               FillMode = FillMode.Solid,
                                                               CullMode = CullMode.None,
                                                               IsDepthClipEnabled = false,
                                                           });
        deviceContext.Rasterizer.State = _cullNoneState;
        deviceContext.OutputMerger.BlendState = DefaultRenderingStates.DisabledBlendState;
        deviceContext.OutputMerger.DepthStencilState = DefaultRenderingStates.DisabledDepthStencilState;
        deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        deviceContext.InputAssembler.InputLayout = null;

        _resources[0] = mesh.VertexBuffer.Srv;
        _resources[1] = mesh.IndicesBuffer.Srv;
        _resources[2] = image is { IsDisposed: false } ? SrvManager.GetSrvForTexture(image) : null;
        _resources[3] = useCubeMap ? SrvManager.GetSrvForTexture(cubeMap!) : null;
        _samplers[0] = DefaultRenderingStates.DefaultSamplerState;
        _constantBuffers[0] = _parameterBuffer;

        deviceContext.VertexShader.Set(vs);
        deviceContext.VertexShader.SetShaderResources(0, 2, _resources);
        deviceContext.VertexShader.SetConstantBuffers(0, 1, _constantBuffers);
        deviceContext.GeometryShader.Set(null);
        deviceContext.PixelShader.Set(ps);
        deviceContext.PixelShader.SetShaderResources(0, 4, _resources);
        deviceContext.PixelShader.SetSamplers(0, 1, _samplers);
        deviceContext.PixelShader.SetConstantBuffers(0, 1, _constantBuffers);

        deviceContext.Draw(mesh.FaceCount * 3, 0);

        // Unbind what this draw bound, so the mesh buffers aren't left as inputs while something writes them.
        _resources[0] = _resources[1] = _resources[2] = _resources[3] = null;
        deviceContext.VertexShader.SetShaderResources(0, 2, _resources);
        deviceContext.PixelShader.SetShaderResources(0, 4, _resources);

        deviceContext.VertexShader.Set(prevVs);
        deviceContext.PixelShader.Set(prevPs);
        deviceContext.GeometryShader.Set(prevGs);
        deviceContext.InputAssembler.PrimitiveTopology = prevTopology;
        deviceContext.Rasterizer.State = prevRasterizer;
        deviceContext.OutputMerger.BlendState = prevBlend;
        deviceContext.OutputMerger.DepthStencilState = prevDepthState;
        deviceContext.OutputMerger.SetRenderTargets(prevDepth, prevTargets);
        if (prevViewports.Length > 0)
            deviceContext.Rasterizer.SetViewports(prevViewports, prevViewports.Length);

        // Getters hand out references that must be released; the stage objects themselves live on.
        Utilities.Dispose(ref prevVs);
        Utilities.Dispose(ref prevPs);
        Utilities.Dispose(ref prevGs);
        Utilities.Dispose(ref prevDepth);
        for (var i = 0; i < prevTargets.Length; i++)
            prevTargets[i]?.Dispose();

        Output.Value = _target;
    }

    /** The canvas texture at the wanted size, recreated only when the size changes. */
    private void EnsureTarget(T3.Core.DataTypes.Vector.Int2 size)
    {
        if (_target != null && !_target.IsDisposed && _target.Description.Width == size.Width && _target.Description.Height == size.Height)
            return;

        Utilities.Dispose(ref _targetView);
        Utilities.Dispose(ref _target);
        _target = Texture2D.CreateTexture2D(new Texture2DDescription
                                               {
                                                   ArraySize = 1,
                                                   BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                                                   CpuAccessFlags = CpuAccessFlags.None,
                                                   Format = Format.R16G16B16A16_Float,
                                                   Width = size.Width,
                                                   Height = size.Height,
                                                   MipLevels = 1,
                                                   OptionFlags = ResourceOptionFlags.None,
                                                   SampleDescription = new SampleDescription(1, 0),
                                                   Usage = ResourceUsage.Default,
                                               });
        _targetView = new RenderTargetView(ResourceManager.Device, _target);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        Utilities.Dispose(ref _targetView);
        Utilities.Dispose(ref _target);
        Utilities.Dispose(ref _parameterBuffer);
        Utilities.Dispose(ref _cullNoneState);
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        string.IsNullOrEmpty(_lastError) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string? IStatusProvider.GetStatusMessage() => _lastError;

    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private struct Parameters
    {
        public Vector3 ViewerPosition;
        public float UseCubeMap;
        public Vector4 Color;
        public float RotateY;
        public float FlipHorizontal;
        public Vector2 Padding;
    }

    private const string ShaderPath = "Lib:shaders/3d/mesh/mesh-DrawStageCanvas.hlsl";

    private readonly Resource<VertexShader> _vertexShader;
    private readonly Resource<PixelShader> _pixelShader;
    private Texture2D? _target;
    private RenderTargetView? _targetView;
    private SharpDX.Direct3D11.Buffer? _parameterBuffer;
    private RasterizerState? _cullNoneState;
    private string? _lastError;

    // Scratch arrays for the stage calls, reused every frame.
    private readonly ShaderResourceView?[] _resources = new ShaderResourceView?[4];
    private readonly SamplerState?[] _samplers = new SamplerState?[1];
    private readonly SharpDX.Direct3D11.Buffer?[] _constantBuffers = new SharpDX.Direct3D11.Buffer?[1];

    [Input(Guid = "ec895305-671f-4f1b-ada1-7ef91afc6212")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "d5dee94b-5dbc-448b-b8eb-c44eb6e36539")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "acd9fcfb-5693-49e5-890f-95ffce4d0d32")]
    public readonly InputSlot<Texture2D> CubeMap = new();

    [Input(Guid = "a17c5fd5-04f8-4ed1-bc89-c389d845f152")]
    public readonly InputSlot<Vector3> ViewerPosition = new();

    [Input(Guid = "379b6223-920b-448f-a508-c93dccb06316")]
    public readonly InputSlot<Guid> OutputRef = new();

    [Input(Guid = "2fb9a2cb-8583-49bc-9745-438a5d2eed63")]
    public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new();

    [Input(Guid = "8213f365-71e2-41dc-8e84-e3948f0ceea2")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "0f0b5a2e-4b9e-4f3a-9c2d-7a1e6d8c5b41")]
    public readonly InputSlot<float> RotateY = new();

    [Input(Guid = "3c7d9e11-2a6f-4d5b-8e4c-1f9a0b7c6d52")]
    public readonly InputSlot<bool> FlipHorizontal = new();
}
