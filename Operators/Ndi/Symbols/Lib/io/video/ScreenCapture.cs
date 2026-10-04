#nullable enable
using SharpDX.DXGI;
using T3.Core.Utils;

namespace Lib.io.video;

[Guid("80032e95-90ec-486d-92a4-ff3d225e556e")]
public sealed class ScreenCapture : Instance<ScreenCapture>
{
    [Output(Guid = "16D64181-ABFE-4B34-98C3-87B88D049E50", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Texture2D?> TextureOutput = new();

    public ScreenCapture()
    {
        TextureOutput.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var device = ResourceManager.Device;
        var screenIndex = ScreenIndex.GetValue(context);
        var timeOut = TimeOut.GetValue(context);

        if (_currentScreenIndex != screenIndex)
        {
            Utilities.Dispose(ref _dup);
            _currentScreenIndex = screenIndex;
        }

        if (_dup == null)
        {
            using var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
            
            var dxgiAdapter = dxgiDevice.GetParent<SharpDX.DXGI.Adapter>();

            var output = dxgiAdapter.GetOutput(Utilities.InfiniteModIndexer(screenIndex, dxgiAdapter.GetOutputCount()));

            using var o1 = output.QueryInterface<Output1>();
            _dup = o1.DuplicateOutput(device);
        }

        var capResult = _dup.TryAcquireNextFrame(timeOut, out _, out var newScreenResource);

        if (capResult.Success)
        {
            using (var newTexture = newScreenResource.QueryInterface<SharpDX.Direct3D11.Texture2D>())
            {
                if (_currentScreen != null)
                {
                    if (_currentScreen.Description.Width != newTexture.Description.Width
                        || _currentScreen.Description.Height != newTexture.Description.Height
                        || _currentScreen.Description.Format != newTexture.Description.Format)
                    {
                        Utilities.Dispose(ref _currentScreen);
                    }
                }

                if (_currentScreen == null)
                {
                    var desc = newTexture.Description;
                    desc.OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None;
                    desc.BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource;
                    _currentScreen = Texture2D.CreateTexture2D(desc);
                    //using (var newTex = new SharpDX.Direct3D11.Texture2D((SharpDX.Direct3D11.Device)device, desc))
                    //{
                    //    currentScreen = new Texture2dReadView(newTex, device.ResourceFactory.ResourceTracker);
                    //}
                }

                device.ImmediateContext.CopyResource(newTexture, _currentScreen);
            }

            _dup.ReleaseFrame();
        }

        Utilities.Dispose(ref newScreenResource);

        TextureOutput.Value = _currentScreen;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        Utilities.Dispose(ref _currentScreen);
        Utilities.Dispose(ref _dup);
    }

    private OutputDuplication? _dup;
    private int _currentScreenIndex = -1;
    private Texture2D? _currentScreen;

    [Input(Guid = "633199D4-1950-48E3-ACE9-0E266C7E5771")]
    public readonly InputSlot<int> ScreenIndex = new();

    [Input(Guid = "9ACD5B21-CAC7-4A1E-B7A8-1552AD0A907D")]
    public readonly InputSlot<int> TimeOut = new();
}