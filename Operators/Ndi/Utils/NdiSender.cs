#nullable enable
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NewTek;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.Output;
using T3.Core.Output.Streaming;
using T3.Core.Resource.Assets;
using T3.Core.Utils;

namespace Lib.Utils;

/// <summary>
/// One NDI sender. Used by the <c>NdiOutput</c> op and offered to the output setup through
/// <see cref="NdiStreamProvider"/>, so both paths share one implementation.
/// <para>Built so a large frame never stalls the render loop: the frame is packed to UYVY on the GPU (unless
/// alpha is sent), copied into a ring of staging textures, and a frame from two sends ago — finished by then —
/// is mapped and handed to NDI's asynchronous send without a CPU copy. NDI compresses on its own threads while
/// the next frame renders; the frame time becomes the larger of render and encode instead of their sum.</para>
/// </summary>
internal sealed class NdiSender : IOutputStreamSender
{
    public NdiSender(string name)
    {
        _requestedName = name;
    }

    public string Name => _requestedName;

    public string? LastError => _lastError;

    /// <summary>Frames per second advertised to receivers.</summary>
    public int FrameRate = 60;

    /// <summary>Whether the alpha channel is sent, or receivers get an opaque frame.</summary>
    public bool EnableAlpha;

    public void Configure(OutputStreamSettings settings)
    {
        FrameRate = settings.FrameRate;
        EnableAlpha = settings.EnableAlpha;
    }

    /// <summary>Re-targets the sender; takes effect on the next <see cref="Send"/>.</summary>
    public void Rename(string name)
    {
        _requestedName = name;
    }

    public bool Send(Texture2D frame)
    {
        if (frame == null)
        {
            _lastError = "No texture";
            return false;
        }

        var format = frame.Description.Format;
        if (!IsSupported(format))
        {
            _lastError = """
                         Texture format must be one of the following:
                         B8G8R8A8_UNorm
                         R8G8B8A8_UNorm
                         B8G8R8A8_Typeless
                         R8G8B8A8_Typeless

                         Please use [ConvertFormat] to convert the texture to a supported format.
                         """;
            return false;
        }

        if (frame.Description.SampleDescription.Count > 1)
        {
            _lastError = "Multisampled textures are not supported";
            return false;
        }

        if (frame.Description.ArraySize != 1)
        {
            _lastError = "Texture arrays are not supported";
            return false;
        }

        _lastError = null;

        var width = frame.Description.Width;
        var height = frame.Description.Height;

        // UYVY carries no alpha, and a typeless frame can't be sampled for the conversion without knowing its format.
        var packsToUyvy = !EnableAlpha && format is Format.B8G8R8A8_UNorm or Format.R8G8B8A8_UNorm;

        // A new name, or a frame of another shape than the pipeline was built for, starts over. The shape is only
        // compared once a pipeline exists: it is built on the first frame a receiver watches, and a sender torn
        // down every frame before that could never be discovered.
        var shapeChanged = _stagingTextures.Count > 0
                           && (width != _width || height != _height || packsToUyvy != _packsToUyvy || format != _sourceFormat);
        if (_ndiSender != IntPtr.Zero && (shapeChanged || _requestedName != _openedName))
            Release();

        if (_ndiSender == IntPtr.Zero)
            Open(_requestedName);

        // Checked before any GPU work: with nobody listening, reading 46 megapixels back only to drop them is waste.
        // No timeout — waiting here would stall the render loop every frame nobody watches.
        if (NDIlib.send_get_no_connections(_ndiSender, 0) <= 0)
        {
            DropQueuedFrames();
            return false;
        }

        if (_stagingTextures.Count == 0 && !TryCreatePipeline(frame, width, height, packsToUyvy))
            return false;

        var context = ResourceManager.Device.ImmediateContext;
        Texture2D copySource = frame;
        if (packsToUyvy)
        {
            if (!TryPackToUyvy(frame))
                return false;

            copySource = _packedTexture!;
        }

        var freeSlot = FindFreeSlot();
        if (freeSlot < 0)
            return false;

        context.CopySubresourceRegion(copySource, 0, null, _stagingTextures[freeSlot], 0);
        _queuedSlots.Enqueue(freeSlot);

        // Send the frame from two sends ago: by now the GPU has finished its copy, so mapping it doesn't stall.
        if (_queuedSlots.Count <= LatencyInFrames)
            return true;

        var sendSlot = _queuedSlots.Dequeue();
        var dataBox = context.MapSubresource(_stagingTextures[sendSlot], 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out _);

        var videoFrame = new NDIlib.video_frame_v2_t
                             {
                                 xres = packsToUyvy ? _packedWidth * 2 : width,
                                 yres = height,
                                 FourCC = FourCcFor(format, packsToUyvy),
                                 frame_rate_N = FrameRate,
                                 frame_rate_D = 1,
                                 picture_aspect_ratio = (float)width / height,
                                 frame_format_type = NDIlib.frame_format_type_e.frame_format_type_progressive,
                                 timecode = NDIlib.send_timecode_synthesize,
                                 p_data = dataBox.DataPointer,
                                 line_stride_in_bytes = dataBox.RowPitch,
                                 p_metadata = IntPtr.Zero,
                             };

        // Returns once NDI has taken the previous frame, which is when that frame's buffer may be let go.
        NDIlib.send_send_video_async_v2(_ndiSender, ref videoFrame);
        UnmapInFlightSlot();
        _inFlightSlot = sendSlot;
        return true;
    }

    public void Dispose()
    {
        Release();
    }

    private void Open(string senderName)
    {
        var ndiString = NewTek.NDI.UTF.StringToUtf8(senderName);
        var createDesc = new NDIlib.send_create_t
                             {
                                 p_ndi_name = ndiString,
                                 p_groups = IntPtr.Zero,

                                 // The host already paces its frames; NDI's clock would block the render loop to a
                                 // cadence of its own on every send.
                                 clock_video = false,
                                 clock_audio = false,
                             };

        _ndiSender = NDIlib.send_create(ref createDesc);
        Marshal.FreeHGlobal(ndiString);
        _openedName = senderName;
    }

    private bool TryCreatePipeline(Texture2D frame, int width, int height, bool packsToUyvy)
    {
        _width = width;
        _height = height;
        _packsToUyvy = packsToUyvy;
        _sourceFormat = frame.Description.Format;
        _packedWidth = (width + 1) / 2;

        Texture2DDescription stagingDescription;
        if (packsToUyvy)
        {
            if (!TryLoadPackShader())
                return false;

            var packedDescription = new Texture2DDescription
                                        {
                                            Width = _packedWidth,
                                            Height = height,
                                            MipLevels = 1,
                                            ArraySize = 1,
                                            Format = Format.R8G8B8A8_UNorm,
                                            SampleDescription = new SampleDescription(1, 0),
                                            Usage = ResourceUsage.Default,
                                            BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                            CpuAccessFlags = CpuAccessFlags.None,
                                            OptionFlags = ResourceOptionFlags.None,
                                        };
            _packedTexture = Texture2D.CreateTexture2D(packedDescription);
            _packedUav = new UnorderedAccessView(ResourceManager.Device, _packedTexture);
            stagingDescription = packedDescription;
        }
        else
        {
            stagingDescription = frame.Description;
            stagingDescription.MipLevels = 1;
        }

        stagingDescription.BindFlags = BindFlags.None;
        stagingDescription.CpuAccessFlags = CpuAccessFlags.Read;
        stagingDescription.Usage = ResourceUsage.Staging;
        stagingDescription.OptionFlags = ResourceOptionFlags.None;
        for (var i = 0; i < SlotCount; i++)
        {
            _stagingTextures.Add(Texture2D.CreateTexture2D(stagingDescription));
        }

        return true;
    }

    private bool TryLoadPackShader()
    {
        if (_packShaderResource?.Value != null)
            return true;

        if (!AssetRegistry.TryResolveAddress(PackShaderAddress, null, out var shaderPath, out _))
        {
            _lastError = $"Can't find {PackShaderAddress}";
            return false;
        }

        _packShaderResource = ResourceManager.CreateShaderResource<ComputeShader>(shaderPath, null, () => "main");
        if (_packShaderResource.Value != null)
            return true;

        _lastError = "The UYVY packing shader failed to compile";
        return false;
    }

    private bool TryPackToUyvy(Texture2D frame)
    {
        var shader = _packShaderResource?.Value;
        var sourceView = SrvManager.GetSrvForTexture(frame);
        if (shader == null || sourceView == null || _packedUav == null)
            return false;

        var context = ResourceManager.Device.ImmediateContext;
        var computeStage = context.ComputeShader;

        // Everything touched is put back: this runs inside whatever the host is rendering.
        var previousShader = computeStage.Get();
        var previousUavs = computeStage.GetUnorderedAccessViews(0, 1);
        var previousSrvs = computeStage.GetShaderResources(0, 1);

        // The frame is often still bound as a render target (the output compositor leaves its target bound), and
        // D3D11 silently drops a view onto a resource that is bound for writing — the shader would read black.
        var previousTargets = context.OutputMerger.GetRenderTargets(MaxRenderTargets, out var previousDepth);
        context.OutputMerger.SetRenderTargets((DepthStencilView?)null, (RenderTargetView?)null);

        computeStage.Set(shader);
        computeStage.SetShaderResource(0, sourceView);
        computeStage.SetUnorderedAccessView(0, _packedUav);
        context.Dispatch((_packedWidth + 15) / 16, (_height + 15) / 16, 1);

        // Unbound before anything else touches the packed texture, which is read next as a copy source.
        computeStage.SetUnorderedAccessView(0, previousUavs[0]);
        computeStage.SetShaderResource(0, previousSrvs[0]);
        computeStage.Set(previousShader);
        context.OutputMerger.SetRenderTargets(previousDepth, previousTargets);

        // Getters hand out references that must be released.
        previousShader?.Dispose();
        previousUavs[0]?.Dispose();
        previousSrvs[0]?.Dispose();
        previousDepth?.Dispose();
        foreach (var target in previousTargets)
            target?.Dispose();

        return true;
    }

    /** A slot neither queued for sending nor held by NDI; -1 when all are busy (they never should be). */
    private int FindFreeSlot()
    {
        for (var i = 0; i < _stagingTextures.Count; i++)
        {
            if (i != _inFlightSlot && !_queuedSlots.Contains(i))
                return i;
        }

        return -1;
    }

    /** Frames queued while a receiver watched are stale once it leaves; the next one starts the queue afresh. */
    private void DropQueuedFrames()
    {
        _queuedSlots.Clear();
    }

    private void UnmapInFlightSlot()
    {
        if (_inFlightSlot < 0)
            return;

        ResourceManager.Device.ImmediateContext.UnmapSubresource(_stagingTextures[_inFlightSlot], 0);
        _inFlightSlot = -1;
    }

    private void Release()
    {
        // Destroying the sender waits for NDI to finish with the frame in flight, so its buffer is unmapped after.
        if (_ndiSender != IntPtr.Zero)
        {
            NDIlib.send_destroy(_ndiSender);
            _ndiSender = IntPtr.Zero;
        }

        UnmapInFlightSlot();
        _queuedSlots.Clear();
        foreach (var stagingTexture in _stagingTextures)
            stagingTexture.Dispose();

        _stagingTextures.Clear();
        Utilities.Dispose(ref _packedUav);
        Utilities.Dispose(ref _packedTexture);
    }

    private static NDIlib.FourCC_type_e FourCcFor(Format format, bool packsToUyvy)
    {
        if (packsToUyvy)
            return NDIlib.FourCC_type_e.FourCC_type_UYVY;

        var isBgr = format is Format.B8G8R8A8_UNorm or Format.B8G8R8A8_Typeless;
        return isBgr ? NDIlib.FourCC_type_e.FourCC_type_BGRA : NDIlib.FourCC_type_e.FourCC_type_RGBA;
    }

    private static bool IsSupported(Format format)
    {
        return format is Format.B8G8R8A8_UNorm or Format.R8G8B8A8_UNorm or Format.B8G8R8A8_Typeless or Format.R8G8B8A8_Typeless;
    }

    /** Frames between copying one into a staging texture and mapping it: enough for the GPU to be done with it. */
    private const int LatencyInFrames = 2;

    /** Queued frames plus the one NDI still holds. */
    private const int SlotCount = LatencyInFrames + 2;

    private const string PackShaderAddress = "Lib:shaders/img/rgba-to-uyvy-cs.hlsl";

    /** D3D11's number of simultaneous render targets, so every one the host has bound is put back. */
    private const int MaxRenderTargets = 8;

    private IntPtr _ndiSender = IntPtr.Zero;
    private string _requestedName;
    private string _openedName = string.Empty;
    private string? _lastError;

    private int _width;
    private int _height;
    private int _packedWidth;
    private bool _packsToUyvy;
    private Format _sourceFormat;

    private Texture2D? _packedTexture;
    private UnorderedAccessView? _packedUav;
    private Resource<ComputeShader>? _packShaderResource;

    private readonly List<Texture2D> _stagingTextures = [];
    private readonly Queue<int> _queuedSlots = new();
    private int _inFlightSlot = -1;
}
