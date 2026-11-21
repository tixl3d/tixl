#nullable enable
using T3.Core.Resource.Assets;
using T3.Graphics.Compat;

namespace Lib.io.video.depthanything.Utils;

/// <summary>
/// One compute shader that fills a single output texture from up to two input textures, run inside the host's
/// frame. Depth post-processing stays on the GPU this way, so no frame waits for a CPU readback.
/// </summary>
internal sealed class ComputePass : IDisposable
{
    public ComputePass(string shaderAddress)
    {
        _shaderAddress = shaderAddress;
    }

    public Texture2D? Output { get; private set; }

    public string? Error { get; private set; }

    /// <summary>
    /// Runs the shader over a <paramref name="width"/> x <paramref name="height"/> output. Returns false, with
    /// <see cref="Error"/> set, when the shader or an input view is unavailable.
    /// </summary>
    public bool TryRun<TConstants>(in TConstants constants, Texture2D? input0, Texture2D? input1, int width, int height, Format outputFormat)
        where TConstants : unmanaged
    {
        Error = null;
        if (!TryLoadShader())
            return false;

        ShaderResourceView? view0 = null;
        if (input0 != null)
        {
            view0 = SrvManager.GetSrvForTexture(input0);
            if (view0 == null)
            {
                Error = "The input texture can't be read by a shader";
                return false;
            }
        }

        ShaderResourceView? view1 = null;
        if (input1 != null)
        {
            view1 = SrvManager.GetSrvForTexture(input1);
            if (view1 == null)
            {
                Error = "The input texture can't be read by a shader";
                return false;
            }
        }

        EnsureOutput(width, height, outputFormat);

        if (_constants == null)
        {
            ResourceManager.SetupConstBuffer(constants, ref _constants);
        }
        else
        {
            ResourceManager.UpdateConstBuffer(constants, _constants);
        }

        var context = ResourceManager.Device.ImmediateContext;
        var computeStage = context.ComputeShader;

        // Everything touched is put back: this runs inside whatever the host is rendering.
        context.PushState(StateGroups.ComputeShader | StateGroups.OutputMerger);

        // A view onto a resource that is still bound for writing reads black, so the host's render target goes first.
        context.OutputMerger.SetRenderTargets((DepthStencilView?)null, (RenderTargetView?)null);

        computeStage.Set(_shaderResource!.Value!);
        computeStage.SetConstantBuffer(0, _constants);
        computeStage.SetShaderResource(0, view0);
        computeStage.SetShaderResource(1, view1);
        computeStage.SetUnorderedAccessView(0, _outputView);
        context.Dispatch((width + GroupSize - 1) / GroupSize, (height + GroupSize - 1) / GroupSize, 1);

        context.PopState();
        return true;
    }

    public void Dispose()
    {
        _outputView?.Dispose();
        _outputView = null;
        Output?.Dispose();
        Output = null;
        _constants?.Dispose();
        _constants = null;
    }

    private bool TryLoadShader()
    {
        if (_shaderResource?.Value != null)
            return true;

        if (!AssetRegistry.TryResolveAddress(_shaderAddress, null, out var shaderPath, out _))
        {
            Error = $"Can't find {_shaderAddress}";
            return false;
        }

        _shaderResource = ResourceManager.CreateShaderResource<ComputeShader>(shaderPath, null, () => "main");
        if (_shaderResource.Value != null)
            return true;

        Error = $"{_shaderAddress} failed to compile";
        return false;
    }

    private void EnsureOutput(int width, int height, Format format)
    {
        if (Output is { IsDisposed: false } && _outputView != null
                                              && Output.Description.Width == width
                                              && Output.Description.Height == height
                                              && Output.Description.Format == format)
        {
            return;
        }

        _outputView?.Dispose();
        Output?.Dispose();

        Output = Texture2D.CreateTexture2D(new Texture2DDescription
                                               {
                                                   Width = width,
                                                   Height = height,
                                                   MipLevels = 1,
                                                   ArraySize = 1,
                                                   Format = format,
                                                   SampleDescription = new SampleDescription(1, 0),
                                                   Usage = ResourceUsage.Default,
                                                   BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                                   CpuAccessFlags = CpuAccessFlags.None,
                                                   OptionFlags = ResourceOptionFlags.None,
                                               });
        _outputView = new UnorderedAccessView(ResourceManager.Device, Output);
    }

    private const int GroupSize = 16;

    private readonly string _shaderAddress;
    private Resource<ComputeShader>? _shaderResource;
    private UnorderedAccessView? _outputView;
    private Buffer? _constants;
}
