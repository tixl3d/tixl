#nullable enable
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
#if SIXLABORS_FONTS_V3
using SixLabors.Fonts.Rendering;
#endif
using T3.Core.Utils;

namespace Lib.geometry;

/// <summary>
/// Lays out a string with a TrueType/OpenType font and emits the glyph outlines as
/// CurveGeometry: one part per glyph (pivot at its origin), closed contours of cubic
/// beziers, with per-glyph attributes for downstream selection and styling.
/// </summary>
[Guid("e2b7f4a1-3c69-4d58-8a1e-9f6c0d2b5e37")]
[ExportDependencies("SixLabors.Fonts.dll")]
internal sealed class TextToCurves : Instance<TextToCurves>, IDescriptiveFilename, IStatusProvider
{
    [Output(Guid = "5d3f9b27-a8c1-4e60-b2d4-7e9f1c6a3b85")]
    public readonly Slot<CurveGeometry?> Curves = new();

    [Output(Guid = "9a6e2c48-d1b5-4f73-8c0e-3b7d5a2f9e16")]
    public readonly Slot<int> GlyphCount = new();

    public TextToCurves()
    {
        _resource = new Resource<LoadedFont>(Path, TryLoadFont, allowDisposal: false);
        _resource.AddDependentSlots(Curves, GlyphCount);
        Curves.UpdateAction = Update;
        GlyphCount.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var text = Text.GetValue(context) ?? string.Empty;
        var size = MathF.Max(Size.GetValue(context), 1e-4f);
        var lineSpacing = LineSpacing.GetValue(context);
        var alignment = (Alignments)Alignment.GetValue(context).Clamp(0, 2);
        var kerning = Kerning.GetValue(context);
        var weight = Weight.GetValue(context);
        var axisTag = Axis.GetValue(context)?.Trim() ?? string.Empty;
        var axisValue = AxisValue.GetValue(context);
        var pivot = Pivot.GetValue(context);

        if (!_resource.TryGetValue(context, out var loadedFont))
        {
            _warningMessage = $"Failed loading font {Path.Value}";
            Curves.Value = null;
            GlyphCount.Value = 0;
            return;
        }

        _warningMessage = string.Empty;
        if (text.Length == 0)
        {
            Curves.Value = null;
            GlyphCount.Value = 0;
            return;
        }

        // Dpi 72 makes one point one unit, so Size is the em height in scene units.
#if SIXLABORS_FONTS_V3
        // Variation axes only apply when the font has them; static fonts ignore the values.
        _variations.Clear();
        if (loadedFont.HasAxis(KnownVariationAxes.Weight) && weight > 0)
            _variations.Add(new FontVariation(KnownVariationAxes.Weight, weight));

        if (axisTag.Length == 4 && loadedFont.HasAxis(axisTag))
            _variations.Add(new FontVariation(axisTag, axisValue));

        var font = _variations.Count > 0
                       ? loadedFont.Family.CreateFont(size, _variations.ToArray())
                       : loadedFont.Family.CreateFont(size);
#else
        // SixLabors.Fonts 2.x has no variable-font support: Weight and Axis are accepted but have no effect
        _ = weight; _ = axisTag; _ = axisValue;
        var font = loadedFont.Family.CreateFont(size);
#endif
        var options = new TextOptions(font)
        {
            Dpi = 72,
            LineSpacing = lineSpacing,
            KerningMode = kerning ? KerningMode.Standard : KerningMode.None,
            HorizontalAlignment = alignment switch
            {
                Alignments.Center => HorizontalAlignment.Center,
                Alignments.Right  => HorizontalAlignment.Right,
                _                 => HorizontalAlignment.Left,
            },
            TextAlignment = alignment switch
            {
                Alignments.Center => TextAlignment.Center,
                Alignments.Right  => TextAlignment.End,
                _                 => TextAlignment.Start,
            },
        };

        _collector.Begin(text, pivot);
        try
        {
#if SIXLABORS_FONTS_V3
            TextRenderer.RenderTo(_collector, text, options);
#else
            TextRenderer.RenderTextTo(_collector, text, options);
#endif
        }
        catch (Exception e)
        {
            _warningMessage = $"Text layout failed: {e.Message}";
            Log.Warning(_warningMessage, this);
            Curves.Value = null;
            GlyphCount.Value = 0;
            return;
        }

        _collector.Finish(_output);
        Curves.Value = _output;
        GlyphCount.Value = _output.Parts.Length;
    }

    private bool TryLoadFont(FileResource file, LoadedFont? currentValue, [NotNullWhen(true)] out LoadedFont? newValue,
                             [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            var collection = new FontCollection();
            var family = collection.Add(file.AbsolutePath);
            var axes = new HashSet<string>();
#if SIXLABORS_FONTS_V3
            if (family.TryGetMetrics(FontStyle.Regular, out var metrics) && metrics.TryGetVariationAxes(out var variationAxes))
            {
                foreach (var axis in variationAxes.Span)
                {
                    axes.Add(axis.Tag.ToString());
                }
            }
#endif

            newValue = new LoadedFont(collection, family, axes);
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            failureReason = $"Can't read font {file.AbsolutePath}: {e.Message}";
            Log.Warning(failureReason, this);
            newValue = null;
            return false;
        }
    }

    /// <summary>The collection keeps the parsed tables alive; the family is what fonts are created from.</summary>
    private sealed record LoadedFont(FontCollection Collection, FontFamily Family, HashSet<string> Axes)
    {
        public bool HasAxis(string tag) => Axes.Contains(tag);
#if SIXLABORS_FONTS_V3
        public bool HasAxis(SixLabors.Fonts.Tables.AdvancedTypographic.Tag tag) => Axes.Contains(tag.ToString());
#endif
    }

    private enum Alignments
    {
        Left,
        Center,
        Right,
    }

    /// <summary>
    /// Receives the glyph outlines from the layout engine and turns them into contours
    /// of cubic beziers. Layout coordinates are y-down pixels; they are flipped so text
    /// reads upright in scene space. Quadratic segments (TrueType) are raised to cubics
    /// exactly. Per glyph the source character is found by walking the text in parallel,
    /// which yields character, word and line indices even across ligatures.
    /// </summary>
    private sealed class OutlineCollector : IGlyphRenderer
    {
        public void Begin(string text, Vector2 pivot)
        {
            _text = text;
            _pivot = pivot;
            _textCursor = 0;
            _wordIndex = 0;
            _lineIndex = 0;
            _positions.Clear();
            _handlesIn.Clear();
            _handlesOut.Clear();
            _contourOffsets.Clear();
            _contourOffsets.Add(0);
            _contourClosed.Clear();
            _parts.Clear();
            _codePoints.Clear();
            _glyphIds.Clear();
            _charIndices.Clear();
            _wordIndices.Clear();
            _lineIndices.Clear();
            _advances.Clear();
            _pivots.Clear();
        }

        public void Finish(CurveGeometry target)
        {
            target.Positions = _positions.ToArray();
            target.HandlesIn = _handlesIn.ToArray();
            target.HandlesOut = _handlesOut.ToArray();
            target.ContourOffsets = _contourOffsets.ToArray();
            target.ContourClosed = _contourClosed.ToArray();
            target.Parts = _parts.ToArray();

            var count = _parts.Count;
            target.Attributes.Clear();
            Fill(target.Attributes.GetOrCreate<int>(CurveAttributeNames.CodePoint, AttributeDomain.Part, count).Values, _codePoints);
            Fill(target.Attributes.GetOrCreate<int>(CurveAttributeNames.GlyphId, AttributeDomain.Part, count).Values, _glyphIds);
            Fill(target.Attributes.GetOrCreate<int>(CurveAttributeNames.CharIndex, AttributeDomain.Part, count).Values, _charIndices);
            Fill(target.Attributes.GetOrCreate<int>(CurveAttributeNames.WordIndex, AttributeDomain.Part, count).Values, _wordIndices);
            Fill(target.Attributes.GetOrCreate<int>(CurveAttributeNames.LineIndex, AttributeDomain.Part, count).Values, _lineIndices);
            Fill(target.Attributes.GetOrCreate<float>(CurveAttributeNames.Advance, AttributeDomain.Part, count).Values, _advances);
            target.InvalidateCaches();
        }

        private static void Fill<T>(T[] target, List<T> source)
        {
            for (var i = 0; i < source.Count; i++)
            {
                target[i] = source[i];
            }
        }

        public bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
        {
            _glyphContourStart = _contourOffsets.Count - 1;
            _glyphBounds = bounds;
            _glyphCodePoint = parameters.CodePoint.Value;
            _glyphId = parameters.GlyphId;
            AdvanceTextCursorTo(parameters.CodePoint);
            return true;
        }

#if SIXLABORS_FONTS_V3
        public void BeginLayer(Paint paint, FillRule fillRule)
        {
        }

        public void EndLayer()
        {
        }

        public void BeginGroup(CompositeMode mode)
        {
        }

        public void EndGroup()
        {
        }

        public void ArcTo(float radiusX, float radiusY, float rotation, bool largeArc, bool sweep, Vector2 point)
        {
            // Font outlines are lines and beziers; arcs only occur in SVG color glyphs, which we don't render
            LineTo(point);
        }

        public void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness, ReadOnlyMemory<float> dashPattern)
        {
        }
#else
        public void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
        {
        }
#endif

        public void EndGlyph()
        {
            var contourCount = _contourOffsets.Count - 1 - _glyphContourStart;
            if (contourCount == 0)
                return; // whitespace and control glyphs: layout advances, nothing to draw

            // Calculate pivot based on input PivotPosition value
            // Bounds are in em units. X is left, Y is bottom of the box.
            var x = _glyphBounds.Left;
            var yBottom = -_glyphBounds.Bottom; // Flipped for scene space

            var pivotX = x + _glyphBounds.Width * (_pivot.X + 1f) * 0.5f;
            var pivotY = yBottom + _glyphBounds.Height * (_pivot.Y + 1f) * 0.5f;

            var finalPivot = new Vector3(pivotX, pivotY, 0);

            _parts.Add(new CurvePart(_glyphContourStart, contourCount, finalPivot, _parts.Count, _charIndex));
            _codePoints.Add(_glyphCodePoint);
            _glyphIds.Add(_glyphId);
            _charIndices.Add(_charIndex);
            _wordIndices.Add(_wordIndex);
            _lineIndices.Add(_lineIndex);
            _advances.Add(_glyphBounds.Width);
        }

        public void BeginFigure()
        {
            _figureStart = _positions.Count;
        }

        public void MoveTo(Vector2 point)
        {
            AddAnchor(point);
        }

        public void LineTo(Vector2 point)
        {
            AddAnchor(point);
        }

        public void QuadraticBezierTo(Vector2 control, Vector2 point)
        {
            // Exact elevation: cubic handles at two thirds of the way to the quadratic control
            var last = _positions.Count - 1;
            var p0 = new Vector2(_positions[last].X, -_positions[last].Y);
            var c1 = p0 + (control - p0) * (2f / 3f);
            var c2 = point + (control - point) * (2f / 3f);
            _handlesOut[last] = Flip(c1);
            AddAnchor(point, handleIn: Flip(c2));
        }

        public void CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
        {
            var last = _positions.Count - 1;
            _handlesOut[last] = Flip(secondControlPoint);
            AddAnchor(point, handleIn: Flip(thirdControlPoint));
        }

        public void EndFigure()
        {
            var count = _positions.Count - _figureStart;
            if (count < 2)
            {
                // Degenerate figure - drop it
                _positions.RemoveRange(_figureStart, count);
                _handlesIn.RemoveRange(_figureStart, count);
                _handlesOut.RemoveRange(_figureStart, count);
                return;
            }

            // Fonts close explicitly by returning to the start; fold that into the closed flag
            var first = _positions[_figureStart];
            var last = _positions.Count - 1;
            if (Vector3.DistanceSquared(_positions[last], first) < 1e-10f)
            {
                _handlesIn[_figureStart] = _handlesIn[last];
                _positions.RemoveAt(last);
                _handlesIn.RemoveAt(last);
                _handlesOut.RemoveAt(last);
            }

            _contourOffsets.Add(_positions.Count);
            _contourClosed.Add(true);
        }

        public void BeginText(in FontRectangle bounds)
        {
        }

        public void EndText()
        {
        }

        public TextDecorations EnabledDecorations() => TextDecorations.None;

        private void AddAnchor(Vector2 point, Vector3? handleIn = null)
        {
            var position = Flip(point);
            _positions.Add(position);
            _handlesIn.Add(handleIn ?? position);
            _handlesOut.Add(position);
        }

        private static Vector3 Flip(Vector2 p) => new(p.X, -p.Y, 0);

        /// <summary>Moves the text cursor to the next occurrence of the glyph's code point, counting words and lines passed.</summary>
        private void AdvanceTextCursorTo(CodePoint codePoint)
        {
            var search = _textCursor;
            while (search < _text.Length)
            {
                if (System.Text.Rune.DecodeFromUtf16(_text.AsSpan(search), out var rune, out var consumed) != System.Buffers.OperationStatus.Done)
                {
                    consumed = 1;
                    rune = System.Text.Rune.ReplacementChar;
                }

                if (rune.Value == codePoint.Value)
                {
                    // Count the whitespace and line breaks skipped between the previous glyph and this one
                    for (var i = _textCursor; i < search; i++)
                    {
                        if (_text[i] == '\n')
                        {
                            _lineIndex++;
                            _wordIndex++;
                        }
                        else if (char.IsWhiteSpace(_text[i]) && (i == 0 || !char.IsWhiteSpace(_text[i - 1])))
                        {
                            _wordIndex++;
                        }
                    }

                    _charIndex = search;
                    _textCursor = search + consumed;
                    return;
                }

                search += consumed;
            }

            // Not found (ligature or fallback glyph): keep the running indices
        }

        private string _text = string.Empty;
        private int _textCursor;
        private int _charIndex;
        private int _wordIndex;
        private int _lineIndex;
        private int _figureStart;
        private int _glyphContourStart;
        FontRectangle _glyphBounds;
        private int _glyphCodePoint;
        private int _glyphId;
        private Vector2 _pivot;
       
        private readonly List<Vector3> _positions = [];
        private readonly List<Vector3> _handlesIn = [];
        private readonly List<Vector3> _handlesOut = [];
        private readonly List<int> _contourOffsets = [0];
        private readonly List<bool> _contourClosed = [];
        private readonly List<CurvePart> _parts = [];
        private readonly List<int> _codePoints = [];
        private readonly List<int> _glyphIds = [];
        private readonly List<int> _charIndices = [];
        private readonly List<int> _wordIndices = [];
        private readonly List<int> _lineIndices = [];
        private readonly List<float> _advances = [];
        private readonly List<Vector3> _pivots = [];
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_warningMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage()
    {
        return _warningMessage;
    }

    public InputSlot<string> SourcePathSlot => Path;

    private readonly Resource<LoadedFont> _resource;
    private readonly OutlineCollector _collector = new();
#if SIXLABORS_FONTS_V3
    private readonly List<FontVariation> _variations = [];
#endif
    private readonly CurveGeometry _output = new();
    private string _warningMessage = string.Empty;

    [Input(Guid = "1f8a3c5e-7d92-4b06-a4e1-c6b9d2f7a350")]
    public readonly InputSlot<string> Text = new();

    [Input(Guid = "7c2e9a4b-5f61-4d38-b8a0-e3d5c1f9b724")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "a5d1f7c3-8b24-4e69-9c0d-2f4a6e8b1d95")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "3e6b8d2a-c4f9-4a17-b5e3-8d0c7a2f6e41")]
    public readonly InputSlot<float> LineSpacing = new();

    [Input(Guid = "d9c4a6e2-1b83-4f50-a7e9-5c2d8b3f1a67", MappedType = typeof(Alignments))]
    public readonly InputSlot<int> Alignment = new();

    [Input(Guid = "6b3f1e9d-a2c5-4d84-8e7b-0f9a4c6d2e13")]
    public readonly InputSlot<bool> Kerning = new();

    [Input(Guid = "f4a9c2e7-6d18-4b53-9e0c-a7b1d5f3c826")]
    public readonly InputSlot<float> Weight = new();

    [Input(Guid = "8c5e1d7a-3f92-4a64-b0d8-6e2c9f4a1b57")]
    public readonly InputSlot<string> Axis = new();

    [Input(Guid = "2e7b4f9c-a1d6-4c38-8b5e-d9f0a3c7e614")]
    public readonly InputSlot<float> AxisValue = new();

    [Input(Guid = "4a7f2b9e-8c31-4d56-a0b2-c7e4f9d1a823")]
    public readonly InputSlot<Vector2> Pivot = new();

}
