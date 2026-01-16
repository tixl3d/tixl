#nullable enable
using T3.Core.Utils;
using NewTek;
using SharpDX.Direct3D11;

namespace Lib.io.video;

[Guid("9412d0f4-dab8-4145-9719-10395e154fa7")]
public sealed class NdiOutput : Instance<NdiOutput>, IStatusProvider
{
    [Output(Guid = "3c0ae0e5-a2af-4437-b7fa-8ad300cb8b8b", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Texture2D?> TextureOutput = new();

    public NdiOutput()
    {
        TextureOutput.UpdateAction = Update;
    }
    
    private void Update(EvaluationContext context)
    {
        var texture = Texture.GetValue(context);
        var senderName = SenderName.GetValue(context) ?? "unknown";
        var fps = FrameRate.GetValue(context);
        var alpha = EnableAlpha.GetValue(context);

        TextureOutput.Value = texture;

        if (texture == null)
        {
            _lastErrorMessage = "No texture input";
            return;
        }

        if (!_allowedFormats.Contains(texture.Description.Format))
        {
            _lastErrorMessage =
                "Texture format must be one of the following:\nB8G8R8A8_UNorm\nR8G8B8A8_UNorm\nB8G8R8A8_Typeless\nR8G8B8A8_Typeless \n\nPlease us [ConvertFormat] to convert the texture to a supported format.";
            return;
        }

        if (texture.Description.SampleDescription.Count > 1)
        {
            _lastErrorMessage = "Multisampled textures are not supported";
            return;
        }

        if (texture.Description.ArraySize != 1)
        {
            _lastErrorMessage = "Texture arrays are not supported";
            return;
        }

        SendTexture(senderName, fps, alpha, ref texture);
        SenderName.Update(context);
        _lastErrorMessage = string.Empty;
    }

    private void SetupNdiSender(string senderName, uint width, uint height)
    {
        IntPtr groupsNamePtr = IntPtr.Zero;

        IntPtr ndiString = NewTek.NDI.UTF.StringToUtf8(senderName);

        // Create an NDI source description using sourceNamePtr and it's clocked to the video.
        NDIlib.send_create_t createDesc = new NDIlib.send_create_t()
                                              {
                                                  p_ndi_name = ndiString,
                                                  p_groups = groupsNamePtr,
                                                  clock_video = true,
                                                  clock_audio = false
                                              };

        // We create the NDI finder instance
        _ndiSender = NDIlib.send_create(ref createDesc);

        Marshal.FreeHGlobal(ndiString); //created with allochglobal in utils
        _currentSenderName = senderName;
    }

    private bool SendTexture(string senderName, int frameRate, bool enableAlpha, ref Texture2D? frame)
    {
        var device = ResourceManager.Device;

        if (frame == null)
            return false;

        if (_stagingTexture != null)
        {
            //reset sender if resolution is different
            if (_stagingTexture.Description.Width != frame.Description.Width
                || _stagingTexture.Description.Height != frame.Description.Height
                || senderName != _currentSenderName)
            {
                ReleaseCpuData();
            }
        }

        int width = frame.Description.Width;
        int height = frame.Description.Height;
        int stride = width * 4;

        //whichever null or intptr zero does the job
        if (_stagingTexture == null)
        {
            SetupNdiSender(senderName, (uint)frame.Description.Width, (uint)frame.Description.Height);

            Texture2DDescription stagingDesc = frame.Description;
            stagingDesc.BindFlags = BindFlags.None;
            stagingDesc.CpuAccessFlags = CpuAccessFlags.Read;
            stagingDesc.Usage = ResourceUsage.Staging;
            stagingDesc.MipLevels = 1; // later use copysubresource to use only mip 1 if necessary
            stagingDesc.OptionFlags = ResourceOptionFlags.None;

            _stagingTexture = Texture2D.CreateTexture2D(stagingDesc);

            _textureData = Marshal.AllocHGlobal(width * height * 4);
        }

        //Readback from gpu
        {
            //device.ImmediateContext.CopyResource(frame, stagingTexture);

            //This allows to only copy mip level 0;
            device.ImmediateContext.CopySubresourceRegion(frame, 0, null, _stagingTexture, 0);

            var dataBox = device.ImmediateContext.MapSubresource(_stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out _);
            Utilities.CopyImageMemory(dataBox.DataPointer, _textureData, height, dataBox.RowPitch, stride);
            device.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
        }

        //Send data
        {
            bool isBgr = frame.Description.Format == Format.B8G8R8A8_UNorm || frame.Description.Format == Format.B8G8R8A8_Typeless;

            NDIlib.FourCC_type_e fcc;
            if (isBgr)
            {
                fcc = enableAlpha ? NDIlib.FourCC_type_e.FourCC_type_BGRA : NDIlib.FourCC_type_e.FourCC_type_BGRX;
            }
            else
            {
                fcc = enableAlpha ? NDIlib.FourCC_type_e.FourCC_type_RGBA : NDIlib.FourCC_type_e.FourCC_type_RGBX;
            }

            NDIlib.video_frame_v2_t videoFrame = new NDIlib.video_frame_v2_t()
                                                     {
                                                         // Resolution
                                                         xres = width,
                                                         yres = height,
                                                         FourCC = fcc,
                                                         frame_rate_N = frameRate,
                                                         frame_rate_D = 1,
                                                         picture_aspect_ratio = (float)width / (float)height,
                                                         frame_format_type = NDIlib.frame_format_type_e.frame_format_type_progressive,
                                                         timecode = NDIlib.send_timecode_synthesize,
                                                         p_data = _textureData,
                                                         line_stride_in_bytes = stride,
                                                         p_metadata = IntPtr.Zero
                                                     };

            int connectionCount = NDIlib.send_get_no_connections(_ndiSender, 10);

            if (connectionCount > 0)
            {
                NDIlib.send_send_video_v2(_ndiSender, ref videoFrame);
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    #region IDisposable Support
    private void ReleaseCpuData()
    {
        Utilities.Dispose(ref _stagingTexture);
        if (_ndiSender != IntPtr.Zero)
        {
            // Destroy the NDI sender
            NDIlib.send_destroy(_ndiSender);
            _ndiSender = IntPtr.Zero;
        }

        if (_textureData != IntPtr.Zero)
        {
            // Destroy the NDI sender
            Marshal.FreeHGlobal(_textureData);
            _textureData = IntPtr.Zero;
        }
    }
    
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        ReleaseCpuData();
    }
    
    #endregion
    
    private static readonly Format[] _allowedFormats =
        [
            Format.B8G8R8A8_UNorm,
            Format.R8G8B8A8_UNorm,
            Format.B8G8R8A8_Typeless,
            Format.R8G8B8A8_Typeless
        ];
    
    private IntPtr _ndiSender = IntPtr.Zero;
    private string _currentSenderName = string.Empty;

    /** Texture to send */
    private Texture2D? _stagingTexture;  
    
    /** Need a copy here since we need to handle stride (also necessary if we want to queue frame later to remove stall) */
    private IntPtr _textureData; 

    [Input(Guid = "15ddab7a-aad4-49eb-9e56-70714e10679f")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "740db4c4-4182-4743-a713-5a45855499d9")]
    public readonly InputSlot<string> SenderName = new();

    [Input(Guid = "CE0E267E-E96D-441F-86FB-BACEF1F0186A")]
    public readonly InputSlot<int> FrameRate = new();

    [Input(Guid = "E6359F2E-E4F7-419D-A403-CE5EB0528898")]
    public readonly InputSlot<bool> EnableAlpha = new();

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Error;

    string? IStatusProvider.GetStatusMessage() => _lastErrorMessage;

    private string? _lastErrorMessage;
}