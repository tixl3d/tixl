using SpoutDX;
using DeviceContext = OpenGL.DeviceContext;
using Resource = SharpDX.DXGI.Resource;
using DXTexture2D = SharpDX.Direct3D11.Texture2D;

namespace Lib.io.video;

[Guid("13be1e3f-861d-4350-a94e-e083637b3e55")]
public class SpoutOutput : Instance<SpoutOutput>
{
    [Output(Guid = "297ea260-4486-48f8-b58f-8180acf0c2c5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> TextureOutput = new();

    public SpoutOutput()
    {
        if (!_triedForceLoad)
        {
            _triedForceLoad = true;
            // Symbol.SymbolPackage.TryLoadNativeDependencies("Spout", "SpoutDX");
        }
        TextureOutput.UpdateAction = Update;
        _instance++;
    }

    private void Update(EvaluationContext context)
    {
        var texture = Texture.GetValue(context);
        var senderName = SenderName.GetValue(context);

        TextureOutput.Value = texture;
        SendTexture(senderName, ref texture);
        SenderName.Update(context);
    }

    private string GetAdapterName()
    {
        IntPtr adapterName = Marshal.AllocHGlobal(1024);
        string adapter;
        unsafe
        {
            sbyte* name = (sbyte*)adapterName;
            _spoutDX.GetAdapterName(_spoutDX.Adapter, name, 1024);
            adapter = new string(name);
        }

        Marshal.FreeHGlobal(adapterName);
        return adapter;
    }

    private DXTexture2D CreateD3D11Texture2D(DXTexture2D d3d11Texture2D)
    {
        using (var resource = d3d11Texture2D.QueryInterface<Resource>())
        {
            return ResourceManager.Device.OpenSharedResource<DXTexture2D>(resource.SharedHandle);
        }
    }

    private bool InitializeSpout(string senderName, uint width, uint height)
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

            _device = ID3D11Device.__CreateInstance(((IntPtr)ResourceManager.Device));
            _initialized = true;
        }
        else if (_glContext != DeviceContext.GetCurrentContext())
        {
            // Make this become the primary context
            if (_deviceContext == null || !_deviceContext.MakeCurrent(_glContext))
                return false;
        }

        // get rid of old textures?
        if (width != _width || height != _height || senderName != _senderName)
        {
            DisposeTextures();

            if (_spoutDX != null)
            {
                _spoutDX.CloseDirectX11();
                _spoutDX.Dispose();
                _spoutDX = null;
            }

            _width = 0;
            _height = 0;
        }

        // create new spoutDX object
        if (_spoutDX == null)
        {
            _spoutDX = new SpoutDX.SpoutDX();
            _spoutDX.OpenDirectX11(_device);
            Log.Debug(@$"Spout output is using adapter {GetAdapterName()}");

            // create new sender and read back the actual name chosen by spout
            // (which may be different if you have multiple senders of the same name)
            _spoutDX.SenderName = senderName;
            _senderName = _spoutDX.SenderName;
            SenderName.SetTypedInputValue(_senderName);
            _width = width;
            _height = height;
        }

        return true;
    }

    private bool SendTexture(string senderName, ref Texture2D frame)
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
            if (!InitializeSpout(senderName, (uint)width, (uint)height))
                return false;
        }
        catch (InvalidOperationException e)
        {
            Log.Debug("Initialization of Spout failed. Are Spout.dll and SpoutDX.dll present in the executable folder?", this);
            Log.Debug(e.ToString());

            _spoutDX?.ReleaseSender();
            _spoutDX?.CloseDirectX11();
            _spoutDX?.Dispose();
            _spoutDX = null;
            return false;
        }
        catch (Exception e)
        {
            Log.Debug(e.ToString());
            _spoutDX?.ReleaseSender();
            _spoutDX?.CloseDirectX11();
            _spoutDX?.Dispose();
            _spoutDX = null;
            return false;
        }

        var device = ResourceManager.Device;
        try
        {
            // create several textures with a given format with CPU access
            // to be able to read out the initial texture values
            if (ImagesWithGpuAccess.Count == 0
                || ImagesWithGpuAccess[0].Description.Format != currentDesc.Format
                || ImagesWithGpuAccess[0].Description.Width != currentDesc.Width
                || ImagesWithGpuAccess[0].Description.Height != currentDesc.Height
                || ImagesWithGpuAccess[0].Description.MipLevels != 1)
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

                for (int i = 0; i < NumTextureEntries; ++i)
                {
                    ImagesWithGpuAccess.Add(new Texture2D(new DXTexture2D(device, imageDesc)));
                }

                _currentIndex = 0;
            }

            // sanity check
            if (_spoutDX == null || width == 0 || height == 0 || _width != width || _height != height)
                return false;

            // copy the original texture to a readable image
            var immediateContext = device.ImmediateContext;
            var readableImage = ImagesWithGpuAccess[_currentIndex];

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
            else dxTex = (DXTexture2D)readableImage;
            if (dxTex != null)
            {
                _texture = ID3D11Texture2D.__CreateInstance((IntPtr)dxTex.NativePointer);
                _spoutDX.SendTexture(_texture);
            }
        }
        catch (Exception e)
        {
            Log.Debug("Texture sending failed : " + e.ToString());
        }
        finally
        {
            _texture?.Dispose();
        }

        return true;
    }

    protected void DisposeTextures()
    {
        foreach (var image in ImagesWithGpuAccess)
            image.Dispose();

        ImagesWithGpuAccess.Clear();
    }

    #region IDisposable Support
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;
        // dispose textures
        DisposeTextures();

        _spoutDX?.ReleaseSender();
        _spoutDX?.CloseDirectX11();
        _spoutDX?.Dispose();

        if (_instance > 0)
            --_instance;

        if (_instance <= 0)
        {
            _device?.Dispose();
            _deviceContext?.MakeCurrent(IntPtr.Zero);
            _deviceContext?.Dispose();
            _initialized = false;
        }
    }
    #endregion

    private static int _instance; // number of instances of this object
    private static bool _initialized; // were static members initialized?
    private static DeviceContext _deviceContext; // OpenGL device context
    private static IntPtr _glContext; // OpenGL context
    private static ID3D11Device _device; // Direct3D11 device

    private SpoutDX.SpoutDX _spoutDX; // spout object supporting DirectX
    private string _senderName; // name of our spout sender
    private uint _width; // current width of our sender
    private uint _height; // current height of our sender
    private ID3D11Texture2D _texture; // texture to send
    private bool _triedForceLoad = false; // have we tried to force load the native dependencies?

    private bool _conversionWarning = false; // Warn the user about conversion, just once

    private Format _previousFormat = Format.Unknown;

    // hold several textures internally to speed up calculations
    private const int NumTextureEntries = 2;

    private TextureBgraReadAccess _textureConverter = new(targetFormat: Format.R16G16B16A16_Float);

    private readonly List<Texture2D> ImagesWithGpuAccess = new() { Capacity = NumTextureEntries };

    // current image index (used for circular access of ImagesWithGpuAccess)
    private int _currentIndex;

    [Input(Guid = "d4b5c642-9cb9-4f41-8739-edbb9c6c4857")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "7C27EBD7-3746-4B70-A252-DD0AC0445B74")]
    public readonly InputSlot<string> SenderName = new();
}