using T3.Core.Animation;

namespace Lib.io.video.mediapipe;

/// <summary>
/// Reads a source texture back once per evaluation frame and hands every MediaPipe operator the same
/// downsampled pixels.
/// <para>
/// Each operator used to do its own <c>CopyResource</c> + <c>MapSubresource</c> + downsample. Measured on
/// a 1920x1080 source that is ~2.0 ms per operator, and the cost is dominated by the mapped readback
/// itself rather than by the pixel count (640x360 still costs 1.2 ms) - so seven operators sharing one
/// camera paid that ~14 ms every frame for byte-identical data. Sharing the readback pays it once and
/// leaves a per-operator memcpy.
/// </para>
/// <para>
/// Validity is keyed on the evaluation frame (<see cref="Playback.FrameCount"/>, the same token the audio
/// engine uses to detect a new frame), so a frame's pixels are computed once and never reused after the
/// graph advances. The returned array is shared and must be treated as read-only: callers copy it into
/// their own pooled buffer, which keeps the existing per-operator buffer lifetime untouched.
/// </para>
/// </summary>
internal static class MediaPipeFrameCache
{
    private sealed class Entry
    {
        public int Frame;
        public int MaxWidth;
        public byte[]? Rgb;
        public byte[]? Rgba;
        public int Width;
        public int Height;
        public string Error = string.Empty;
    }

    private static readonly object _lock = new();
    private static readonly Dictionary<T3.Core.DataTypes.Texture2D, Entry> _entries = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Returns the input texture downsampled to at most <paramref name="maxWidth"/> pixels wide, with
    /// <paramref name="bytesPerPixel"/> channels (3 = RGB, 4 = RGBA). The array is owned by the cache and
    /// is only valid for the current evaluation frame.
    /// </summary>
    public static bool TryGet(T3.Core.DataTypes.Texture2D texture, int maxWidth, int bytesPerPixel,
                              out byte[] pixels, out int width, out int height, out string error)
    {
        var frame = Playback.FrameCount;

        lock (_lock)
        {
            PruneStaleFrames(frame);

            if (!_entries.TryGetValue(texture, out var entry))
            {
                entry = new Entry();
                _entries[texture] = entry;
            }

            if (entry.Frame != frame || entry.MaxWidth != maxWidth)
            {
                entry.Frame = frame;
                entry.MaxWidth = maxWidth;
                entry.Error = string.Empty;

                if (!Readback(texture, maxWidth, entry))
                {
                    if (entry.Error.Length == 0)
                        entry.Error = "texture readback failed"; // format-guard failures return without a message; the same-frame gate below keys on a non-empty Error
                    pixels = [];
                    width = height = 0;
                    error = entry.Error;
                    return false;
                }
            }
            else if (entry.Error.Length > 0)
            {
                // This frame's readback already failed; later same-frame callers get the same failure instead of stale or null pixels
                pixels = [];
                width = height = 0;
                error = entry.Error;
                return false;
            }

            if (bytesPerPixel == 4)
            {
                pixels = entry.Rgba!;
            }
            else
            {
                entry.Rgb ??= ToRgb(entry.Rgba!, entry.Width, entry.Height);
                pixels = entry.Rgb;
            }

            width = entry.Width;
            height = entry.Height;
            error = string.Empty;
            return true;
        }
    }

    /// <summary>Drops entries from earlier frames so a graph that switches cameras does not accumulate them.</summary>
    private static void PruneStaleFrames(int frame)
    {
        if (_entries.Count == 0)
            return;

        List<T3.Core.DataTypes.Texture2D>? stale = null;
        foreach (var pair in _entries)
        {
            if (pair.Value.Frame != frame)
                (stale ??= []).Add(pair.Key);
        }

        if (stale == null)
            return;

        foreach (var key in stale)
            _entries.Remove(key);
    }

    /// <summary>Copy + map + nearest-neighbour downsample into the entry's RGBA-sized working array.</summary>
    private static bool Readback(T3.Core.DataTypes.Texture2D texture, int maxWidth, Entry entry)
    {
        if (!MediaPipeTextureReadback.CheckFormat(texture.Description.Format, ref _warnedFormat,
                                                  message => Log.Warning(message), nameof(MediaPipeFrameCache)))
            return false;

        var device = ResourceManager.Device;
        var desc = texture.Description;
        var sourceWidth = desc.Width;
        var sourceHeight = desc.Height;

        var downscale = Math.Min(1.0f, (float)maxWidth / sourceWidth);
        var width = Math.Max(1, (int)(sourceWidth * downscale));
        var height = Math.Max(1, (int)(sourceHeight * downscale));

        var staging = GetStaging(device, sourceWidth, sourceHeight, desc.Format);
        if (!MediaPipeTextureReadback.TryMap(device.ImmediateContext, texture, staging, out var dataBox, out var error))
        {
            entry.Error = error;
            return false;
        }

        try
        {
            if (!MediaPipeTextureReadback.RowPitchFits(dataBox, sourceWidth))
            {
                entry.Error = $"row pitch {dataBox.RowPitch} cannot hold a {sourceWidth}px row";
                return false;
            }

            // Reuse the previous frame's array when the size is unchanged: at 60 fps a fresh 0.9 MB
            // pinned allocation per frame is avoidable churn on a path that must not jitter.
            var needed = width * height * 4;
            var rgba = entry.Rgba is { Length: > 0 } existing && existing.Length == needed
                           ? existing
                           : GC.AllocateUninitializedArray<byte>(needed, pinned: true);
            entry.Rgb = null; // derived per stride; rebuilt lazily below
            unsafe
            {
                var src = dataBox.DataPointer;
                var rowPitch = dataBox.RowPitch;
                fixed (byte* dst = rgba)
                {
                    var destination = (IntPtr)dst;
                    var xStep = (float)sourceWidth / width;
                    var yStep = (float)sourceHeight / height;

                    // Bounded: several operators and editor instances may convert in the same frame.
                    Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = MaxDownscaleThreads }, y =>
                    {
                        var rowSrc = (byte*)src + (int)(y * yStep) * rowPitch;
                        var rowDst = (byte*)destination + y * width * 4;
                        for (var x = 0; x < width; x++)
                        {
                            var px = (int)(x * xStep) * 4;
                            rowDst[x * 4] = rowSrc[px];
                            rowDst[x * 4 + 1] = rowSrc[px + 1];
                            rowDst[x * 4 + 2] = rowSrc[px + 2];
                            rowDst[x * 4 + 3] = rowSrc[px + 3];
                        }
                    });
                }
            }

            entry.Rgba = rgba;
            entry.Width = width;
            entry.Height = height;
            return true;
        }
        finally
        {
            device.ImmediateContext.UnmapSubresource(staging, 0);
        }
    }

    private static byte[] ToRgb(byte[] rgba, int width, int height)
    {
        var rgb = GC.AllocateUninitializedArray<byte>(width * height * 3, pinned: true);
        for (int src = 0, dst = 0; dst < rgb.Length; src += 4, dst += 3)
        {
            rgb[dst] = rgba[src];
            rgb[dst + 1] = rgba[src + 1];
            rgb[dst + 2] = rgba[src + 2];
        }

        return rgb;
    }

    // ------------------------------------------------------------------ staging textures

    private const int MaxCachedStaging = 4;
    private static readonly int MaxDownscaleThreads = Math.Clamp(Environment.ProcessorCount, 1, 8);
    private static readonly object _stagingLock = new();
    private static readonly Dictionary<(int w, int h, T3.Graphics.Format f), T3.Graphics.Compat.Texture2D> _staging = new();
    private static T3.Graphics.Format? _warnedFormat;

    private static T3.Graphics.Compat.Texture2D GetStaging(T3.Graphics.Compat.Device device, int width, int height,
                                                          T3.Graphics.Format format)
    {
        lock (_stagingLock)
        {
            if (_staging.TryGetValue((width, height, format), out var cached) && cached != null)
                return cached;

            if (_staging.Count >= MaxCachedStaging)
            {
                foreach (var pair in _staging)
                    pair.Value?.Dispose();
                _staging.Clear();
            }

            var texture = new T3.Graphics.Compat.Texture2D(device, new T3.Graphics.Compat.Texture2DDescription
            {
                Width = width, Height = height, MipLevels = 1, ArraySize = 1,
                Format = format,
                SampleDescription = new T3.Graphics.SampleDescription(1, 0),
                Usage = T3.Graphics.Compat.ResourceUsage.Staging,
                BindFlags = T3.Graphics.Compat.BindFlags.None,
                CpuAccessFlags = T3.Graphics.Compat.CpuAccessFlags.Read,
                OptionFlags = T3.Graphics.Compat.ResourceOptionFlags.None
            });
            _staging[(width, height, format)] = texture;
            return texture;
        }
    }
}
