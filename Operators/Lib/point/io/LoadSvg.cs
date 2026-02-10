#nullable enable
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Lib.Utils;
using Svg;
using Svg.Pathing;
using Svg.Transforms;
using T3.Core.Utils;

namespace Lib.point.io;

[Guid("e8d94dd7-eb54-42fe-a7b1-b43543dd457e")]
internal sealed class LoadSvg : Instance<LoadSvg>, IDescriptiveFilename
{
    [Output(Guid = "e21e3843-7d63-4db2-9234-77664e872a0f")]
    public readonly Slot<StructuredList> ResultList = new();

    public LoadSvg()
    {
        _svgResource = new Resource<SvgDocument>(FilePath, SvgLoader.TryLoad);
        _svgResource.AddDependentSlots(ResultList);
        ResultList.UpdateAction += Update;
        _pointListWithSeparator.TypedElements[_pointListWithSeparator.NumElements - 1] = Point.Separator();
    }

    private struct GraphicsPathEntry
    {
        public GraphicsPath GraphicsPath;
        public bool NeedsClosing;
    }

    private void Update(EvaluationContext context)
    {
        if (!_svgResource.TryGetValue(context, out var svgDoc) && !Scale.IsDirty && !CenterToBounds.IsDirty && !ScaleToBounds.IsDirty && !ImportAs.IsDirty && !ReduceFactor.IsDirty)
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

        var bounds = svgDoc.Bounds;
        var width = bounds.Width;
        var height = bounds.Height;

        // Calculate center offset to move center of bounds to (0,0)
        /*var centerOffset = centerToBounds
            ? new Vector3(-(bounds.Left + width / 2), -(1 - (bounds.Top + height / 2)), 0)
            : Vector3.Zero;*/

        var fitBoundsFactor = scaleToBounds ? (2f / height) : 1;
        var scale = Scale.GetValue(context) * fitBoundsFactor;
        var importMode = ImportAs.GetValue(context);
        var importAsLines = importMode == 0;
        var importAsShape = importMode == 2; // Shape mode
        var reduceFactor = ReduceFactor.GetValue(context).Clamp(0.001f, 1f);
        var selectedShapeIndex = SelectSingleShape.GetValue(context); // Get the selected shape index

        var svgElements = svgDoc.Descendants();
        var pathElements = importAsShape
            ? GetSelectedShapePathElements(svgElements, selectedShapeIndex)
            : ConvertAllNodesIntoGraphicPaths(svgElements, importAsLines);

        // Calculate actual bounds and center offset based on import mode
        Vector3 centerOffset;

        if (importAsShape && pathElements.Count > 0)
        {
            // Get actual bounds from the shape path
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;

            foreach (var pathElement in pathElements)
            {
                var pathBounds = pathElement.GraphicsPath.GetBounds();
                minX = Math.Min(minX, pathBounds.Left);
                minY = Math.Min(minY, pathBounds.Top);
                maxX = Math.Max(maxX, pathBounds.Right);
                maxY = Math.Max(maxY, pathBounds.Bottom);
            }

            var shapeWidth = maxX - minX;
            var shapeHeight = maxY - minY;
            

            // Center offset should account for the actual position of the shape
            centerOffset = centerToBounds
                ? new Vector3(-(minX + shapeWidth / 2), (minY + shapeHeight / 2), 0)
                : Vector3.Zero;
        }
        else
        {
            centerOffset = centerToBounds ? new Vector3(-bounds.X / 2, bounds.Y / 2, 0) : Vector3.Zero;
        }

        // Flatten and sum total point count including separators 
        var totalPointCount = 0;
        foreach (var p in pathElements)
        {
            p.GraphicsPath.Flatten(null, reduceFactor);
            var closePoint = p.NeedsClosing ? 1 : 0;
            totalPointCount += p.GraphicsPath.PointCount + 1 + closePoint;
        }

        if (totalPointCount != _pointListWithSeparator.NumElements)
        {
            _pointListWithSeparator.SetLength(totalPointCount);
        }

        var pointIndex = 0;
        foreach (var pathElement in pathElements)
        {
            var startIndex = pointIndex;

            var path = pathElement.GraphicsPath;
            var pathPointCount = path.PathPoints.Length;
            for (var pathPointIndex = 0; pathPointIndex < pathPointCount; pathPointIndex++)
            {
                var point = path.PathPoints[pathPointIndex];

                _pointListWithSeparator.TypedElements[startIndex + pathPointIndex].Position
                    = (new Vector3(point.X, 1 - point.Y, 0) + centerOffset) * scale;
                _pointListWithSeparator.TypedElements[startIndex + pathPointIndex].F1 = 1;
                _pointListWithSeparator.TypedElements[startIndex + pathPointIndex].Orientation = Quaternion.Identity;
                _pointListWithSeparator.TypedElements[startIndex + pathPointIndex].Color = new Vector4(1.0f); // We need a better fix, maybe with the colors from the SVG file
                _pointListWithSeparator.TypedElements[startIndex + pathPointIndex].F2 = 1;
                _pointListWithSeparator.TypedElements[startIndex + pathPointIndex].Scale = Vector3.One;
            }

            // Calculate normals
            if (pathPointCount > 1)
            {
                for (var pathPointIndex = 0; pathPointIndex < pathPointCount; pathPointIndex++)
                {
                    if (pathPointIndex == 0)
                    {
                        _pointListWithSeparator.TypedElements[startIndex + pathPointIndex].Orientation =
                            RotationFromTwoPositions(_pointListWithSeparator.TypedElements[0].Position,
                                                     _pointListWithSeparator.TypedElements[1].Position);
                    }
                    else if (pathPointIndex == pathPointCount - 1)
                    {
                        _pointListWithSeparator.TypedElements[startIndex + pathPointIndex].Orientation =
                            RotationFromTwoPositions(_pointListWithSeparator.TypedElements[pathPointCount - 2].Position,
                                                     _pointListWithSeparator.TypedElements[pathPointCount - 1].Position);
                    }
                    else
                    {
                        _pointListWithSeparator.TypedElements[startIndex + pathPointIndex].Orientation =
                            RotationFromTwoPositions(_pointListWithSeparator.TypedElements[startIndex + pathPointIndex].Position,
                                                     _pointListWithSeparator.TypedElements[startIndex + pathPointIndex + 1].Position);
                    }
                }
            }

            // Close loop?
            if (pathElement.NeedsClosing)
            {
                _pointListWithSeparator.TypedElements[startIndex + pathPointCount] = _pointListWithSeparator.TypedElements[startIndex];
                pointIndex++;
            }

            pointIndex += path.PathPoints.Length;

            _pointListWithSeparator.TypedElements[pointIndex] = Point.Separator();
            pointIndex++;
        }


        ResultList.Value = _pointListWithSeparator;

    }

    private static Quaternion RotationFromTwoPositions(Vector3 p1, Vector3 p2)
    {
        return Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), (float)(Math.Atan2(p1.X - p2.X, -(p1.Y - p2.Y)) + Math.PI / 2));
    }

    /// <summary>
    /// Gets a specific path element by index from all SVG paths
    /// </summary>
    private static List<GraphicsPathEntry> GetSelectedShapePathElements(IEnumerable<SvgElement> nodes, int selectedIndex)
    {
        var paths = new List<GraphicsPathEntry>();
        _svgRenderer ??= SvgRenderer.FromImage(new Bitmap(1, 1));

        // Collect all SvgPath elements
        var allSvgPaths = nodes.OfType<SvgPath>().ToList();

        if (allSvgPaths.Count == 0)
            return paths;

        // Clamp the selected index to valid range
        var clampedIndex = selectedIndex;
        if (clampedIndex < 0)
            clampedIndex = 0;
        if (clampedIndex >= allSvgPaths.Count)
            clampedIndex = allSvgPaths.Count - 1;

        var targetPath = allSvgPaths[clampedIndex];

        GraphicsPath? newPath = null;

        foreach (var s in targetPath.PathData)
        {
            var segmentIsJump = s is SvgMoveToSegment or SvgClosePathSegment;
            if (segmentIsJump)
            {
                if (newPath == null)
                    continue;

                paths.Add(new GraphicsPathEntry
                {
                    GraphicsPath = newPath,
                    NeedsClosing = false
                });
                newPath = null;
            }
            else
            {
                newPath ??= new GraphicsPath();
                s.AddToPath(newPath);
            }
        }

        if (newPath != null)
        {
            paths.Add(new GraphicsPathEntry
            {
                GraphicsPath = newPath,
                NeedsClosing = false
            });
        }

        return paths;
    }

    private static List<GraphicsPathEntry> ConvertAllNodesIntoGraphicPaths(IEnumerable<SvgElement> nodes, bool importAsLines)
    {
        var paths = new List<GraphicsPathEntry>();

        _svgRenderer ??= SvgRenderer.FromImage(new Bitmap(1, 1));

        foreach (var node in nodes)
        {
            GraphicsPath? newPath = null;
            switch (node)
            {
                case SvgPath svgPath:
                    {
                        foreach (var s in svgPath.PathData)
                        {
                            var segmentIsJump = s is SvgMoveToSegment or SvgClosePathSegment;
                            if (segmentIsJump)
                            {
                                if (newPath == null)
                                    continue;

                                paths.Add(new GraphicsPathEntry
                                {
                                    GraphicsPath = newPath,
                                    NeedsClosing = false
                                });
                                newPath = null;
                            }
                            else
                            {
                                newPath ??= new GraphicsPath();
                                s.AddToPath(newPath);
                            }
                        }

                        if (newPath != null)
                        {
                            paths.Add(new GraphicsPathEntry
                            {
                                GraphicsPath = newPath,
                                NeedsClosing = false
                            });
                        }

                        break;
                    }
                case SvgGroup:
                    break;

                case SvgPathBasedElement element:
                    {
                        if (element is SvgRectangle rect)
                        {
                            if (rect.Transforms != null)
                            {
                                foreach (var t in rect.Transforms)
                                {
                                    if (t is not SvgTranslate tr)
                                        continue;

                                    rect.X += tr.X;
                                    rect.Y += tr.Y;
                                }
                            }
                        }

                        var needsClosing = element is SvgRectangle or SvgCircle or SvgEllipse;

                        var graphicsPath = element.Path(_svgRenderer);

                        paths.Add(new GraphicsPathEntry
                        {
                            GraphicsPath = graphicsPath,
                            NeedsClosing = needsClosing && importAsLines
                        });
                        break;
                    }
            }
        }

        return paths;
    }

    public InputSlot<string> SourcePathSlot => FilePath;
    private readonly Resource<SvgDocument> _svgResource;
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

    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private enum ImportModes
    {
        Lines,
        Points,
        Shape // Select a single path by index using SelectSingleShape input
    }

    private static ISvgRenderer? _svgRenderer;
}