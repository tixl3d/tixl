#nullable enable
using System.Runtime.InteropServices;
using NewTek;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.Output;
using T3.Core.Utils;

namespace Lib.Utils;

/// <summary>
/// One NDI sender: the native sender handle plus the staging texture and CPU buffer frames are read back
/// through. Used by the <c>NdiOutput</c> op and offered to the output setup through
/// <see cref="NdiStreamProvider"/>, so both paths share one implementation.
/// </summary>
internal sealed class NdiSender : IOutputStreamSender
{
    public NdiSender(string name)
    {
        _requestedName = name;
    }

    public string Name => _requestedName;

    public string? LastError => _lastError;

    /// <summary>Frames per second advertised to receivers (the sender is clocked to the video it gets).</summary>
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
        var device = ResourceManager.Device;

        // A size or name change needs a fresh sender.
        if (_stagingTexture != null
            && (_stagingTexture.Description.Width != frame.Description.Width
                || _stagingTexture.Description.Height != frame.Description.Height
                || _requestedName != _openedName))
        {
            Release();
        }

        var width = frame.Description.Width;
        var height = frame.Description.Height;
        var stride = width * 4;

        if (_stagingTexture == null)
        {
            Open(_requestedName);

            var stagingDesc = frame.Description;
            stagingDesc.BindFlags = BindFlags.None;
            stagingDesc.CpuAccessFlags = CpuAccessFlags.Read;
            stagingDesc.Usage = ResourceUsage.Staging;
            stagingDesc.MipLevels = 1;
            stagingDesc.OptionFlags = ResourceOptionFlags.None;
            _stagingTexture = Texture2D.CreateTexture2D(stagingDesc);
            _textureData = Marshal.AllocHGlobal(width * height * 4);
        }

        // Read back mip 0 only.
        device.ImmediateContext.CopySubresourceRegion(frame, 0, null, _stagingTexture, 0);
        var dataBox = device.ImmediateContext.MapSubresource(_stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out _);
        Utilities.CopyImageMemory(dataBox.DataPointer, _textureData, height, dataBox.RowPitch, stride);
        device.ImmediateContext.UnmapSubresource(_stagingTexture, 0);

        var isBgr = format is Format.B8G8R8A8_UNorm or Format.B8G8R8A8_Typeless;
        NDIlib.FourCC_type_e fcc;
        if (isBgr)
            fcc = EnableAlpha ? NDIlib.FourCC_type_e.FourCC_type_BGRA : NDIlib.FourCC_type_e.FourCC_type_BGRX;
        else
            fcc = EnableAlpha ? NDIlib.FourCC_type_e.FourCC_type_RGBA : NDIlib.FourCC_type_e.FourCC_type_RGBX;

        var videoFrame = new NDIlib.video_frame_v2_t
                             {
                                 xres = width,
                                 yres = height,
                                 FourCC = fcc,
                                 frame_rate_N = FrameRate,
                                 frame_rate_D = 1,
                                 picture_aspect_ratio = (float)width / height,
                                 frame_format_type = NDIlib.frame_format_type_e.frame_format_type_progressive,
                                 timecode = NDIlib.send_timecode_synthesize,
                                 p_data = _textureData,
                                 line_stride_in_bytes = stride,
                                 p_metadata = IntPtr.Zero
                             };

        // Nobody listening: skip the send rather than block.
        if (NDIlib.send_get_no_connections(_ndiSender, 10) <= 0)
            return false;

        NDIlib.send_send_video_v2(_ndiSender, ref videoFrame);
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
                                 clock_video = true,
                                 clock_audio = false
                             };

        _ndiSender = NDIlib.send_create(ref createDesc);
        Marshal.FreeHGlobal(ndiString);
        _openedName = senderName;
    }

    private void Release()
    {
        Utilities.Dispose(ref _stagingTexture);
        if (_ndiSender != IntPtr.Zero)
        {
            NDIlib.send_destroy(_ndiSender);
            _ndiSender = IntPtr.Zero;
        }

        if (_textureData != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_textureData);
            _textureData = IntPtr.Zero;
        }
    }

    private static bool IsSupported(Format format)
    {
        return format is Format.B8G8R8A8_UNorm or Format.R8G8B8A8_UNorm or Format.B8G8R8A8_Typeless or Format.R8G8B8A8_Typeless;
    }

    private IntPtr _ndiSender = IntPtr.Zero;
    private string _requestedName;
    private string _openedName = string.Empty;
    private string? _lastError;
    private Texture2D? _stagingTexture;

    // CPU copy with the stride NDI expects (also what a later frame queue would hand off).
    private IntPtr _textureData;
}
