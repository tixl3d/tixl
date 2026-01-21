using Lib.Utils;
using Svg;
using SharpDX.WIC;


namespace Lib.image.generate.load;

[Guid("d05739d3-f89d-488d-85d0-c0d115265b75")]
internal sealed class LoadSvgAsTexture2D : Instance<LoadSvgAsTexture2D>, IDescriptiveFilename, IStatusProvider
{
    [Output(Guid = "ab1e6a50-4ac1-43ba-8323-81fc8af29814")]
    public readonly Slot<Texture2D> Texture = new();

    public LoadSvgAsTexture2D()
    {
        _svgResource = new Resource<SvgDocument>(Path, SvgLoader.TryLoad);

        _svgResource.AddDependentSlots(Texture);

        Texture.UpdateAction = UpdateTexture;
    }

    private void UpdateTexture(EvaluationContext context)
    {
        if (_svgResource.TryGetValue(context, out var svgDocument))
        {
            // Process the SVG document...
            var scale = Scale.GetValue(context);

            if (svgDocument == null)
            {
                return;
            }

            try
            {
                // Rasterize the SVG to a bitmap
                System.Drawing.Bitmap rasterizedBitmap;

                // Check if a specific resolution is provided
                if (scale == 0.0 || scale == 1.0)
                {
                    rasterizedBitmap = svgDocument.Draw();
                }
                else
                {
                    var width = (int)(svgDocument.ViewBox.Width * scale);
                    var height = (int)(svgDocument.ViewBox.Height * scale);
                    rasterizedBitmap = svgDocument.Draw(width, height);
                }

                if (rasterizedBitmap == null)
                {
                    _lastErrorMessage = "Failed to rasterize SVG: " + Path.Value;
                    Log.Warning(_lastErrorMessage, this);
                    Texture.Value = null;
                    Texture.DirtyFlag.Clear();
                    return;
                }

                // Convert System.Drawing.Bitmap to Texture2D using SharpDX
                Texture.Value = ConvertBitmapToTexture2D(rasterizedBitmap);
                Texture.DirtyFlag.Clear();

                // Dispose the bitmap after conversion
                rasterizedBitmap.Dispose();

                if (Texture.Value == null)
                {
                    _lastErrorMessage = "Failed to convert bitmap to texture: " + Path.Value;
                    Log.Warning(_lastErrorMessage, this);
                    return;
                }

                var currentSrv = SrvManager.GetSrvForTexture(Texture.Value);

                try
                {
                    ResourceManager.Device.ImmediateContext.GenerateMips(currentSrv);
                }
                catch (Exception exception)
                {
                    Log.Error($"Failed to generate mipmaps for texture {Path.Value}: " + exception);
                }

                _lastErrorMessage = string.Empty;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = $"Error processing SVG: {ex.Message}";
                Log.Error(_lastErrorMessage, this);
                Texture.Value = null;
                Texture.DirtyFlag.Clear();
            }
        }
        else
        {
            // Handle loading failure
            _lastErrorMessage = "Failed to load SVG document: " + Path.Value;
            Log.Warning(_lastErrorMessage, this);
            Texture.Value = null;
            Texture.DirtyFlag.Clear();
        }
    }

    private static Texture2D ConvertBitmapToTexture2D(System.Drawing.Bitmap bitmap)
    {
        using var memoryStream = new MemoryStream();

        // Save the bitmap to a memory stream as PNG
        bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
        memoryStream.Position = 0;

        // Use WIC to load the bitmap
        using var factory = new ImagingFactory();
        using var bitmapDecoder = new BitmapDecoder(factory, memoryStream, DecodeOptions.CacheOnDemand);
        using var formatConverter = new FormatConverter(factory);
        using var bitmapFrameDecode = bitmapDecoder.GetFrame(0);

        // Convert to RGBA format
        formatConverter.Initialize(
            bitmapFrameDecode,
            PixelFormat.Format32bppRGBA,
            BitmapDitherType.None,
            null,
            0.0,
            BitmapPaletteType.Custom);

        return Texture2D.CreateFromBitmap(ResourceManager.Device, formatConverter);
    }

    public IEnumerable<string> FileFilter => FileFilters;
    private static readonly string[] FileFilters = ["*.svg"];
    public InputSlot<string> SourcePathSlot => Path;

    private readonly Resource<SvgDocument> _svgResource;

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _lastErrorMessage;

    private string _lastErrorMessage = string.Empty;

    [Input(Guid = "f4860e75-eff7-4e6e-a144-016ff5bb054e")]
    public readonly InputSlot<string> Path = new();
    
    [Input(Guid = "89CD0433-8646-47F3-A22E-AFE1344C6F07")]
    public readonly InputSlot<float> Scale = new();
}