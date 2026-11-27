using Clockworks.CLX;
using Clx.Utils;
using T3.Core.Animation;
using T3.Graphics.Compat;

namespace Lib.io.clx;

[Guid("7dc0e3a4-cc8a-4034-95a6-3ea2ae5e620d")]
internal sealed class ClxWaveform : Instance<ClxWaveform>, IStatusProvider
{
    [Output(Guid = "08e0f347-7f74-4bb5-831d-82dfe7f3513a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Texture = new();

    [Output(Guid = "111beb7b-9c28-4425-9da5-a028defc01ff", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Duration = new();

    [Output(Guid = "d012161e-5d69-458f-84c2-ce1beb2d74c9", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> HasWaveform = new();

    public ClxWaveform()
    {
        Texture.UpdateAction += Update;
        Duration.UpdateAction += Update;
        HasWaveform.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Playback.FrameCount == _lastFrame)
            return;

        _lastFrame = Playback.FrameCount;

        var width = Math.Clamp(Width.GetValue(context), 1, 16384);
        var height = Math.Clamp(Height.GetValue(context), 1, 4096);
        var color = Color.GetValue(context);
        var client = _binding.Update(Port.GetValue(context), Listen.GetValue(context));
        var waveform = client?.GetWaveform(Deck.GetValue(context));

        HasWaveform.Value = waveform != null;
        Duration.Value = (float)(waveform?.Duration ?? 0);

        if (waveform == _lastWaveform && width == _lastWidth && height == _lastHeight && color == _lastColor && Texture.Value != null)
            return;

        _lastWaveform = waveform;
        _lastWidth = width;
        _lastHeight = height;
        _lastColor = color;

        Render(waveform, width, height, color);
        Upload(width, height);
    }

    /// <summary>
    /// Draws the whole track left to right on a transparent background, one column per pixel, in the
    /// waveform's own colours or <paramref name="fallback"/> when it has none.
    /// </summary>
    private void Render(CLXWaveform waveform, int width, int height, Vector4 fallback)
    {
        var byteCount = width * height * 4;
        if (_pixels.Length != byteCount)
        {
            if (_pixelsHandle.IsAllocated)
                _pixelsHandle.Free();

            _pixels = new byte[byteCount];
            _pixelsHandle = GCHandle.Alloc(_pixels, GCHandleType.Pinned);
        }
        else
        {
            Array.Clear(_pixels);
        }

        if (waveform == null || waveform.Length <= 0)
            return;

        var fallbackR = ToByte(fallback.X);
        var fallbackG = ToByte(fallback.Y);
        var fallbackB = ToByte(fallback.Z);
        var alpha = ToByte(fallback.W);
        var middle = (height - 1) * 0.5f;

        for (var x = 0; x < width; x++)
        {
            var first = (int)((long)x * waveform.Length / width);
            var last = Math.Max(first + 1, (int)((long)(x + 1) * waveform.Length / width));
            last = Math.Min(last, waveform.Length);

            float min = 0, max = 0;
            int r = 0, g = 0, b = 0;
            for (var pixel = first; pixel < last; pixel++)
            {
                for (var channel = 0; channel < waveform.Channels; channel++)
                {
                    waveform.GetPeak(pixel, channel, out var low, out var high);
                    min = Math.Min(min, low);
                    max = Math.Max(max, high);
                }

                if (waveform.HasColors)
                {
                    r += waveform.Colors[pixel * 3];
                    g += waveform.Colors[pixel * 3 + 1];
                    b += waveform.Colors[pixel * 3 + 2];
                }
            }

            var count = Math.Max(1, last - first);
            var colorR = waveform.HasColors ? (byte)(r / count) : fallbackR;
            var colorG = waveform.HasColors ? (byte)(g / count) : fallbackG;
            var colorB = waveform.HasColors ? (byte)(b / count) : fallbackB;

            // Row 0 is the top of the texture, so positive peaks go up from the middle.
            var top = Math.Clamp((int)MathF.Round(middle - max * middle), 0, height - 1);
            var bottom = Math.Clamp((int)MathF.Round(middle - min * middle), 0, height - 1);
            for (var y = top; y <= bottom; y++)
            {
                var o = (y * width + x) * 4;
                _pixels[o] = colorR;
                _pixels[o + 1] = colorG;
                _pixels[o + 2] = colorB;
                _pixels[o + 3] = alpha;
            }
        }
    }

    private void Upload(int width, int height)
    {
        if (Texture.Value == null || Texture.Value.Description.Width != width || Texture.Value.Description.Height != height)
        {
            if (Texture.Value != null)
                GraphicsUtilities.Dispose(ref Texture.Value);

            Texture.Value = Texture2D.CreateTexture2D(new Texture2DDescription
                                                          {
                                                              Width = width,
                                                              Height = height,
                                                              ArraySize = 1,
                                                              BindFlags = BindFlags.ShaderResource,
                                                              Usage = ResourceUsage.Default,
                                                              MipLevels = 1,
                                                              CpuAccessFlags = CpuAccessFlags.None,
                                                              Format = Format.R8G8B8A8_UNorm,
                                                              SampleDescription = new SampleDescription(1, 0),
                                                          });
        }

        var rowPitch = width * 4;
        var dataBox = new DataBox(_pixelsHandle.AddrOfPinnedObject(), rowPitch, rowPitch * height);
        ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, Texture.Value, 0);
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _binding.Release();
        if (Texture.Value != null)
            GraphicsUtilities.Dispose(ref Texture.Value);

        if (_pixelsHandle.IsAllocated)
            _pixelsHandle.Free();
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _binding.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _binding.GetStatusMessage();

    private readonly ClxBinding _binding = new();
    private byte[] _pixels = [];
    private GCHandle _pixelsHandle;
    private CLXWaveform _lastWaveform;
    private int _lastWidth;
    private int _lastHeight;
    private Vector4 _lastColor;
    private int _lastFrame = -1;

    [Input(Guid = "b41fd5d6-3b33-427d-bde9-b6672ff3c994")]
    public readonly InputSlot<int> Deck = new();

    [Input(Guid = "ccd0acfd-0c15-4518-8f77-24e5cb7ae260")]
    public readonly InputSlot<int> Width = new();

    [Input(Guid = "61acac5d-4db3-4091-9688-0e098e75d483")]
    public readonly InputSlot<int> Height = new();

    [Input(Guid = "29895470-e52c-42ac-b864-2e7476b64ac5")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "883a3a8d-c4d2-4a17-8bd2-7164a75bee6a")]
    public readonly InputSlot<int> Port = new();

    [Input(Guid = "379d6e7c-fc43-4f2d-a826-5f0b21b47ab2")]
    public readonly InputSlot<bool> Listen = new();
}
