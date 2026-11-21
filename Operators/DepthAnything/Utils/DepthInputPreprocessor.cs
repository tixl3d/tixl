#nullable enable
using T3.Graphics;
using T3.Graphics.Compat;

namespace Lib.io.video.depthanything.Utils;

/// <summary>
/// Turns a texture of any size and format into the model's input tensor. The resampling and normalization run on
/// the GPU and only the model-sized result is read back, without ever blocking the render thread.
/// </summary>
internal sealed class DepthInputPreprocessor : IDisposable
{
    public enum ReadbackState
    {
        Pending,
        Ready,
        TimedOut,
    }

    /// <summary>The tensor layout a model takes.</summary>
    public enum InputLayout
    {
        /// <summary>float32 NCHW, ImageNet-normalized.</summary>
        PlanarFloat,

        /// <summary>uint8 NHWC, for models that normalize inside their graph.</summary>
        PackedBytes,
    }

    public bool IsPending { get; private set; }

    public string? Error => _floatPass.Error ?? _bytePass.Error;

    /// <summary>
    /// Queues the resampling of <paramref name="source"/> to the model size and the copy of the result into a
    /// staging texture.
    /// </summary>
    public bool TryBegin(Texture2D source, int modelWidth, int modelHeight, InputLayout layout, int padRows = 0)
    {
        var parameters = new Parameters
                             {
                                 ModelWidth = (uint)modelWidth,
                                 ModelHeight = (uint)modelHeight,
                                 LinearSource = IsLinearEncoded(source.Description.Format) ? 1u : 0u,
                                 Padding = layout == InputLayout.PackedBytes ? 0u : (uint)padRows,
                             };

        // The float planes are stacked into one texture: red, green, then blue, each ModelHeight rows
        var packed = layout == InputLayout.PackedBytes;
        var pass = packed ? _bytePass : _floatPass;
        var height = packed ? modelHeight : modelHeight * 3;
        var format = packed ? Format.R8G8B8A8_UNorm : Format.R32_Float;
        if (!pass.TryRun(parameters, source, null, modelWidth, height, format))
            return false;

        EnsureStaging(modelWidth, height, format);
        ResourceManager.Device.ImmediateContext.CopyResource(pass.Output!, _staging!);

        _width = modelWidth;
        _height = height;
        IsPending = true;
        _waitFrames = 0;
        return true;
    }

    /// <summary>
    /// Copies the float planes into <paramref name="destination"/> once the GPU finished them. Polls, so a call
    /// that isn't ready yet costs nothing.
    /// </summary>
    public unsafe ReadbackState TryComplete(float[] destination)
    {
        var state = TryMap(out var mapped);
        if (state != ReadbackState.Ready)
            return state;

        try
        {
            if (mapped.Data == null || destination == null || destination.Length < _width * _height)
                return ReadbackState.TimedOut;

            var rowBytes = _width * sizeof(float);
            unsafe
            {
                var sourcePointer = (byte*)mapped.Data;
                fixed (float* destinationPointer = destination)
                {
                    for (var row = 0; row < _height; row++)
                    {
                        System.Buffer.MemoryCopy(sourcePointer + (long)row * mapped.RowPitch, destinationPointer + (long)row * _width, rowBytes, rowBytes);
                    }
                }
            }
        }
        finally
        {
            ResourceManager.Device.ImmediateContext.UnmapSubresource(_staging!, 0);
        }

        return ReadbackState.Ready;
    }

    /// <summary>Copies the pixels into <paramref name="destination"/> as packed RGB, dropping alpha.</summary>
    public unsafe ReadbackState TryComplete(byte[] destination)
    {
        var state = TryMap(out var mapped);
        if (state != ReadbackState.Ready)
            return state;

        try
        {
            if (mapped.Data == null || destination == null || destination.Length < 3 * _width * _height)
                return ReadbackState.TimedOut;

            unsafe
            {
                var sourcePointer = (byte*)mapped.Data;
                fixed (byte* destinationPointer = destination)
                {
                    var target = destinationPointer;
                    for (var row = 0; row < _height; row++)
                    {
                        var pixel = sourcePointer + (long)row * mapped.RowPitch;
                        for (var column = 0; column < _width; column++)
                        {
                            target[0] = pixel[0];
                            target[1] = pixel[1];
                            target[2] = pixel[2];
                            target += 3;
                            pixel += 4;
                        }
                    }
                }
            }
        }
        finally
        {
            ResourceManager.Device.ImmediateContext.UnmapSubresource(_staging!, 0);
        }

        return ReadbackState.Ready;
    }

    public void Cancel()
    {
        IsPending = false;
    }

    public void Dispose()
    {
        IsPending = false;
        _floatPass.Dispose();
        _bytePass.Dispose();
        _staging?.Dispose();
        _staging = null;
    }

    /// <summary>Maps the staging texture once the GPU finished the copy; leaves it mapped on <see cref="ReadbackState.Ready"/>.</summary>
    private unsafe ReadbackState TryMap(out MappedMemory mapped)
    {
        mapped = default;
        if (!IsPending || _staging == null)
            return ReadbackState.Pending;

        T3.Graphics.Compat.Texture2D nativeStaging = _staging;
        var mappedOk = nativeStaging.Native != null
                       && ResourceManager.Device.Backend.TryMapForRead(nativeStaging.Native, 0, out mapped);

        // A successful map with a null data pointer would otherwise crash the copy; unmap it again.
        if (mappedOk && mapped.Data == null)
        {
            try
            {
                ResourceManager.Device.ImmediateContext.UnmapSubresource(nativeStaging, 0);
            }
            catch
            {
                // Not mapped after all - the retry below is still the right response.
            }
        }

        if (!mappedOk || mapped.Data == null)
        {
            _waitFrames++;
            if (_waitFrames <= ReadbackTimeoutFrames)
                return ReadbackState.Pending;

            // A device reset can leave the copy unfinished forever
            IsPending = false;
            return ReadbackState.TimedOut;
        }

        IsPending = false;
        return ReadbackState.Ready;
    }

    /// <summary>An *_SRgb view returns linear values, but the models were trained on encoded ones.</summary>
    private static bool IsLinearEncoded(Format format)
    {
        return format is Format.R8G8B8A8_UNorm_SRgb or Format.B8G8R8A8_UNorm_SRgb;
    }

    private void EnsureStaging(int width, int height, Format format)
    {
        if (_staging is { IsDisposed: false } && _staging.Description.Width == width && _staging.Description.Height == height
            && _staging.Description.Format == format)
            return;

        _staging?.Dispose();
        _staging = Texture2D.CreateTexture2D(new Texture2DDescription
                                                 {
                                                     Width = width,
                                                     Height = height,
                                                     MipLevels = 1,
                                                     ArraySize = 1,
                                                     Format = format,
                                                     SampleDescription = new SampleDescription(1, 0),
                                                     Usage = ResourceUsage.Staging,
                                                     BindFlags = BindFlags.None,
                                                     CpuAccessFlags = CpuAccessFlags.Read,
                                                     OptionFlags = ResourceOptionFlags.None,
                                                 });
    }

    // Padding rounds the constant buffer up to whole 16-byte rows
#pragma warning disable CS0649
    private struct Parameters
    {
        public uint ModelWidth;
        public uint ModelHeight;
        public uint LinearSource;
        public uint Padding;
    }
#pragma warning restore CS0649

    private const int ReadbackTimeoutFrames = 60;

    private readonly ComputePass _floatPass = new("DepthAnything:shaders/depth-preprocess-cs.hlsl");
    private readonly ComputePass _bytePass = new("DepthAnything:shaders/depth-preprocess-u8-cs.hlsl");
    private Texture2D? _staging;
    private int _width;
    private int _height;
    private int _waitFrames;
}
