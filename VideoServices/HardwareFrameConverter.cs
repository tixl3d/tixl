using System;
using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.Resource;
using ComputeShader = T3.Core.DataTypes.ComputeShader;
using CoreTexture2D = T3.Core.DataTypes.Texture2D;
using SharpDxTexture2D = SharpDX.Direct3D11.Texture2D;

namespace T3.VideoServices;

/// <summary>
/// GPU YUV→RGBA converter for the D3D11VA zero-copy path. A hardware <see cref="Frame"/> carries the decoder
/// surface in <c>data[0]</c> (an ID3D11Texture2D array) and the array slice index in <c>data[1]</c>. This wraps
/// that surface, builds luma + chroma plane SRVs for the slice — R8/R8G8 for NV12 (8-bit), R16/R16G16 for
/// P010/P016 (10/12-bit) — and dispatches a compute shader into an RGBA output texture's UAV (RGBA8 for 8-bit,
/// RGBA16 for deeper) — no CPU round-trip.
///
/// Must run on the render thread (immediate context). Owns the output texture + UAV; the caller reads
/// <see cref="Convert"/>'s result and must not dispose it.
/// </summary>
internal sealed class HardwareFrameConverter : IDisposable
{
    public unsafe CoreTexture2D? Convert(Frame gpuFrame, int width, int height)
    {
        var shader = ShaderResource?.Value;
        if (shader == null || width <= 0 || height <= 0)
            return _output;

        // Only a real D3D11VA surface carries an ID3D11Texture2D in data[0]. A hardware decoder can still emit a
        // software-format frame — e.g. the first frame right after opening, while the hwaccel initialises — even
        // though the session already reports zero-copy. Treating that frame's data[0] (a CPU pixel buffer) as a COM
        // object would AddRef a non-pointer and crash hard (AccessViolationException, uncatchable). Skip it; the
        // next real surface converts normally and the last-valid texture stays on screen meanwhile.
        if ((AVPixelFormat)gpuFrame.Format != AVPixelFormat.D3d11)
            return _output;

        var texturePtr = (IntPtr)gpuFrame.Data[0];
        var sliceIndex = (int)gpuFrame.Data[1];
        if (texturePtr == IntPtr.Zero)
            return _output;

        var device = ResourceManager.Device;
        var csStage = device.ImmediateContext.ComputeShader;

        // Decode (worker) and convert (here) share one D3D11 device. FFmpeg guards its decode with DeviceLock
        // (its lock/unlock callbacks take it — see VideoDecoderSession), so take the same lock around the
        // convert: the two never touch the GPU at once.
        lock (DeviceLock)
        {
            // Borrow the FFmpeg-owned decoder texture: AddRef so the SharpDX wrapper's Dispose-time Release is
            // balanced and FFmpeg's own reference is left intact. (Risk spot: if the pool exhausts after ~20
            // frames and decode stalls, this AddRef is doubling a ref the ctor already takes — drop it then.)
            Marshal.AddRef(texturePtr);
            using var decoderTexture = new SharpDxTexture2D(texturePtr);

            // NV12 (8-bit) reads R8/R8G8 plane SRVs into an RGBA8 output; P010/P016 (10/12-bit) read R16/R16G16
            // into RGBA16 so the extra bits survive. The plane is selected by the SRV format on the decoder
            // texture-array slice (D3D11 maps the format to the plane; PlaneSlice is a D3D12 concept).
            var tenBit = decoderTexture.Description.Format != Format.NV12;
            var lumaFormat = tenBit ? Format.R16_UNorm : Format.R8_UNorm;
            var chromaFormat = tenBit ? Format.R16G16_UNorm : Format.R8G8_UNorm;
            var outputFormat = tenBit ? Format.R16G16B16A16_UNorm : Format.R8G8B8A8_UNorm;

            EnsureOutput(width, height, outputFormat);

            using var lumaSrv = new ShaderResourceView(device, decoderTexture, PlaneSrvDesc(lumaFormat, sliceIndex));
            using var chromaSrv = new ShaderResourceView(device, decoderTexture, PlaneSrvDesc(chromaFormat, sliceIndex));

            var prevShader = csStage.Get();
            var prevUavs = csStage.GetUnorderedAccessViews(0, 1);
            var prevSrvs = csStage.GetShaderResources(0, 2);

            csStage.Set(shader);
            csStage.SetShaderResource(0, lumaSrv);
            csStage.SetShaderResource(1, chromaSrv);
            csStage.SetUnorderedAccessView(0, _outputUav);

            device.ImmediateContext.Dispatch((width + 15) / 16, (height + 15) / 16, 1);

            // Restore previous compute-stage bindings, then release the refs Get* handed us (else they leak per frame).
            csStage.Set(prevShader);
            csStage.SetUnorderedAccessView(0, prevUavs[0]);
            csStage.SetShaderResource(0, prevSrvs[0]);
            csStage.SetShaderResource(1, prevSrvs[1]);
            prevShader?.Dispose();
            prevUavs[0]?.Dispose();
            prevSrvs[0]?.Dispose();
            prevSrvs[1]?.Dispose();
        }

        return _output;
    }

    private static ShaderResourceViewDescription PlaneSrvDesc(Format format, int slice) => new()
        {
            Format = format,
            Dimension = ShaderResourceViewDimension.Texture2DArray,
            Texture2DArray = new ShaderResourceViewDescription.Texture2DArrayResource
                {
                    FirstArraySlice = slice,
                    ArraySize = 1,
                    MipLevels = 1,
                    MostDetailedMip = 0,
                },
        };

    private void EnsureOutput(int width, int height, Format format)
    {
        if (_output != null && _output.Description.Width == width && _output.Description.Height == height
            && _output.Description.Format == format)
            return;

        DisposeOutput();
        _output = CoreTexture2D.CreateTexture2D(new Texture2DDescription
            {
                Width = width,
                Height = height,
                ArraySize = 1,
                MipLevels = 1,
                Format = format,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget | BindFlags.UnorderedAccess,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None,
                Usage = ResourceUsage.Default,
                SampleDescription = new SampleDescription(1, 0),
            });
        _outputUav = new UnorderedAccessView(ResourceManager.Device, _output);
    }

    private Resource<ComputeShader>? ShaderResource =>
        _shaderResource ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, null, () => "main");

    // Held by FFmpeg's decode (its D3D11VA lock/unlock callbacks) and by this converter's dispatch, so decode
    // and convert never run on the shared device simultaneously.
    internal static readonly object DeviceLock = new();

    public void Dispose() => DisposeOutput();

    private void DisposeOutput()
    {
        _outputUav?.Dispose();
        _outputUav = null;
        _output?.Dispose();
        _output = null;
    }

    private const string ShaderPath = "Lib:shaders/img/Nv12ToRgba-cs.hlsl";
    private Resource<ComputeShader>? _shaderResource;
    private CoreTexture2D? _output;
    private UnorderedAccessView? _outputUav;
}
