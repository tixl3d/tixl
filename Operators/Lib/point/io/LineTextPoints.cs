#nullable enable
using System.Drawing;
using System.Drawing.Drawing2D;
using Lib.Utils;
using Svg;
using Svg.Pathing;
using Svg.Transforms;
using T3.Core.Utils;
// ReSharper disable MemberCanBePrivate.Global

// ReSharper disable TooWideLocalVariableScope
// ReSharper disable CheckNamespace

namespace Lib.point.io;

/**
 * This operator is a compromise: Please also review the additional documentation
 * on the wiki: https://github.com/tixl3d/tixl/wiki/SvgLineFonts
 */
[Guid("3d862455-6a7b-4bf6-a159-e4f7cdba6062")]
internal sealed class LineTextPoints : Instance<LineTextPoints>
{
    [Output(Guid = "618e9151-cd91-4aa6-9d91-4bb51610cc8b")]
    public readonly Slot<StructuredList?> ResultList = new();

    public LineTextPoints()
    {
        _svgResource = new Resource<SvgDocument>(FilePath, SvgLoader.TryLoad);
        _svgResource.AddDependentSlots(ResultList);
        ResultList.UpdateAction += Update;
        _pointListWithSeparator.TypedElements[_pointListWithSeparator.NumElements - 1] = Point.Separator();
    }

    private void Update(EvaluationContext context)
    {
        if (!_svgResource.TryGetValue(context, out var svgDoc))
        {
            ResultList.Value = null;
            return;
        }
            
        var lineFontKey = new LineFontDefinition(Key: FilePath.GetValue(context) ?? string.Empty,
                                                 ReduceCurveThreshold: ReduceCurveThreshold.GetValue(context),
                                                 CornerBalance: CornerWeightBalance.GetValue(context));
        var fontNeedsUpdate = _lineFontKey != lineFontKey;
            
        var needsUpdate = fontNeedsUpdate
                          || Text.DirtyFlag.IsDirty
                          || Position.DirtyFlag.IsDirty
                          || Size.DirtyFlag.IsDirty
                          || LineHeight.DirtyFlag.IsDirty
                          || VerticalAlign.DirtyFlag.IsDirty
                          || HorizontalAlign.DirtyFlag.IsDirty
                          || Spacing.DirtyFlag.IsDirty;
            
        if (!needsUpdate)
            return;
            
        _lineFont = LineFont.Create(svgDoc, _lineFontKey, lineFontKey, this);
        _lineFontKey = lineFontKey;
            
        var cursorPos = Vector3.Zero;

        var text = Text.GetValue(context);

        if (string.IsNullOrEmpty(text))
            text = " ";

        var horizontalAlign = HorizontalAlign.GetEnumValue<HorizontalAligns>(context);
        var verticalAlign = VerticalAlign.GetEnumValue<VerticalAligns>(context);
        
        var characterSpacing = Spacing.GetValue(context);
        var lineHeightFactor = LineHeight.GetValue(context);
        
        var verticalFlip = _lineFont.UnitsPerEm < 0 ? 1 : -1;
        var cursorLineHeight = MathF.Abs(_lineFont.LineHeight) * verticalFlip * lineHeightFactor;

        var color = Color.GetValue(context);
        var lineWidth = LineWidth.GetValue(context);
        
        var position = Position.GetValue(context);

        var entitySetter = SetFx1To.GetEnumValue<Entities>(context);

        if (_lineFont == null)
            return;
        
        float cursorX = 0;
        float cursorY = 0;

        var needComputeLineWidths = verticalAlign != VerticalAligns.Top || horizontalAlign != HorizontalAligns.Left;

        var lineCountInText = 1;

        if (verticalAlign == VerticalAligns.Middle || verticalAlign == VerticalAligns.Bottom)
        {
            foreach (var c in text)
            {
                if (c == '\n')
                {
                    lineCountInText++;
                }
            }
        }

        switch (verticalAlign)
        {
            case VerticalAligns.Top:
                cursorY -= _lineFont.LineHeight * verticalFlip;
                break;

            case VerticalAligns.Middle:
                cursorY -= _lineFont.LineHeight * verticalFlip - (lineCountInText - 1) / 2f * cursorLineHeight;
                break;

            case VerticalAligns.Bottom:
                cursorY -= _lineFont.LineHeight * verticalFlip - (lineCountInText - 1) * cursorLineHeight;
                break;
        }

        _points.Clear();

        var lastCharForKerning = 0;
        var size = Size.GetValue(context) / MathF.Abs(_lineFont.UnitsPerEm) * 2/1080f; // Scaling to match 1080p 72DPI pt font sizes

        var horizontalLineOffset = 0f;
        var needUpdateCurrentLineWidth = needComputeLineWidths;
        var wordIndex = 0;
        var lineIndex = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (needUpdateCurrentLineWidth)
            {
                horizontalLineOffset = GetHorizontalLineOffset(text, index, horizontalAlign, characterSpacing);
                needUpdateCurrentLineWidth = false;
            }

            var c = text[index];

            if (c == '\n')
            {
                cursorY -= cursorLineHeight;
                cursorX = 0;
                lastCharForKerning = 0;
                needUpdateCurrentLineWidth = true;
                wordIndex++;
                lineIndex++;
                continue;
            }

            if (c == ' ')
            {
                wordIndex++;
            }

            var glyph = _lineFont?.GetGlyphForCharacter(c);
            if (glyph == null)
            {
                lastCharForKerning = 0;
                continue;
            }

            if (lastCharForKerning != 0)
            {
                int key = lastCharForKerning | c;
                if (_lineFont!.KerningForPairs.TryGetValue(key, out var kerning2))
                {
                    cursorX += kerning2;
                }
            }

            var numPoints = glyph.Points.Length;
            var entityFloat = entitySetter switch
                                  {
                                      Entities.None       => 0,
                                      Entities.Characters => index,
                                      Entities.Words      => wordIndex,
                                      Entities.Lines      => lineIndex,
                                      _                   => throw new ArgumentOutOfRangeException()
                                  };
            
            for (var pointIndex = 0; pointIndex < numPoints; pointIndex++)
            {
                var p = glyph.Points[pointIndex];
                var pointPos = p.Position;
                p.Position = new Vector3(
                                         (cursorX - glyph.VertOriginX + pointPos.X + horizontalLineOffset) * size,
                                         (cursorY - glyph.VertOriginY + pointPos.Y) * size * verticalFlip,
                                         0) + position;
                p.Color = color;
                p.Scale  *= lineWidth;// keep NaN
                p.F1 = entityFloat;
                p.F2 = 1;
                _points.Add(p);
            }

            cursorPos.X += glyph.AdvanceX;

            cursorX += glyph.AdvanceX;
            cursorX += characterSpacing * MathF.Abs(_lineFont?.UnitsPerEm ?? 0) * 0.01f;
            lastCharForKerning = c;
        }

        var pointCount = _points.Count;
        if (pointCount == 0)
        {
            ResultList.Value = null;
            return;
        }

        if (_pointListWithSeparator.TypedElements.Length != pointCount)
        {
            _pointListWithSeparator = new StructuredList<Point>(_points.Count);
        }

        for (int index = pointCount - 1; index >= 0; --index)
        {
            _pointListWithSeparator.TypedElements[index] = _points[index];
            _points.RemoveAt(index); // clear while we're here
        }

        ResultList.Value = _pointListWithSeparator;
    }

    private float GetHorizontalLineOffset(string text, int indexAtLineStart, HorizontalAligns horizontalAligns, float characterSpacing)
    {
        if (_lineFont == null)
            return 0;
        
        var widthSum = 0f;

        for (var index = indexAtLineStart; index < text.Length; index++)
        {
            var c = text[index];
            if (c == '\n')
            {
                break;
            }

            var glyph = _lineFont?.GetGlyphForCharacter(c);
            if (glyph == null)
                continue;

            widthSum += glyph.AdvanceX + characterSpacing * MathF.Abs(_lineFont!.UnitsPerEm) * 0.01f;
        }

        if (horizontalAligns == HorizontalAligns.Center)
            return -widthSum / 2;

        if (horizontalAligns == HorizontalAligns.Right)
            return -widthSum;

        return 0;
    }

    private readonly List<Point> _points = new(1000);
    private StructuredList<Point> _pointListWithSeparator = new(1);

    public enum HorizontalAligns
    {
        Left,
        Center,
        Right,
    }

    public enum VerticalAligns
    {
        Top,
        Middle,
        Bottom,
    }

    [Input(Guid = "8EE43C95-869E-4390-A567-CB0BB9C31BDD")]
    public readonly InputSlot<string> Text = new();

    [Input(Guid = "1077857C-93FA-403A-BBD1-FD5B2B402AE6")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "FB14EABB-B3C8-437C-B083-EBC5E0A46F7B")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "EB22073E-2348-4E76-A096-A4F6F80E2C01")]
    public readonly InputSlot<float> LineHeight = new();

    [Input(Guid = "EEF1217E-E719-4C32-9A8A-5E7ACBE1A185", MappedType = typeof(VerticalAligns))]
    public readonly InputSlot<int> VerticalAlign = new();

    [Input(Guid = "4C275222-D85C-4037-8161-536105A6C80F", MappedType = typeof(HorizontalAligns))]
    public readonly InputSlot<int> HorizontalAlign = new();

    [Input(Guid = "D9CD13D4-C6A3-42DE-9119-5A164CB12C41")]
    public readonly InputSlot<float> Spacing = new();

    [Input(Guid = "05ACBA8D-1DD1-4D6C-BEFF-E11E466F7253")]
    public readonly InputSlot<string> FilePath = new();

    [Input(Guid = "77B18D33-A41E-4676-A55C-0DF8E0290D05")]
    public readonly InputSlot<float> ReduceCurveThreshold = new();

    [Input(Guid = "96F345D9-2E38-4AF6-937D-EE14528727E4")]
    public readonly InputSlot<float> CornerWeightBalance = new();

    [Input(Guid = "D0B12D48-75A6-4DC5-A634-4D969EC20A29")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "72324D06-1A8E-44F1-A946-9B0E7987908E")]
    public readonly InputSlot<float> LineWidth = new();

    [Input(Guid = "585A1124-1505-49BB-8E42-9F3E9B51845C", MappedType = typeof(Entities))]
    public readonly InputSlot<int> SetFx1To = new();

    private enum Entities
    {
        None,
        Characters,
        Words,
        Lines,
    }
    
    
    
    private LineFont? _lineFont;
    private readonly Resource<SvgDocument> _svgResource;
    private LineFontDefinition? _lineFontKey;
}

internal record struct LineFontDefinition(string Key, float ReduceCurveThreshold, float CornerBalance);
        
internal sealed class LineFont
{
    private const int GlyphTableLength = 256;
    private Glyph[] _glyphTable = []; // Lookup table for fast lookup
    private readonly Dictionary<char, Glyph> _glyphsForCharacters = new(); // Fallback dictionary

    public Glyph? GetGlyphForCharacter(char c)
    {
        var value = (int)c;
        if (value < GlyphTableLength)
            return _glyphTable[value];

        Log.Debug($"need dictionary for {c}", this);
        return _glyphsForCharacters.TryGetValue(c, out var glyph) ? glyph : null;
    }

    public void InitGlyphTable()
    {
        _glyphTable = new Glyph[GlyphTableLength];

        foreach (var (c, glyph) in _glyphsForCharacters)
        {
            var value = (int)c;
            if (c >= GlyphTableLength)
                continue;

            _glyphTable[value] = glyph;
        }
    }

    // ReSharper disable once CollectionNeverUpdated.Global
    public readonly Dictionary<int, float> KerningForPairs = new();
    public float UnitsPerEm = 1000;
    public float Ascent = 800;
    public float Descent = -200;
    public float CapHeight = 500;
    public float XHeight = 300;
    public float LineHeight = 1000; // identical with FontSize
    public float ReduceCurveThreshold = 0.2f;
    public float CornerWeightBalance = 0.5f;
        

    public static LineFont Create(SvgDocument svgDoc, LineFontDefinition? previousKey, LineFontDefinition key, object user)
    {
        if (svgDoc == null)
            throw new ArgumentNullException(nameof(svgDoc));
            
        if (previousKey == key)
        {
            return _definitionsForFilePaths[previousKey.Value];
        }

        if (previousKey.HasValue && _users.TryGetValue(previousKey.Value, out var previousUsers))
        {
            previousUsers.Remove(user);
            
            if (previousUsers.Count == 0)
            {
                _users.Remove(previousKey.Value);
                _definitionsForFilePaths.Remove(previousKey.Value);
            }
        }
            
        if (_definitionsForFilePaths.TryGetValue(key, out var definition))
        {
            if (_users.TryGetValue(key, out var users))
            {
                users.Add(user);
            }
            else
            {
                _users[key] = [user];
            }
                
            return definition;
        }

        var svgElementCollection = svgDoc.Children;
        var newLineFont = new LineFont()
                              {
                                  ReduceCurveThreshold = key.ReduceCurveThreshold,
                                  CornerWeightBalance = key.CornerBalance,
                              };
            
        ParseFontDefinitionsInElements(svgElementCollection, newLineFont);

        newLineFont.InitGlyphTable();

        _definitionsForFilePaths[key] = newLineFont;
        return newLineFont;
    }

    private static void ParseFontDefinitionsInElements(SvgElementCollection svgElementCollection,
                                                       LineFont font)
    {
        foreach (var svgDocChild in svgElementCollection)
        {
            switch (svgDocChild)
            {
                case SvgFont svgFont:
                    CollectGlyphsFromSvgFont(svgFont, font);
                    break;
                case SvgDefinitionList svgDefinitionList:
                    ParseFontDefinitionsInElements(svgDefinitionList.Children, font);
                    break;
            }
        }
    }

    private static void CollectGlyphsFromSvgFont(SvgFont svgFont, LineFont font)
    {
        var fontFace = svgFont.Children.SingleOrDefault(c => c is SvgFontFace);
        if (fontFace is SvgFontFace svgFontFace)
        {
            font.Descent = svgFontFace.Descent;
            font.Ascent = svgFontFace.Ascent;
            font.XHeight = svgFontFace.XHeight;
            font.UnitsPerEm = svgFontFace.UnitsPerEm;
        }

        font.LineHeight = svgFont.FontSize != 0 ? svgFont.FontSize : font.UnitsPerEm;

        foreach (var svgFontChild in svgFont.Children)
        {
            if (svgFontChild is not SvgGlyph svgGlyph)
                continue;
            
            var points = GetPointsFromSvgGroup(font, svgGlyph);
            if (points.Length == 0)
                continue;

            if (string.IsNullOrEmpty(svgGlyph.Unicode) || svgGlyph.Unicode.Length != 1)
            {
                Log.Warning("Skipping svg glyph with missing or invalid unicode:" + svgGlyph.GlyphName);
                continue;
            }

            var uniCode = svgGlyph.Unicode[0];
            var glyphValue = (int)uniCode;

            // There are some exporters that automatically assign a "icon" range to the Id.
            // If that's the case we fall back to the GlyphName.
            if (glyphValue > 1000 && svgGlyph.GlyphName?.Length == 1)
            {
                uniCode = svgGlyph.GlyphName[0];
            }

            font._glyphsForCharacters[uniCode] = new Glyph
                                                     {
                                                         Points = points,
                                                         UniCode = "" + uniCode,
                                                         Name = svgGlyph.GlyphName!,
                                                         AdvanceX = svgGlyph.HorizAdvX,
                                                         VertOriginX = svgGlyph.VertOriginX,
                                                         VertOriginY = svgGlyph.VertOriginY,
                                                         BoundsMin = new Vector2(svgGlyph.Bounds.Left, svgGlyph.Bounds.Top),
                                                         BoundsMax = new Vector2(svgGlyph.Bounds.Right, svgGlyph.Bounds.Bottom),
                                                     };
        }
    }

    private static Point[] GetPointsFromSvgGroup(LineFont font, SvgElement svgGlyph)
    {
        var svgElements = new List<SvgElement> { svgGlyph };
        var pathElements = GraphicsPathEntry.CreateFromSvgElements(svgElements, true);

        // Flatten and sum total point count including separators 
            

        _tempPoints.Clear();
        foreach (var pathElement in pathElements)
        {
            try
            {
                pathElement.GraphicsPath?.Flatten(null, font.ReduceCurveThreshold);
                _ = pathElement.GraphicsPath?.PathPoints.Length; // Access path points to see if result is valid. 
            }
            catch (Exception e)
            {
                Log.Debug("Can't flatten element" + e.Message);
                continue;
            }

            var path = pathElement.GraphicsPath;
            if (path == null)
                continue;

            var pathPointCount = path.PathPoints.Length;

            var loopStartIndex = _tempPoints.Count;
            var lastPos = new Vector3(-9999f, 0f, 0f);

            for (var pathPointIndex = 0; pathPointIndex < pathPointCount; pathPointIndex++)
            {
                var point = path.PathPoints[pathPointIndex];

                var position = (new Vector3(point.X, 1 - point.Y, 0));
                var length = (lastPos - position).LengthSquared();
                lastPos = position;
                var tooClose = length < 0.000001f;
                if (tooClose)
                {
                    continue;
                }

                _tempPoints.Add(new Point
                                    {
                                        Position = position,
                                        F1 = 1,
                                        Orientation = Quaternion.Identity,
                                        Color = Vector4.One,
                                        Scale = Vector3.One,
                                        F2 = 1,
                                    });
            }

            // Close loop?
            if (pathElement.NeedsClosing)
            {
                _tempPoints.Add(_tempPoints[loopStartIndex]);
            }

            _tempPoints.Add(Point.Separator());
        }
            
        var points = _tempPoints.ToArray();
        var flip = font.UnitsPerEm < 0 ? 1 : -1;
        // Calc point orientation
        {
            var p0 = Vector3.Zero;
            var p0Valid = false;
            var p1 = Vector3.Zero;
            var p1Valid = false;
            var p2 = Vector3.Zero;
            var p2Valid = false;
            for (var pointIndex = 0; pointIndex <= points.Length + 2; pointIndex++)
            {
                p0Valid = pointIndex >= 0 && pointIndex < points.Length  && !float.IsNaN(points[pointIndex].F1);
                if (p0Valid)
                    p0 = points[pointIndex].Position;
                    
                if (p1Valid)
                {
                    if (p0Valid && p2Valid)
                    {
                        var v1 = Vector3.Normalize(p0-p1) - Vector3.Normalize(p2-p1);
                        var v2 = Vector3.Normalize(p0 - p2);
                        var v = Vector3.Lerp(v1, v2, font.CornerWeightBalance);
                            
                        var angle = -MathF.Atan2(v.X, v.Y);
                        points[pointIndex-1].Orientation= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle * flip);
                    }
                    else if (p2Valid)
                    {
                        var v = p1 - p2;
                        var p =points[pointIndex-1];
                        var angle = -MathF.Atan2(v.X, v.Y) ;
                        points[pointIndex-1].Orientation= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle * flip );
                    }
                    else if (p0Valid)
                    {
                        var v = p0 - p1;
                        var p =points[pointIndex-1];
                        var angle = -MathF.Atan2(v.X, v.Y) ;
                        points[pointIndex-1].Orientation= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle * flip );
                    }
                }

                p2Valid = p1Valid;
                p2 = p1;
                p1Valid = p0Valid;
                p1 = p0;
            }
        }            
            
        return points;
    }

    private static readonly Dictionary<LineFontDefinition, LineFont> _definitionsForFilePaths = new();
    private static readonly Dictionary<LineFontDefinition, HashSet<object>> _users = new();

    private static readonly List<Point> _tempPoints = new(100); // Reuse to avoid GC

    internal sealed class Glyph
    {
        //public char Char;
        public float AdvanceX = 10;
        public Point[] Points = [];
        public string UniCode = string.Empty;
        public string Name = string.Empty;
        public float VertOriginX;
        public float VertOriginY;
        public Vector2 BoundsMin;
        public Vector2 BoundsMax;
    }
}

internal sealed class GraphicsPathEntry
{
    public GraphicsPath? GraphicsPath;
    public bool NeedsClosing;

    public static List<GraphicsPathEntry> CreateFromSvgElements(IEnumerable<SvgElement> nodes, bool importAsLines)
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
                    ConvertPathDataElements(svgPath.PathData, newPath, paths);
                    break;
                }

                case SvgGlyph svgGlyph:
                {
                    ConvertPathDataElements(svgGlyph.PathData, newPath, paths);
                    break;
                }

                case SvgGroup:
                    break;

                case SvgPathBasedElement element:
                {
                    if (element is SvgRectangle { Transforms: not null } rect)
                    {
                        foreach (var t in rect.Transforms)
                        {
                            if (t is not SvgTranslate tr)
                                continue;

                            rect.X += tr.X;
                            rect.Y += tr.Y;
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

    private static void ConvertPathDataElements(SvgPathSegmentList? svgPathSegmentList, GraphicsPath? newPath, List<GraphicsPathEntry> paths)
    {
        if (svgPathSegmentList == null)
            return;

        foreach (var s in svgPathSegmentList)
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
    }

    private static ISvgRenderer? _svgRenderer;
}