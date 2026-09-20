#nullable enable
using T3.Graphics;
using T3.Graphics.Compat;
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
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _dup);
            _currentScreenIndex = screenIndex;
        }

        if (_dup == null)
        {
            using var nativeDevice = new SharpDX.Direct3D11.Device(device.NativePointer);
            System.Runtime.InteropServices.Marshal.AddRef(device.NativePointer);
            using var dxgiDevice = nativeDevice.QueryInterface<SharpDX.DXGI.Device>();
            
            var dxgiAdapter = dxgiDevice.GetParent<SharpDX.DXGI.Adapter>();

            var output = dxgiAdapter.GetOutput(Utilities.InfiniteModIndexer(screenIndex, dxgiAdapter.GetOutputCount()));

            using var o1 = output.QueryInterface<SharpDX.DXGI.Output1>();
            _dup = o1.DuplicateOutput(nativeDevice);
        }

        var capResult = _dup.TryAcquireNextFrame(timeOut, out _, out var newScreenResource);

        if (capResult.Success)
        {
            using (var nativeTexture = newScreenResource.QueryInterface<SharpDX.Direct3D11.Texture2D>())
            {
                // The duplicated frame is a D3D11 texture DXGI owns; copy it into one of ours.
                var captured = nativeTexture.Description;

                if (_currentScreen != null
                    && (_currentScreen.Description.Width != captured.Width
                        || _currentScreen.Description.Height != captured.Height
                        || (int)_currentScreen.Description.Format != (int)captured.Format))
                {
                    T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _currentScreen);
                }

                _currentScreen ??= Texture2D.CreateTexture2D(new T3.Graphics.Compat.Texture2DDescription
                                                                 {
                                                                     Width = captured.Width,
                                                                     Height = captured.Height,
                                                                     MipLevels = captured.MipLevels,
                                                                     ArraySize = captured.ArraySize,
                                                                     Format = (T3.Graphics.Format)captured.Format,
                                                                     SampleDescription = new SampleDescription(captured.SampleDescription.Count,
                                                                                                               captured.SampleDescription.Quality),
                                                                     BindFlags = T3.Graphics.Compat.BindFlags.ShaderResource,
                                                                     Usage = T3.Graphics.Compat.ResourceUsage.Default,
                                                                 });

                var adopted = device.AdoptTexture(nativeTexture.NativePointer, _currentScreen.Description);

                if (adopted != null)
                    device.ImmediateContext.CopyResource(adopted, _currentScreen);
            }

            _dup.ReleaseFrame();
        }

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref newScreenResource);

        TextureOutput.Value = _currentScreen;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _currentScreen);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _dup);
    }

    private SharpDX.DXGI.OutputDuplication? _dup;
    private int _currentScreenIndex = -1;
    private Texture2D? _currentScreen;

    [Input(Guid = "633199D4-1950-48E3-ACE9-0E266C7E5771")]
    public readonly InputSlot<int> ScreenIndex = new();

    [Input(Guid = "9ACD5B21-CAC7-4A1E-B7A8-1552AD0A907D")]
    public readonly InputSlot<int> TimeOut = new();
}