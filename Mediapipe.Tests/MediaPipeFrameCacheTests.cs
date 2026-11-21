using Lib.io.video.mediapipe;
using T3.Core.Animation;
using T3.Core.Resource;
using T3.Graphics;
using T3.Graphics.Compat;
using Xunit;
using CompatTexture = T3.Graphics.Compat.Texture2D;
using EngineTexture = T3.Core.DataTypes.Texture2D;

namespace Mediapipe.Tests;

/// <summary>
/// The shared-frame cache is what keeps seven operators on one camera from paying for seven readbacks,
/// so its contract matters: identical pixels to a single-operator readback, one array shared within a
/// frame, and a rebuild once the graph advances.
/// </summary>
public class MediaPipeFrameCacheTests
{
    private const int Size = 4;

    private static Device? _device;

    private static Device? TryGetDevice()
    {
        if (_device != null)
            return _device;

        // The readback path is D3D11 in the tests; elsewhere the cache is exercised by the real editors.
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            _device = new Device(T3.Graphics.D3D11.D3D11Backend.Create());
            ResourceManager.Init(_device);
            return _device;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static EngineTexture CreateSource(Device device, byte r, byte g, byte b, byte a)
    {
        var texture = new EngineTexture(new CompatTexture(device, new Texture2DDescription
        {
            Width = Size, Height = Size, MipLevels = 1, ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            OptionFlags = ResourceOptionFlags.None
        }));

        Upload(device, texture, r, g, b, a);
        return texture;
    }

    private static void Upload(Device device, EngineTexture texture, byte r, byte g, byte b, byte a)
    {
        var data = GC.AllocateUninitializedArray<byte>(Size * Size * 4, pinned: true);
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = r;
            data[i + 1] = g;
            data[i + 2] = b;
            data[i + 3] = a;
        }

        unsafe
        {
            fixed (byte* p = data)
            {
                device.ImmediateContext.UpdateSubresource(new DataBox((IntPtr)p, Size * 4, 0), texture, 0);
            }
        }
    }

    [Fact]
    public void ReturnsTheSourcePixelsAndSharesWithinAFrame()
    {
        var device = TryGetDevice();
        if (device == null)
            return;

        Playback.FrameCount = 1;
        using var source = CreateSource(device, 10, 20, 30, 40);

        Assert.True(MediaPipeFrameCache.TryGet(source, Size, 3, out var rgb, out var w, out var h, out var error), error);
        Assert.Equal(Size, w);
        Assert.Equal(Size, h);
        Assert.Equal(10, rgb[0]);
        Assert.Equal(20, rgb[1]);
        Assert.Equal(30, rgb[2]);

        // Same frame, same texture, same stride: the same array - the second operator spends no readback.
        Assert.True(MediaPipeFrameCache.TryGet(source, Size, 3, out var rgbAgain, out _, out _, out _));
        Assert.Same(rgb, rgbAgain);

        // The four-channel variant is a separate (derived) array of the same frame's pixels; asking twice
        // returns that same one, so the readback is still shared between operators that want either stride.
        Assert.True(MediaPipeFrameCache.TryGet(source, Size, 4, out var rgba, out _, out _, out error), error);
        Assert.Equal(40, rgba[3]);
        Assert.True(MediaPipeFrameCache.TryGet(source, Size, 4, out var rgbaAgain, out _, out _, out _));
        Assert.Same(rgba, rgbaAgain);
    }

    [Fact]
    public void RebuildsWhenTheFrameAdvances()
    {
        var device = TryGetDevice();
        if (device == null)
            return;

        Playback.FrameCount = 10;
        using var source = CreateSource(device, 1, 2, 3, 4);

        Assert.True(MediaPipeFrameCache.TryGet(source, Size, 4, out var first, out _, out _, out _));
        Assert.Equal(1, first[0]);

        // New frame, new content: the cache must not serve last frame's pixels.
        Playback.FrameCount = 11;
        Upload(device, source, 200, 201, 202, 203);

        Assert.True(MediaPipeFrameCache.TryGet(source, Size, 4, out var second, out _, out _, out _));
        Assert.NotSame(first, second);
        Assert.Equal(200, second[0]);
        Assert.Equal(203, second[3]);
    }

    [Fact]
    public void DownscalesToTheRequestedWidth()
    {
        var device = TryGetDevice();
        if (device == null)
            return;

        Playback.FrameCount = 20;
        using var source = CreateSource(device, 5, 6, 7, 8);

        // Half width: 4 -> 2, so the frame is 2x2 and every pixel still comes from the source.
        Assert.True(MediaPipeFrameCache.TryGet(source, 2, 3, out var rgb, out var w, out var h, out var error), error);
        Assert.Equal(2, w);
        Assert.Equal(2, h);
        Assert.Equal(2 * 2 * 3, rgb.Length);
        Assert.All(new[] { 0, 3, 6, 9 }, i => Assert.Equal(5, rgb[i]));
    }
}
