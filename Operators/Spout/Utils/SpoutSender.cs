using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpoutDX;
using T3.Core.Output;
using DeviceContext = OpenGL.DeviceContext;
using DXTexture2D = SharpDX.Direct3D11.Texture2D;

namespace Lib.Utils;

/// <summary>
/// One Spout sender: the native SpoutDX object plus the shared-texture copies it sends from. Used by the
/// <c>SpoutOutput</c> op and offered to the output setup through <see cref="SpoutStreamProvider"/>, so both
/// paths share one implementation. The OpenGL context Spout needs is process-wide and refcounted across senders.
/// </summary>
internal sealed class SpoutSender : IOutputStreamSender
{
    public SpoutSender(string name)
    {
        _requestedName = name;
        _senderName = name;
        _instanceCount++;
    }

    /// <summary>The name Spout actually chose — differs from the requested one when it was already taken.</summary>
    public string Name => _senderName;

    public string LastError => _lastError;

    /// <summary>Spout has no frame-rate or alpha notion here — it shares the texture as it is.</summary>
    public void Configure(OutputStreamSettings settings)
    {
    }

    /// <summary>Re-targets the sender; takes effect on the next <see cref="Send"/>.</summary>
    public void Rename(string name)
    {
        _requestedName = name;
    }

    public bool Send(Texture2D frame)
    {
        if (frame == null)
            return false;

        int width, height;
        Texture2DDescription currentDesc;
        try
        {
            currentDesc = frame.Description;
            if (currentDesc.Format != _previousFormat)
            {
                _conversionWarning = false;
                _previousFormat = currentDesc.Format;
            }

            width = currentDesc.Width;
            height = currentDesc.Height;
            if (!Initialize(_requestedName, (uint)width, (uint)height))
                return false;
        }
        catch (InvalidOperationException e)
        {
            _lastError = "Initialization of Spout failed. Are Spout.dll and SpoutDX.dll present in the executable folder?";
            Log.Debug(_lastError);
            Log.Debug(e.ToString());
            ReleaseSpout();
            return false;
        }
        catch (Exception e)
        {
            _lastError = e.Message;
            Log.Debug(e.ToString());
            ReleaseSpout();
            return false;
        }

        var device = ResourceManager.Device;
        try
        {
            // Shared copies of the frame, so the sender never reads a texture the graph is still writing.
            if (_sharedImages.Count == 0
                || _sharedImages[0].Description.Format != currentDesc.Format
                || _sharedImages[0].Description.Width != currentDesc.Width
                || _sharedImages[0].Description.Height != currentDesc.Height
                || _sharedImages[0].Description.MipLevels != 1)
            {
                var imageDesc = new Texture2DDescription
                                    {
                                        BindFlags = BindFlags.ShaderResource,
                                        Format = currentDesc.Format,
                                        Width = currentDesc.Width,
                                        Height = currentDesc.Height,
                                        MipLevels = 1,
                                        SampleDescription = new SampleDescription(1, 0),
                                        Usage = ResourceUsage.Default,
                                        OptionFlags = ResourceOptionFlags.Shared,
                                        CpuAccessFlags = CpuAccessFlags.None,
                                        ArraySize = 1
                                    };
                DisposeTextures();
                for (var i = 0; i < NumTextureEntries; ++i)
                    _sharedImages.Add(new Texture2D(new DXTexture2D(device, imageDesc)));

                _currentIndex = 0;
            }

            if (_spoutDX == null || width == 0 || height == 0 || _width != width || _height != height)
                return false;

            var immediateContext = device.ImmediateContext;
            var readableImage = _sharedImages[_currentIndex];
            immediateContext.CopyResource((DXTexture2D)frame, readableImage);
            _currentIndex = (_currentIndex + 1) % NumTextureEntries;

            DXTexture2D dxTex;
            if (readableImage.Description.Format != Format.B8G8R8A8_UNorm &&
                readableImage.Description.Format != Format.B8G8R8A8_Typeless &&
                readableImage.Description.Format != Format.R8G8B8A8_UNorm &&
                readableImage.Description.Format != Format.R16G16B16A16_UNorm &&
                readableImage.Description.Format != Format.R16G16B16A16_Typeless &&
                readableImage.Description.Format != Format.R16G16B16A16_Float)
            {
                if (!_conversionWarning)
                {
                    Log.Debug($"Spout doesn't support {readableImage.Description.Format}, trying to fallback to R16G16B16A16_Float");
                    _conversionWarning = true;
                }

                dxTex = (DXTexture2D)_textureConverter.ConvertToCpuReadableBgra(readableImage);
            }
            else
            {
                dxTex = (DXTexture2D)readableImage;
            }

            if (dxTex != null)
            {
                _texture = ID3D11Texture2D.__CreateInstance((IntPtr)dxTex.NativePointer);
                _spoutDX.SendTexture(_texture);
            }

            _lastError = string.Empty;
        }
        catch (Exception e)
        {
            _lastError = "Texture sending failed: " + e.Message;
            Log.Debug("Texture sending failed : " + e);
        }
        finally
        {
            _texture?.Dispose();
        }

        return true;
    }

    public void Dispose()
    {
        DisposeTextures();
        ReleaseSpout();
        _textureConverter.Dispose();

        if (_instanceCount > 0)
            --_instanceCount;

        if (_instanceCount <= 0)
        {
            _device?.Dispose();
            _device = null;
            _deviceContext?.MakeCurrent(IntPtr.Zero);
            _deviceContext?.Dispose();
            _deviceContext = null;
            _initialized = false;
        }
    }

    private bool Initialize(string senderName, uint width, uint height)
    {
        if (!_initialized)
        {
            // create OpenGL context and make this become the primary context
            _deviceContext = DeviceContext.Create();
            _glContext = DeviceContext.GetCurrentContext();
            if (_glContext == IntPtr.Zero)
            {
                _glContext = _deviceContext.CreateContext(IntPtr.Zero);
                _deviceContext.MakeCurrent(_glContext);
            }

            _device = ID3D11Device.__CreateInstance((IntPtr)ResourceManager.Device);
            _initialized = true;
        }
        else if (_glContext != DeviceContext.GetCurrentContext())
        {
            // Make this become the primary context
            if (_deviceContext == null || !_deviceContext.MakeCurrent(_glContext))
                return false;
        }

        // A size or name change needs a fresh sender.
        if (width != _width || height != _height || senderName != _openedName)
        {
            DisposeTextures();
            ReleaseSpout();
            _width = 0;
            _height = 0;
        }

        if (_spoutDX == null)
        {
            _spoutDX = new SpoutDX.SpoutDX();
            _spoutDX.OpenDirectX11(_device);
            Log.Debug($"Spout output is using adapter {GetAdapterName()}");

            // Create the sender and read back the name Spout chose (unique among senders of the same name).
            _spoutDX.SenderName = senderName;
            _openedName = senderName;
            _senderName = _spoutDX.SenderName;
            _width = width;
            _height = height;
        }

        return true;
    }

    private void ReleaseSpout()
    {
        if (_spoutDX == null)
            return;

        _spoutDX.ReleaseSender();
        _spoutDX.CloseDirectX11();
        _spoutDX.Dispose();
        _spoutDX = null;
    }

    private string GetAdapterName()
    {
        var adapterName = Marshal.AllocHGlobal(1024);
        string adapter;
        unsafe
        {
            var name = (sbyte*)adapterName;
            _spoutDX.GetAdapterName(_spoutDX.Adapter, name, 1024);
            adapter = new string(name);
        }

        Marshal.FreeHGlobal(adapterName);
        return adapter;
    }

    private void DisposeTextures()
    {
        foreach (var image in _sharedImages)
            image.Dispose();

        _sharedImages.Clear();
    }

    private const int NumTextureEntries = 2;

    private static int _instanceCount;
    private static bool _initialized;
    private static DeviceContext _deviceContext;
    private static IntPtr _glContext;
    private static ID3D11Device _device;

    private SpoutDX.SpoutDX _spoutDX;
    private string _requestedName;
    private string _openedName;
    private string _senderName;
    private string _lastError = string.Empty;
    private uint _width;
    private uint _height;
    private ID3D11Texture2D _texture;
    private bool _conversionWarning;
    private Format _previousFormat = Format.Unknown;
    private readonly TextureBgraReadAccess _textureConverter = new(targetFormat: Format.R16G16B16A16_Float);
    private readonly List<Texture2D> _sharedImages = new(NumTextureEntries);
    private int _currentIndex;
}
