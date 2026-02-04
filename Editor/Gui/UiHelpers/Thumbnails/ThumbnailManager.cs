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
using T3.Editor.Gui.Windows.AssetLib;
using T3.Editor.Gui.Windows.RenderExport;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.UiHelpers.Thumbnails;

internal static class ThumbnailManager
{
    // --- Configuration ---
    private const int AtlasSize = 4096;
    private const int SlotWidth = 178; // Results in ~23 columns
    private const int SlotHeight = 133; // 4:3 ratio
    private const int Padding = 2; // 2px gap to avoid bleeding
    private const int MaxSlots = 500; //

    // --- State ---
    private static SharpDX.Direct3D11.Texture2D? _atlas;
    internal static ShaderResourceView? AtlasSrv { get; private set; }

    private static readonly Dictionary<Guid, ThumbnailSlot> _slots = new();
    private static readonly Queue<PendingUpload> _uploadQueue = new();
    private static readonly ThumbnailRect _waiting = new(Vector2.Zero, Vector2.Zero, false);
    private static bool _initialized;

    // --- Internal Structures ---
    internal readonly record struct ThumbnailRect(Vector2 Min, Vector2 Max, bool IsReady);

    private record struct PendingUpload(Guid Guid, SharpDX.Direct3D11.Texture2D Texture, ThumbnailSlot Slot);

    private static readonly HashSet<Guid> _lockedForWriting = new();

    private sealed class ThumbnailSlot
    {
        public Guid Guid = Guid.Empty;
        public int X;
        public int Y;
        public DateTime LastUsed;
        public bool IsLoading;
    }

    // --- Main Thread Methods ---

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
                    slot.IsLoading = false;

                upload.Texture.Dispose();
            }
        }
    }

    internal static void AsImguiImage(this ThumbnailRect thumbnail, float height = SlotHeight)
    {
        if (!thumbnail.IsReady || AtlasSrv == null)
            return;

        // Ensure 4:3 aspect ratio
        ImGui.Image(AtlasSrv.NativePointer, new Vector2(height * 4 / 3, height), thumbnail.Min, thumbnail.Max);
    }

    // --- Data Request Methods ---

    internal static ThumbnailRect GetThumbnail(Asset asset, IResourcePackage? package)
    {
        if (asset.AssetType != AssetHandling.Images || package == null || asset.FileSystemInfo == null)
            return _waiting;

        // 1. Check atlas cache via Guid (Zero allocation hot-path)
        if (_slots.TryGetValue(asset.Id, out var slot))
        {
            slot.LastUsed = DateTime.Now;
            return GetRectFromSlot(slot);
        }

        // 2. Check for cache file
        var thumbPath = Path.Combine(package.Folder, ".temp", "thumbnails", $"{asset.Id}.png");

        if (File.Exists(thumbPath))
        {
            RequestAsyncLoad(asset.Id, thumbPath);
        }
        else if (asset.FileSystemInfo.Exists)
        {
            // 3. Generate if source exists but cache doesn't
            GenerateThumbnailFromAsset(asset, package);
        }

        return _waiting;
    }

    internal static ThumbnailRect GetThumbnail(Guid guid, IResourcePackage? package)
    {
        if (package == null)
            return _waiting;

        if (_slots.TryGetValue(guid, out var slot))
        {
            slot.LastUsed = DateTime.Now;
            return GetRectFromSlot(slot);
        }

        var path = Path.Combine(package.Folder, ".temp", "thumbnails", $"{guid}.png");
        if (File.Exists(path))
        {
            RequestAsyncLoad(guid, path);
        }

        return _waiting;
    }

    // --- Background Operations ---

    private static async void RequestAsyncLoad(Guid guid, string path)
    {
        try
        {
            var targetSlot = GetLruSlot(guid);
            var tex = await LoadTextureViaWic(path);

            if (tex == null)
            {
                lock (_slots)
                {
                    _slots.Remove(guid);
                }

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
            lock (_slots)
            {
                _slots.Remove(guid);
            }
        }
    }

    private static async void GenerateThumbnailFromAsset(Asset asset, IResourcePackage package)
    {
        try
        {
            // Decode large source on background thread
            var sourceTexture = await LoadTextureViaWic(asset.FileSystemInfo!.FullName);
            if (sourceTexture == null) return;

            // GPU scaling/saving back on main thread implicitly via ScreenshotWriter readback
            var t3Texture = new T3.Core.DataTypes.Texture2D(sourceTexture);
            SaveThumbnail(asset.Id, package, t3Texture);

            sourceTexture.Dispose();
        }
        catch (Exception e)
        {
            T3.Core.Logging.Log.Error($"Failed generating thumbnail for {asset.Address}: {e.Message}");
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
                                          // Attempt to open with Shared Read access
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
                                          // File is locked by ScreenshotWriter, wait and retry
                                          retries--;
                                          await Task.Delay(50);
                                      }
                                      catch
                                      {
                                          return null; // Other decoding errors
                                      }
                                  }

                                  return null;
                              });
    }

    // --- GPU / Saving Methods ---

    internal static void SaveThumbnail(Guid guid, IResourcePackage package, T3.Core.DataTypes.Texture2D sourceTexture)
    {
        var device = ResourceManager.Device;
        var context = device.ImmediateContext;

        var thumbDir = Path.Combine(package.Folder, ".temp", "thumbnails");
        try
        {
            Directory.CreateDirectory(thumbDir);
        }
        catch
        {
            return;
        }

        var filePath = Path.Combine(thumbDir, $"{guid}.png");

        const int targetWidth = SlotWidth;
        const int targetHeight = SlotHeight;

        var desc = new Texture2DDescription()
                       {
                           Width = targetWidth,
                           Height = targetHeight,
                           ArraySize = 1,
                           BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                           Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                           Usage = ResourceUsage.Default,
                           SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                           MipLevels = 1
                       };

        using var tempTarget = new SharpDX.Direct3D11.Texture2D(device, desc);
        using var rtv = new RenderTargetView(device, tempTarget);
        var sourceSrv = SrvManager.GetSrvForTexture(sourceTexture);

        var sourceAspect = (float)sourceTexture.Description.Width / sourceTexture.Description.Height;
        var targetAspect = (float)targetWidth / targetHeight;

        float viewWidth, viewHeight, offsetX, offsetY;

        if (sourceAspect > targetAspect)
        {
            // Source is wider than 4:3 (Letterbox top/bottom)
            viewWidth = targetWidth;
            viewHeight = targetWidth / sourceAspect;
            offsetX = 0;
            offsetY = (targetHeight - viewHeight) / 2f;
        }
        else
        {
            // Source is taller than 4:3 (Pillarbox sides)
            viewHeight = targetHeight;
            viewWidth = targetHeight * sourceAspect;
            offsetX = (targetWidth - viewWidth) / 2f;
            offsetY = 0;
        }

        context.OutputMerger.SetTargets(rtv);
        // Clear with transparent black to provide the padding
        context.ClearRenderTargetView(rtv, new RawColor4(0, 0, 0, 0));

        context.Rasterizer.SetViewport(new ViewportF(offsetX, offsetY, viewWidth, viewHeight));

        context.VertexShader.Set(SharedResources.FullScreenVertexShaderResource.Value);
        context.PixelShader.Set(SharedResources.FullScreenPixelShaderResource.Value);
        context.PixelShader.SetShaderResource(0, sourceSrv);

        context.Draw(3, 0);

        context.PixelShader.SetShaderResource(0, null);
        context.OutputMerger.SetTargets((RenderTargetView?)null);

        var newTexture = new T3.Core.DataTypes.Texture2D(tempTarget);
        ScreenshotWriter.StartSavingToFile(newTexture, filePath, ScreenshotWriter.FileFormats.Png);
    }

    // --- Helpers ---

    private static void Initialize()
    {
        var device = ResourceManager.Device;
        var desc = new Texture2DDescription
                       {
                           Width = AtlasSize,
                           Height = AtlasSize,
                           MipLevels = 1,
                           ArraySize = 1,
                           Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                           SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                           Usage = ResourceUsage.Default,
                           BindFlags = BindFlags.ShaderResource,
                           CpuAccessFlags = CpuAccessFlags.None
                       };

        _atlas = new SharpDX.Direct3D11.Texture2D(device, desc);
        AtlasSrv = new ShaderResourceView(device, _atlas);
        _initialized = true;
    }

    private static ThumbnailSlot GetLruSlot(Guid guid)
    {
        if (_slots.Count >= MaxSlots)
        {
            var oldest = _slots.Values.OrderBy(s => s.LastUsed).First();
            _slots.Remove(oldest.Guid);
            oldest.Guid = guid;
            oldest.IsLoading = true;
            oldest.LastUsed = DateTime.Now;
            _slots[guid] = oldest;
            return oldest;
        }

        var newSlot = new ThumbnailSlot
                          {
                              Guid = guid,
                              X = _slots.Count % 23,
                              Y = _slots.Count / 23,
                              IsLoading = true,
                              LastUsed = DateTime.Now
                          };
        _slots[guid] = newSlot;
        return newSlot;
    }

    private static ThumbnailRect GetRectFromSlot(ThumbnailSlot slot)
    {
        var x = (float)(slot.X * SlotWidth + Padding) / AtlasSize;
        var y = (float)(slot.Y * SlotHeight + Padding) / AtlasSize;
        var w = (float)(SlotWidth - Padding * 2) / AtlasSize;
        var h = (float)(SlotHeight - Padding * 2) / AtlasSize;

        return new ThumbnailRect(new Vector2(x, y), new Vector2(x + w, y + h), !slot.IsLoading);
    }
}