extern alias skiasvg;
using Lib.Utils;
using SkiaSharp;
using Svg.Skia;
using skiasvg::Svg;
using skiasvg::Svg.Transforms;

namespace Lib.image.generate.load;

[Guid("d05739d3-f89d-488d-85d0-c0d115265b75")]
internal sealed class LoadSvgAsTexture2D : Instance<LoadSvgAsTexture2D>, IDescriptiveFilename, IStatusProvider
{
    [Output(Guid = "ab1e6a50-4ac1-43ba-8323-81fc8af29814")]
    public readonly Slot<Texture2D> Texture = new();

    public LoadSvgAsTexture2D()
    {
        _svgResource = new Resource<SvgDocument>(Path, SkiaSvgGeometry.TryLoad);

        _svgResource.AddDependentSlots(Texture);

        Texture.UpdateAction = UpdateTexture;
    }

    private void UpdateTexture(EvaluationContext context)
    {
        if (!_svgResource.TryGetValue(context, out var svgDocument) || svgDocument == null)
        {
            _lastErrorMessage = "Failed to load SVG document: " + Path.Value;
            Log.Warning(_lastErrorMessage, this);
            Texture.Value = null;
            Texture.DirtyFlag.Clear();
            return;
        }

        var resolution = Resolution.GetValue(context);
        var useViewBox = UseViewBox.GetValue(context);
        var scale = Scale.GetValue(context);
        var selectLayerRange = SelectLayerRange.GetValue(context);
        var splitToLayers = SplitToLayers.GetValue(context);

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

            var texture = Rasterize(workingDocument, width, height);
            if (texture == null)
            {
                _lastErrorMessage = "Failed to rasterize SVG: " + Path.Value;
                Log.Warning(_lastErrorMessage, this);
                Texture.Value = null;
                Texture.DirtyFlag.Clear();
                return;
            }

            _texture?.Dispose();
            _texture = texture;
            Texture.Value = _texture;
            Texture.DirtyFlag.Clear();
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

    /// <summary>
    /// Renders the document at the given pixel size. A zero side follows the document's aspect ratio and two
    /// given sides stretch it, as System.Drawing's SvgDocument.Draw did.
    /// </summary>
    private static Texture2D Rasterize(SvgDocument document, int width, int height)
    {
        using var skSvg = SKSvg.CreateFromSvgDocument(document);
        var picture = skSvg.Picture;
        if (picture == null)
            return null;

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        if (width <= 0 && height <= 0)
        {
            width = (int)MathF.Round(bounds.Width);
            height = (int)MathF.Round(bounds.Height);
        }
        else if (width <= 0)
        {
            width = (int)MathF.Round(height * bounds.Width / bounds.Height);
        }
        else if (height <= 0)
        {
            height = (int)MathF.Round(width * bounds.Height / bounds.Width);
        }

        if (width <= 0 || height <= 0)
            return null;

        // Skia only draws into premultiplied targets; the textures elsewhere hold straight alpha.
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface == null)
            return null;

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(width / bounds.Width, height / bounds.Height);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);

        using var pixels = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (!surface.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0))
            return null;

        return Texture2D.CreateFromRgba(ResourceManager.Device, width, height, pixels.GetPixelSpan());
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

    public InputSlot<string> SourcePathSlot => Path;

    private readonly Resource<SvgDocument> _svgResource;

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _lastErrorMessage;

    private string _lastErrorMessage = string.Empty;
    private Texture2D _texture;

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