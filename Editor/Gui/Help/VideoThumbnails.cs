#nullable enable
using System.IO;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.WIC;
using T3.Core.Resource;

namespace T3.Editor.Gui.Help;

/// <summary>
/// Loads referenced video thumbnails (<c>.help/.tmp/video-thumbnails/&lt;id&gt;.jpg</c>) into GPU textures for the
/// help resource tooltips and caches the shader-resource view per id for the session. Decoding happens on the
/// first request for a video (a few ms); a missing or unreadable file is remembered so it isn't retried.
/// </summary>
internal static class VideoThumbnails
{
    /// <summary>Resolves the ImGui texture handle for the video's thumbnail, or false when none is available.</summary>
    internal static bool TryGetTexture(string videoId, out IntPtr textureId, out float aspectRatio)
    {
        if (!_cache.TryGetValue(videoId, out var entry))
        {
            entry = Load(videoId);
            _cache[videoId] = entry;
        }

        textureId = entry.TextureId;
        aspectRatio = entry.AspectRatio;
        return entry.TextureId != IntPtr.Zero;
    }

    private static Entry Load(string videoId)
    {
        var path = Path.Combine(ShippedContent.ResolveDirectory(".help", ".tmp", "video-thumbnails"), videoId + ".jpg");
        if (!File.Exists(path))
            return Entry.Missing;

        try
        {
            using var factory = new ImagingFactory();
            using var decoder = new BitmapDecoder(factory, path, DecodeOptions.CacheOnDemand);
            using var frame = decoder.GetFrame(0);
            using var converter = new FormatConverter(factory);
            converter.Initialize(frame, PixelFormat.Format32bppRGBA);

            var width = converter.Size.Width;
            var height = converter.Size.Height;
            var stride = width * 4;
            using var buffer = new DataStream(height * stride, true, true);
            converter.CopyPixels(stride, buffer);

            // Texture and SRV are held for the session: the native pointer must stay valid while ImGui draws it.
            var texture = new Texture2D(ResourceManager.Device,
                                        new Texture2DDescription
                                            {
                                                Width = width,
                                                Height = height,
                                                ArraySize = 1,
                                                BindFlags = BindFlags.ShaderResource,
                                                Usage = ResourceUsage.Immutable,
                                                Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                                                MipLevels = 1,
                                                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                                            },
                                        new DataRectangle(buffer.DataPointer, stride));

            var srv = SrvManager.GetSrvForTexture(texture);
            if (srv == null)
                return Entry.Missing;

            return new Entry(srv.NativePointer, height == 0 ? DefaultAspect : (float)width / height, texture);
        }
        catch (Exception e)
        {
            Log.Debug($"Could not load video thumbnail '{videoId}': {e.Message}");
            return Entry.Missing;
        }
    }

    private const float DefaultAspect = 16f / 9f;

    private readonly record struct Entry(IntPtr TextureId, float AspectRatio, Texture2D? Texture)
    {
        internal static readonly Entry Missing = new(IntPtr.Zero, DefaultAspect, null);
    }

    private static readonly Dictionary<string, Entry> _cache = new();
}
