#nullable enable

using System.IO;
using System.Threading.Tasks;
using ImGuiNET;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using SharpDX.WIC;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.UserData;
using T3.Editor.Gui.Windows.AssetLib;
using T3.Editor.Gui.Windows.RenderExport;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.UiHelpers.Thumbnails;

/// <summary>
/// Manages the generation, caching, and rendering of preview thumbnails for symbols, presets, and assets.
/// 
/// For optimal performance, thumbnails are packed into a single 4K GPU atlas. This minimizes draw calls 
/// and texture swaps during UI rendering. If the atlas is full, a Least Recently Used (LRU) policy 
/// evicts the oldest entries to make room for new requests.
/// 
/// Caching Strategy:
/// - Temporary: Regeneratable thumbnails (e.g., asset files) are cached in the user's AppData Tmp folder.
/// - Persistent: Curated content (e.g., symbol examples) is stored in the project's .meta folder.
/// - Runtime-only: Previews can be pushed directly to the atlas without disk serialization.
/// 
/// To prevent excessive I/O, the manager maintains a persistent 'negative cache' of missing files,
/// ensuring that non-existent thumbnails are only checked once per session.
/// </summary>
internal static class ThumbnailManager
{
    #region Main Thread Methods
    /// <summary>
    /// Processes the upload queue to copy textures into the atlas and updates slot states.
    /// </summary>
    internal static void Update()
    {
        if (!_initialized)
            Initialize();

        lock (_uploadQueue)
        {
            var deviceContext = ResourceManager.Device.ImmediateContext;
            while (_uploadQueue.Count > 0)
            {
                var upload = _uploadQueue.Dequeue();

                var destRegion = new ResourceRegion
                                     {
                                         Left = upload.Slot.X * SlotWidth + Padding,
                                         Top = upload.Slot.Y * SlotHeight + Padding,
                                         Right = upload.Slot.X * SlotWidth + SlotWidth - Padding,
                                         Bottom = upload.Slot.Y * SlotHeight + SlotHeight - Padding,
                                         Front = 0, Back = 1
                                     };

                // Fast GPU copy to atlas
                deviceContext.CopySubresourceRegion(upload.Texture, 0, null, _atlas!, 0, destRegion.Left, destRegion.Top);

                if (_slots.TryGetValue(upload.Guid, out var slot))
                {
                    slot.State = LoadingState.Ready;
                }

                upload.Texture.Dispose();
            }
        }
    }

    internal static bool AsImguiImage(this ThumbnailRect thumbnail, float height = SlotHeight)
    {
        if (!thumbnail.IsReady || AtlasSrv == null)
            return false;

        ImGui.Image(AtlasSrv.NativePointer, new Vector2(height * 4 / 3, height), thumbnail.UvMin, thumbnail.UvMax);
        return true;
    }
    #endregion

    #region Data Request
    /// <summary>
    /// Hot-path for assets. Uses Guid keys to avoid allocations and checks the negative cache to prevent I/O spam.
    /// </summary>
    internal static ThumbnailRect GetThumbnail(Asset asset, IResourcePackage? package, Categories category = Categories.Temp)
    {
        if (asset.AssetType != AssetHandling.Images || package == null || asset.FileSystemInfo == null || asset.Address.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            return _fallback;

        return GetThumbnail(asset.Id, package, category, asset.FileSystemInfo);
    }

    internal static ThumbnailRect GetThumbnail(Guid guid, IResourcePackage? package, Categories category = Categories.Temp, FileSystemInfo? sourceInfo = null)
    {
        if (package == null) return _fallback;

        // 1. Get or Create the persistent state entry
        if (!_slots.TryGetValue(guid, out var slot))
        {
            slot = new ThumbnailSlot { Guid = guid };
            _slots[guid] = slot;
        }

        // 2. Prevent repeated disk checks for non-existent files
        if (slot.State == LoadingState.DoesntExist)
            return _fallback;

        // 3. Return from atlas if ready and assigned a coordinate
        if (slot.State == LoadingState.Ready && slot.X != -1)
        {
            slot.LastUsed = DateTime.Now;
            return GetRectFromSlot(slot);
        }

        // 4. Already in queue
        if (slot.State == LoadingState.Loading)
            return _fallback;

        // 5. Trigger Load or Generate
        var path = Path.Combine(GetPath(package, category), $"{guid}.png");
        if (File.Exists(path))
        {
            RequestAsyncLoad(guid, path);
        }
        else if (sourceInfo != null && sourceInfo.Exists)
        {
            GenerateThumbnailFromAsset(guid, sourceInfo.FullName, package, category);
        }
        else
        {
            // Mark as non-existent to protect against further I/O
            slot.State = LoadingState.DoesntExist;
        }

        return _fallback;
    }
    #endregion

    #region Background Operations
    private static async void RequestAsyncLoad(Guid guid, string path)
    {
        try
        {
            if (!_slots.TryGetValue(guid, out var slot)) return;
        
            slot.State = LoadingState.Loading;
            try
            {
                var targetSlot = AssignAtlasSlot(guid);
                var tex = await LoadTextureViaWic(path);

                if (tex == null)
                {
                    slot.State = LoadingState.DoesntExist;
                    return;
                }

                lock (_uploadQueue)
                {
                    _uploadQueue.Enqueue(new PendingUpload(guid, tex, targetSlot));
                }
            }
            catch (Exception e)
            {
                T3.Core.Logging.Log.Error($"Thumbnail load failed for {guid}: {e.Message}");
                slot.State = LoadingState.DoesntExist;
            }
        }
        catch (Exception e)
        {
            _slots.Remove(guid);
            Log.Warning("Failed to load thumbnail " + e.Message);
        }
    }

    private static async void GenerateThumbnailFromAsset(Guid guid, string sourcePath, IResourcePackage package, Categories category)
    {
        try
        {
            var sourceTexture = await LoadTextureViaWic(sourcePath);
            if (sourceTexture == null) return;

            var t3Texture = new T3.Core.DataTypes.Texture2D(sourceTexture);
            SaveThumbnail(guid, package, t3Texture, category);

            sourceTexture.Dispose();
        }
        catch (Exception e)
        {
            T3.Core.Logging.Log.Error($"Failed generating thumbnail for {guid}: {e.Message}");
        }
    }

    private static async Task<SharpDX.Direct3D11.Texture2D?> LoadTextureViaWic(string path)
    {
        return await Task.Run(async () =>
        {
            int retries = 3;
            while (retries > 0)
            {
                try
                {
                    using var factory = new ImagingFactory();
                    using var decoder = new BitmapDecoder(factory, path, DecodeOptions.CacheOnDemand);
                    using var frame = decoder.GetFrame(0);
                    using var converter = new FormatConverter(factory);

                    converter.Initialize(frame, PixelFormat.Format32bppRGBA);

                    var stride = converter.Size.Width * 4;
                    using var buffer = new SharpDX.DataStream(converter.Size.Height * stride, true, true);
                    converter.CopyPixels(stride, buffer);

                    return new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, new Texture2DDescription()
                    {
                        Width = converter.Size.Width,
                        Height = converter.Size.Height,
                        ArraySize = 1,
                        BindFlags = BindFlags.ShaderResource,
                        Usage = ResourceUsage.Immutable,
                        Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                        MipLevels = 1,
                        SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                    }, new SharpDX.DataRectangle(buffer.DataPointer, stride));
                }
                catch (SharpDXException ex) when ((uint)ex.HResult == 0x80070020)
                {
                    retries--;
                    await Task.Delay(50); // Sharing violation retry
                }
                catch { return null; }
            }
            return null;
        });
    }
    #endregion

    #region Atlas Management (LRU)
    /// <summary>
    /// Manages the physical atlas coordinates. If the atlas is full, it evicts the oldest entry back to NotLoaded status.
    /// </summary>
    private static ThumbnailSlot AssignAtlasSlot(Guid guid)
    {
        var slot = _slots[guid];
        if (slot.X != -1) return slot;

        // Evict volatile GPU slot if at capacity
        if (_atlasLru.Count >= MaxSlots)
        {
            var oldest = _atlasLru.OrderBy(s => s.LastUsed).First();
            oldest.X = -1;
            oldest.Y = -1;
            oldest.State = LoadingState.NotLoaded;
            _atlasLru.Remove(oldest);
        }

        // Find visual index
        int index = _atlasLru.Count;
        slot.X = index % 23;
        slot.Y = index / 23;
        
        slot.LastUsed = DateTime.Now;
        _atlasLru.Add(slot);
        return slot;
    }
    #endregion

    #region GPU / Saving Methods
    internal static void SaveThumbnail(Guid guid, IResourcePackage package, T3.Core.DataTypes.Texture2D sourceTexture, Categories category, bool saveToFile = true)
    {
        if (!_slots.TryGetValue(guid, out var slot))
        {
            slot = new ThumbnailSlot { Guid = guid };
            _slots[guid] = slot;
        }

        slot.State = LoadingState.Loading;
        var targetSlot = AssignAtlasSlot(guid);
        
        var device = ResourceManager.Device;
        var context = device.ImmediateContext;

        var thumbDir = GetPath(package, category);
        try { Directory.CreateDirectory(thumbDir); } catch { return; }
        var filePath = Path.Combine(thumbDir, $"{guid}.png");

        if (saveToFile)
        {
            lock (_lockedPath) { if (!_lockedPath.Add(filePath)) return; }
        }

        const int targetWidth = SlotWidth;
        const int targetHeight = SlotHeight;

        var desc = new Texture2DDescription()
        {
            Width = targetWidth, Height = targetHeight, ArraySize = 1,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm, Usage = ResourceUsage.Default,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0), MipLevels = 1
        };

        using var tempTarget = new SharpDX.Direct3D11.Texture2D(device, desc);
        using var rtv = new RenderTargetView(device, tempTarget);
        var sourceSrv = SrvManager.GetSrvForTexture(sourceTexture);

        var sourceAspect = (float)sourceTexture.Description.Width / sourceTexture.Description.Height;
        var targetAspect = (float)targetWidth / targetHeight;

        // --- Fit Logic (Letterbox/Pillarbox) ---
        float viewWidth, viewHeight, offsetX, offsetY;
        if (sourceAspect > targetAspect) {
            viewWidth = targetWidth; viewHeight = targetWidth / sourceAspect;
            offsetX = 0; offsetY = (targetHeight - viewHeight) / 2f;
        } else {
            viewHeight = targetHeight; viewWidth = targetHeight * sourceAspect;
            offsetX = (targetWidth - viewWidth) / 2f; offsetY = 0;
        }

        context.OutputMerger.SetTargets(rtv);
        context.ClearRenderTargetView(rtv, new RawColor4(0, 0, 0, 0));
        context.Rasterizer.SetViewport(new ViewportF(offsetX, offsetY, viewWidth, viewHeight));

        context.VertexShader.Set(SharedResources.FullScreenVertexShaderResource.Value);
        context.PixelShader.Set(SharedResources.FullScreenPixelShaderResource.Value);
        context.PixelShader.SetShaderResource(0, sourceSrv);

        context.Draw(3, 0);
        context.PixelShader.SetShaderResource(0, null);
        context.OutputMerger.SetTargets((RenderTargetView?)null);

        // Immediate Atlas Queueing
        var uploadTex = new SharpDX.Direct3D11.Texture2D(device, desc);
        context.CopyResource(tempTarget, uploadTex);
        
        lock (_uploadQueue) {
            _uploadQueue.Enqueue(new PendingUpload(guid, uploadTex, targetSlot));
        }

        if (saveToFile) {
            var saveTexture = new T3.Core.DataTypes.Texture2D(tempTarget);
            ScreenshotWriter.StartSavingToFile(saveTexture, filePath, ScreenshotWriter.FileFormats.Png,
                                               path => { if (!string.IsNullOrEmpty(path)) lock (_lockedPath) _lockedPath.Remove(path); }, 
                                               logErrors: false);
        }
    }
    #endregion

    #region Helpers
    private static void Initialize()
    {
        var device = ResourceManager.Device;
        var desc = new Texture2DDescription {
            Width = AtlasSize, Height = AtlasSize, MipLevels = 1, ArraySize = 1,
            Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm, Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource, CpuAccessFlags = CpuAccessFlags.None,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0)
        };

        _atlas = new SharpDX.Direct3D11.Texture2D(device, desc);
        AtlasSrv = new ShaderResourceView(device, _atlas);
        _initialized = true;
    }

    private static ThumbnailRect GetRectFromSlot(ThumbnailSlot slot)
    {
        var x = (float)(slot.X * SlotWidth + Padding) / AtlasSize;
        var y = (float)(slot.Y * SlotHeight + Padding) / AtlasSize;
        var w = (float)(SlotWidth - Padding * 2) / AtlasSize;
        var h = (float)(SlotHeight - Padding * 2) / AtlasSize;
        return new ThumbnailRect(new Vector2(x, y), new Vector2(x + w, y + h), slot.State == LoadingState.Ready);
    }

    private static string GetPath(IResourcePackage package, Categories category)
    {
        return category switch {
            Categories.PackageMeta => Path.Combine(package.Folder, MetaSubFolder, ThumbnailsSubFolder),
            Categories.User        => Path.Combine(FileLocations.TempFolder, ThumbnailsSubFolder, package.Name),
            _                      => Path.Combine(FileLocations.TempFolder, ThumbnailsSubFolder)
        };
    }
    #endregion

    private const int AtlasSize = 4096, Padding = 2, MaxSlots = 500;
    public const int SlotWidth = 178;
    public const int  SlotHeight = 133;
    public const float AspectRatio = (float)SlotWidth / SlotHeight; 
    
    private static SharpDX.Direct3D11.Texture2D? _atlas;
    internal static ShaderResourceView? AtlasSrv { get; private set; }
    private static readonly Dictionary<Guid, ThumbnailSlot> _slots = new();
    private static readonly List<ThumbnailSlot> _atlasLru = new(); 
    private static readonly Queue<PendingUpload> _uploadQueue = new();
    private static readonly ThumbnailRect _fallback = new(Vector2.Zero, Vector2.Zero, false);
    private static bool _initialized;

    internal readonly record struct ThumbnailRect(Vector2 UvMin, Vector2 UvMax, bool IsReady);
    private record struct PendingUpload(Guid Guid, SharpDX.Direct3D11.Texture2D Texture, ThumbnailSlot Slot);
    private enum LoadingState { NotLoaded, Loading, Ready, DoesntExist }

    private sealed class ThumbnailSlot {
        public Guid Guid;
        public LoadingState State = LoadingState.NotLoaded;
        public int X = -1, Y = -1;
        public DateTime LastUsed;
    }

    private static readonly HashSet<string> _lockedPath = [];
    public enum Categories { Temp, User, PackageMeta }
    private const string ThumbnailsSubFolder = "Thumbnails", MetaSubFolder = ".meta";
}