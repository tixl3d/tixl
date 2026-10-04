extern alias skiasvg;
#nullable enable
using System.Collections.Generic;
using Lib.Utils;
using SkiaSharp;
using T3.Core.Utils;
using S = skiasvg::Svg;

namespace Lib.point.io;

[Guid("e8d94dd7-eb54-42fe-a7b1-b43543dd457e")]
internal sealed class LoadSvg : Instance<LoadSvg>, IDescriptiveFilename
{
    [Output(Guid = "e21e3843-7d63-4db2-9234-77664e872a0f")]
    public readonly Slot<StructuredList> ResultList = new();

    public LoadSvg()
    {
        _svgResource = new Resource<S.SvgDocument>(FilePath, SkiaSvgGeometry.TryLoad);
        _svgResource.AddDependentSlots(ResultList);
        ResultList.UpdateAction += Update;
        _pointListWithSeparator.TypedElements[_pointListWithSeparator.NumElements - 1] = Point.Separator();
    }

    private void Update(EvaluationContext context)
    {
        if (!_svgResource.TryGetValue(context, out var svgDoc)
            && !Scale.IsDirty && !CenterToBounds.IsDirty && !ScaleToBounds.IsDirty
            && !ImportAs.IsDirty && !ReduceFactor.IsDirty && !Flattening.IsDirty)
        {
            // Nothing changed, keep existing data
            return;
        }

        if (svgDoc == null)
        {
            _pointListWithSeparator.SetLength(0);
            ResultList.Value = _pointListWithSeparator;
            return;
        }

        var centerToBounds = CenterToBounds.GetValue(context);
        var scaleToBounds = ScaleToBounds.GetValue(context);

        var importMode = ImportAs.GetValue(context);
        var importAsShape = importMode == 2;

        var flattenMode = (SkiaSvgGeometry.FlattenModes)Flattening.GetValue(context);

        // Adaptive reads this as a flatness tolerance and even spacing as a distance, so the useful ranges
        // differ by an order of magnitude. Scaling here keeps the one parameter meaningful for both.
        var reduceFactor = ReduceFactor.GetValue(context).Clamp(0.001f, 1f);
        var flattenAmount = flattenMode == SkiaSvgGeometry.FlattenModes.EvenSpacing
                                ? reduceFactor * 10f
                                : reduceFactor;

        var selectedShapeIndex = SelectSingleShape.GetValue(context);

        var svgElements = svgDoc.Descendants();
        var pathElements = importAsShape
                               ? GetSelectedShape(svgElements, selectedShapeIndex, flattenMode, flattenAmount, out var contentBounds)
                               : ConvertAllNodes(svgElements, flattenMode, flattenAmount, out contentBounds);

        // The document used to answer this through System.Drawing; what it meant was the content's extent.
        var bounds = new Vector3(contentBounds.Width, contentBounds.Height, 0);
        var fitBoundsFactor = scaleToBounds && bounds.Y > 0 ? (2f / bounds.Y) : 1;
        var scale = Scale.GetValue(context) * fitBoundsFactor;

        Vector3 centerOffset;
        if (importAsShape && pathElements.Count > 0)
        {
            centerOffset = centerToBounds
                               ? new Vector3(-(contentBounds.Left + contentBounds.Width / 2),
                                             contentBounds.Top + contentBounds.Height / 2, 0)
                               : Vector3.Zero;
        }
        else
        {
            centerOffset = centerToBounds ? new Vector3(-bounds.X / 2, bounds.Y / 2, 0) : Vector3.Zero;
        }

        // Total including the separator after each contour.
        var totalPointCount = 0;
        foreach (var entry in pathElements)
        {
            totalPointCount += entry.Count + 1;
        }

        if (totalPointCount != _pointListWithSeparator.NumElements)
        {
            _pointListWithSeparator.SetLength(totalPointCount);
        }

        if (totalPointCount == 0)
        {
            ResultList.Value = _pointListWithSeparator;
            return;
        }

        var pointIndex = 0;
        foreach (var points in pathElements)
        {
            var startIndex = pointIndex;
            var pathPointCount = points.Count;

            for (var i = 0; i < pathPointCount; i++)
            {
                var point = points[i];
                ref var target = ref _pointListWithSeparator.TypedElements[startIndex + i];

                target.Position = (new Vector3(point.X, 1 - point.Y, 0) + centerOffset) * scale;
                target.F1 = 1;
                target.Orientation = Quaternion.Identity;
                target.Color = new Vector4(1.0f); // We need a better fix, maybe with the colors from the SVG file
                target.F2 = 1;
                target.Scale = Vector3.One;
            }

            // Orientation follows the step to the next point; the last point borrows the step before it.
            if (pathPointCount > 1)
            {
                for (var i = 0; i < pathPointCount; i++)
                {
                    var a = i == pathPointCount - 1 ? pathPointCount - 2 : i;

                    _pointListWithSeparator.TypedElements[startIndex + i].Orientation =
                        RotationFromTwoPositions(_pointListWithSeparator.TypedElements[startIndex + a].Position,
                                                 _pointListWithSeparator.TypedElements[startIndex + a + 1].Position);
                }
            }

            pointIndex += pathPointCount;

            _pointListWithSeparator.TypedElements[pointIndex] = Point.Separator();
            pointIndex++;
        }

        ResultList.Value = _pointListWithSeparator;
    }

    private static Quaternion RotationFromTwoPositions(Vector3 p1, Vector3 p2)
    {
        return Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), (float)(Math.Atan2(p1.X - p2.X, -(p1.Y - p2.Y)) + Math.PI / 2));
    }

    /// <summary>One chosen path element, split into its contours.</summary>
    private static List<List<SKPoint>> GetSelectedShape(IEnumerable<S.SvgElement> nodes, int selectedIndex,
                                                        SkiaSvgGeometry.FlattenModes mode, float amount,
                                                        out SKRect contentBounds)
    {
        contentBounds = SKRect.Empty;

        var allSvgPaths = nodes.OfType<S.SvgPath>().ToList();
        if (allSvgPaths.Count == 0)
            return [];

        var clampedIndex = selectedIndex.Clamp(0, allSvgPaths.Count - 1);

        using var path = SkiaSvgGeometry.TryBuildPath(allSvgPaths[clampedIndex]);
        if (path == null)
            return [];

        contentBounds = path.Bounds;
        return SkiaSvgGeometry.Flatten(path, mode, amount);
    }

    private static List<List<SKPoint>> ConvertAllNodes(IEnumerable<S.SvgElement> nodes,
                                                       SkiaSvgGeometry.FlattenModes mode, float amount,
                                                       out SKRect contentBounds)
    {
        var entries = new List<List<SKPoint>>();
        var bounds = SKRect.Empty;
        var hasBounds = false;

        foreach (var node in nodes)
        {
            if (node is S.SvgGroup)
                continue;

            using var path = SkiaSvgGeometry.TryBuildPath(node);
            if (path == null)
                continue;

            if (!hasBounds)
            {
                bounds = path.Bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Union(path.Bounds);
            }

            entries.AddRange(SkiaSvgGeometry.Flatten(path, mode, amount));
        }

        contentBounds = bounds;
        return entries;
    }

    public InputSlot<string> SourcePathSlot => FilePath;
    private readonly Resource<S.SvgDocument> _svgResource;
    private readonly StructuredList<Point> _pointListWithSeparator = new(101);

    [Input(Guid = "EF2A461D-C66D-44D8-8B0E-E48A57EC991F")]
    public readonly InputSlot<string> FilePath = new();

    [Input(Guid = "C6692E97-E7F8-4B3F-95BC-5F86C2B399A5")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "4DFCE92E-9282-486F-A274-E59402696BBB")]
    public readonly InputSlot<bool> CenterToBounds = new();

    [Input(Guid = "221BF10C-B13E-40CF-80AF-769C10A21C5B")]
    public readonly InputSlot<bool> ScaleToBounds = new();

    [Input(Guid = "8D63C134-1257-4331-AE84-F5EB6DD66C13", MappedType = typeof(ImportModes))]
    public readonly InputSlot<int> ImportAs = new();

    [Input(Guid = "2BB64740-ED2F-4295-923D-D585D70197E7")]
    public readonly InputSlot<float> ReduceFactor = new();

    [Input(Guid = "05E5AEC4-35A7-48DD-8F79-91EF754D20E8")]
    public readonly InputSlot<int> SelectSingleShape = new();

    [Input(Guid = "7F3A9C21-5D4E-4B88-9A17-2C6E1B0D4F55", MappedType = typeof(SkiaSvgGeometry.FlattenModes))]
    public readonly InputSlot<int> Flattening = new();

    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private enum ImportModes
    {
        Lines,
        Points,
        Shape
    }
}
