using Lib.Utils;
using SharpDX.WIC;
using Svg;
using Svg.Transforms;

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
            var resolution = Resolution.GetValue(context);
            var useViewBox = UseViewBox.GetValue(context);
            var scale = Scale.GetValue(context);
            var selectLayerRange = SelectLayerRange.GetValue(context);
            var splitToLayers = SplitToLayers.GetValue(context);

            if (svgDocument == null)
            {
                return;
            }

            try
            {
                // Work with an SvgDocument to preserve coordinate systems
                var workingDocument = svgDocument;

                if (splitToLayers)
                {
                    // Get actual content layers with their parent attributes
                    var contentLayersWithParents = GetContentLayersWithParentAttributes(svgDocument).ToList();
                    Log.Info($"Found {contentLayersWithParents.Count} layers in {Path.Value}", this);
                    if (contentLayersWithParents.Count > 0)
                    {
                        // Validate and normalize range
                        var startIndex = Math.Max(0, Math.Min(selectLayerRange.X, contentLayersWithParents.Count - 1));
                        var endIndex = Math.Max(0, Math.Min(selectLayerRange.Y, contentLayersWithParents.Count - 1));

                        // Make sure start <= end
                        if (startIndex > endIndex)
                        {
                            (endIndex, startIndex) = (startIndex, endIndex);
                        }

                        // Create a new document with the same properties
                        workingDocument = new SvgDocument
                        {
                            ViewBox = svgDocument.ViewBox,
                            AspectRatio = svgDocument.AspectRatio,
                            Width = svgDocument.Width,
                            Height = svgDocument.Height
                        };

                        // Copy document-level definitions if they exist (important for styles, gradients, etc.)
                        var defs = svgDocument.Children.OfType<SvgDefinitionList>().FirstOrDefault();
                        if (defs != null)
                        {
                            workingDocument.Children.Add((SvgElement)defs.DeepCopy());
                        }

                        // Add selected layers to the new document
                        // ElementRange 0,1 means layers at index 0 and 1 (first two layers)
                        // ElementRange 1,5 means layers at indices 1,2,3,4,5
                        for (var i = startIndex; i <= endIndex; i++)
                        {
                            var (element, parentGroup) = contentLayersWithParents[i];

                            // Deep copy to preserve entire hierarchy
                            var clonedElement = (SvgElement)element.DeepCopy();

                            // If there was a parent group with attributes, wrap the element
                            if (parentGroup != null)
                            {
                                // Create a wrapper group with all parent attributes
                                var wrapperGroup = (SvgGroup)parentGroup.DeepCopy();
                                // Clear any children from the parent copy
                                wrapperGroup.Children.Clear();
                                // Add our cloned element
                                wrapperGroup.Children.Add(clonedElement);
                                workingDocument.Children.Add(wrapperGroup);
                            }
                            else
                            {
                                // No parent attributes, add directly
                                workingDocument.Children.Add(clonedElement);
                            }
                        }     
                    }      
                }

                // Rasterize the SVG to a bitmap
                System.Drawing.Bitmap rasterizedBitmap;
                var width = 0;
                var height = 0;

                if (resolution.X == 0 && resolution.Y == 0 && !useViewBox)
                {
                    height = context.RequestedResolution.Height;
                }
                else if (!useViewBox)
                {
                    width = resolution.X;
                    height = resolution.Y;
                }
                else
                {
                    width = (int)(workingDocument.ViewBox.Width * scale);
                    height = (int)(workingDocument.ViewBox.Height * scale);
                }

                // Render to bitmap at the desired resolution
                rasterizedBitmap = workingDocument.Draw(width, height);

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

    /// <summary>
    /// Gets the actual content layers from an SVG document, unwrapping single-child containers.
    /// This handles cases where SVG editors wrap everything in unnecessary container groups.
    /// Returns tuples of (element, parent group with all attributes to preserve).
    /// </summary>
    private static IEnumerable<(SvgElement element, SvgGroup parentGroup)> GetContentLayersWithParentAttributes(SvgDocument document)
    {
        // Get top-level children that are visual or groups (exclude defs, metadata, etc.)
        var topLevelChildren = document.Children
            .Where(child => child is SvgVisualElement || child is SvgGroup)
            .ToList();

        // If there's only one top-level child and it's a group, check if we should unwrap it
        if (topLevelChildren.Count == 1 && topLevelChildren[0] is SvgGroup singleGroup)
        {
            // Get the children of this single group
            var groupChildren = singleGroup.Children
                .Where(child => child is SvgVisualElement || child is SvgGroup)
                .ToList();

            // If the group has multiple children, those are likely the actual layers
            if (groupChildren.Count > 1)
            {
                // Return children with the parent group
                return groupChildren.Select(child => (child, singleGroup));
            }
            else if (groupChildren.Count == 1 && groupChildren[0] is SvgGroup nestedGroup)
            {
                // Recursively unwrap nested single-child groups
                var nestedChildren = nestedGroup.Children
                    .Where(child => child is SvgVisualElement || child is SvgGroup)
                    .ToList();

                if (nestedChildren.Count > 0)
                {
                    // Create a merged parent group that combines attributes from both levels
                    var mergedParent = MergeParentGroups(singleGroup, nestedGroup);
                    return nestedChildren.Select(child => (child, mergedParent));
                }
            }
        }

        // Default: return the top-level children with no parent group
        return topLevelChildren.Select(child => (child, (SvgGroup)null));
    }

    /// <summary>
    /// Merges attributes from nested parent groups into a single group.
    /// Outer group attributes are applied first, then inner group attributes.
    /// </summary>
    private static SvgGroup MergeParentGroups(SvgGroup outer, SvgGroup inner)
    {
        var merged = new SvgGroup
        {
            // Combine transforms (outer first, then inner)
            Transforms = []
        };
        if (outer.Transforms != null)
        {
            foreach (var transform in outer.Transforms)
            {
                merged.Transforms.Add(transform.Clone() as SvgTransform);
            }
        }
        if (inner.Transforms != null)
        {
            foreach (var transform in inner.Transforms)
            {
                merged.Transforms.Add(transform.Clone() as SvgTransform);
            }
        }

        // Copy other attributes (inner overrides outer if both are set)
        merged.Opacity = inner.Opacity != 1.0f ? inner.Opacity : outer.Opacity;
        merged.Fill = inner.Fill ?? outer.Fill;
        merged.Stroke = inner.Stroke ?? outer.Stroke;
        // SvgUnit is a struct, so use IsEmpty to check for "unset" value
        merged.StrokeWidth = !inner.StrokeWidth.IsEmpty ? inner.StrokeWidth : outer.StrokeWidth;
        merged.StrokeLineCap = inner.StrokeLineCap != SvgStrokeLineCap.Butt ? inner.StrokeLineCap : outer.StrokeLineCap;
        merged.StrokeLineJoin = inner.StrokeLineJoin != SvgStrokeLineJoin.Miter ? inner.StrokeLineJoin : outer.StrokeLineJoin;
        merged.StrokeDashArray = inner.StrokeDashArray ?? outer.StrokeDashArray;
        // SvgUnit is a struct, so use IsEmpty to check for "unset" value
        merged.StrokeDashOffset = !inner.StrokeDashOffset.IsEmpty ? inner.StrokeDashOffset : outer.StrokeDashOffset;
        merged.FillOpacity = inner.FillOpacity != 1.0f ? inner.FillOpacity : outer.FillOpacity;
        merged.StrokeOpacity = inner.StrokeOpacity != 1.0f ? inner.StrokeOpacity : outer.StrokeOpacity;
        return merged;
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

    public InputSlot<string> SourcePathSlot => Path;

    private readonly Resource<SvgDocument> _svgResource;

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _lastErrorMessage;

    private string _lastErrorMessage = string.Empty;

    [Input(Guid = "f4860e75-eff7-4e6e-a144-016ff5bb054e")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "2063A2A5-305D-4022-A1AF-0840CDAA8BE4")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "A01425AD-E38F-4A80-B8CF-A2EA03895FE2")]
    public readonly InputSlot<bool> UseViewBox = new();

    [Input(Guid = "89CD0433-8646-47F3-A22E-AFE1344C6F07")]
    public readonly InputSlot<float> Scale = new();
    
    [Input(Guid = "A13F5039-65A7-45B2-9407-50D718CCB13B")]
    public readonly InputSlot<bool> SplitToLayers = new();

    [Input(Guid = "3E8F9A1B-2C4D-5E6F-7A8B-9C0D1E2F3A4B")]
    public readonly InputSlot<Int2> SelectLayerRange = new();
}