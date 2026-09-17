#nullable enable
using System.Runtime.InteropServices;
using System.Threading;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Builds the geometry shown while the main thread is stalled: the last UI frame, dimmed, with a
/// message and a progress bar.
/// </summary>
/// <remarks>
/// <see cref="Build"/> runs on the watchdog thread while the main thread may be in the middle of an
/// ImGui frame, so nothing here may call ImGui. Text is laid out from a glyph snapshot instead.
/// </remarks>
internal static class StallOverlay
{
    internal sealed class LastLogMessage : ILogWriter
    {
        public ILogEntry.EntryLevel Filter { get; set; }

        public void ProcessEntry(ILogEntry entry)
        {
            _lastLogMessage = entry.Message;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Must be called on the main thread with the current ImGui context after the font atlas was (re)built.
    /// </summary>
    internal static void SnapshotFonts()
    {
        var titleFont = SnapshotFont(Fonts.FontBold);
        var detailFont = SnapshotFont(Fonts.FontSmall);
        var whitePixelUv = ImGui.GetIO().Fonts.TexUvWhitePixel;

        lock (_fontLock)
        {
            _titleFont = titleFont;
            _detailFont = detailFont;
            _whitePixelUv = whitePixelUv;
        }
    }

    /// <summary>
    /// Starts over with an empty progress bar. To be called once per takeover, before the first <see cref="Build"/>.
    /// </summary>
    /// <remarks>Needs no lock, because only the watchdog thread begins takeovers and builds.</remarks>
    internal static void BeginTakeover()
    {
        _estimatedSeconds = 0;
        _progress = 0;
    }

    /// <summary>
    /// Fills <see cref="Vertices"/> with non-indexed triangles. The first <see cref="ScreenVertexCount"/>
    /// vertices sample the captured frame, all following ones sample the font atlas.
    /// </summary>
    /// <returns>The total vertex count</returns>
    internal static int Build(Vector2 size, double takeoverSeconds)
    {
        _vertexCount = 0;
        AddQuad(Vector2.Zero, size, Vector2.Zero, Vector2.One, UiColors.ForegroundFull);

        lock (_fontLock)
        {
            if (_titleFont == null || _detailFont == null)
                return _vertexCount;

            var fade = GetFadeIn(takeoverSeconds);
            var scale = T3Ui.UiScaleFactor;

            AddRect(Vector2.Zero, size, UiColors.BackgroundFull.Fade(0.45f * fade));

            var hasActivity = MainThreadActivity.TryGetCurrent(out var activity);
            var title = hasActivity ? activity.Message : "Working...";

            var padding = 16 * scale;
            var panelSize = new Vector2(MathF.Min(460 * scale, size.X - 2 * padding),
                                        padding * 2 + _titleFont.LineHeight + _detailFont.LineHeight + 22 * scale);
            var panelMin = new Vector2(MathF.Floor((size.X - panelSize.X) * 0.5f),
                                       MathF.Floor((size.Y - panelSize.Y) * 0.5f));
            var contentMin = panelMin + new Vector2(padding);
            var contentWidth = panelSize.X - 2 * padding;

            AddRect(panelMin, panelMin + panelSize, UiColors.BackgroundFull.Fade(0.9f * fade));

            var textColor = UiColors.Text.Fade(fade);
            var mutedColor = UiColors.TextMuted.Fade(fade);

            // Work that comes in several steps opens a scope per step. The progress follows the whole
            // takeover instead, so that a following step does not send the bar back to the start.
            if (hasActivity && activity.EstimatedSeconds > _estimatedSeconds)
                _estimatedSeconds = activity.EstimatedSeconds;

            var estimatedSeconds = _estimatedSeconds > 0 ? _estimatedSeconds : DefaultEstimateSeconds;

            // Once the stall outlasts the estimate, the elapsed time is the only honest progress left.
            var showsElapsed = takeoverSeconds > estimatedSeconds;
            var elapsedWidth = 0f;
            if (showsElapsed)
            {
                var length = FormatSeconds(takeoverSeconds);
                var elapsedText = _numberBuffer.AsSpan(0, length);
                elapsedWidth = MeasureText(_detailFont, elapsedText) + 8 * scale;
                AddText(_detailFont, elapsedText,
                        contentMin + new Vector2(contentWidth - elapsedWidth + 8 * scale, _titleFont.LineHeight - _detailFont.LineHeight),
                        float.MaxValue, mutedColor);
            }

            AddText(_titleFont, title, contentMin, contentWidth - elapsedWidth, textColor);

            var barMin = contentMin + new Vector2(0, _titleFont.LineHeight + 8 * scale);
            var barSize = new Vector2(contentWidth, 3 * scale);
            AddRect(barMin, barMin + barSize, UiColors.ForegroundFull.Fade(0.12f * fade));

            // Approaches but never reaches the end, because the estimate is only an average. A later
            // step raising the estimate would pull the bar back, so it is kept monotonic.
            var progress = (float)(1 - Math.Exp(-ProgressApproachRate * takeoverSeconds / estimatedSeconds));
            _progress = MathF.Max(_progress, MathF.Min(progress, MaxProgress));

            AddProgressFill(barMin, new Vector2(barSize.X * _progress, barSize.Y), textColor,
                            (float)(takeoverSeconds % 1.0), ShimmerWaveLength * scale, SegmentWidth * scale);

            var logMessage = _lastLogMessage;
            if (!string.IsNullOrEmpty(logMessage))
            {
                var firstLine = logMessage.AsSpan();
                var newlineIndex = firstLine.IndexOf('\n');
                if (newlineIndex >= 0)
                    firstLine = firstLine[..newlineIndex];

                AddText(_detailFont, firstLine.Trim(), barMin + new Vector2(0, barSize.Y + 8 * scale), contentWidth, mutedColor);
            }
        }

        return _vertexCount;
    }

    /// <summary>
    /// Eases in, so that a stall short enough to pass unnoticed never flashes a panel.
    /// </summary>
    internal static float GetFadeIn(double takeoverSeconds)
    {
        var t = (float)Math.Clamp(takeoverSeconds / FadeInSeconds, 0, 1);
        return t * t * (3 - 2 * t);
    }

    internal const int ScreenVertexCount = 6;
    internal static readonly ImDrawVert[] Vertices = new ImDrawVert[6 * 1024];

    private static unsafe FontSnapshot SnapshotFont(ImFontPtr font)
    {
        var glyphs = new Glyph[LastSnapshotChar - FirstSnapshotChar + 1];
        for (var c = FirstSnapshotChar; c <= LastSnapshotChar; c++)
        {
            // ImGui.NET declares the native flags bitfield as three separate fields, which shifts
            // all following members. So the native layout is read directly.
            var native = (NativeGlyph*)font.FindGlyph(c).NativePtr;
            glyphs[c - FirstSnapshotChar] = new Glyph(new Vector2(native->X0, native->Y0), new Vector2(native->X1, native->Y1),
                                                      new Vector2(native->U0, native->V0), new Vector2(native->U1, native->V1),
                                                      native->AdvanceX, (native->Flags & NativeGlyph.VisibleFlag) != 0);
        }

        return new FontSnapshot(glyphs, font.FontSize);
    }

    /// <summary>Memory layout of ImFontGlyph.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGlyph
    {
        public uint Flags;
        public float AdvanceX;
        public float X0, Y0, X1, Y1;
        public float U0, V0, U1, V1;

        public const uint VisibleFlag = 1 << 1;
    }

    private static float MeasureText(FontSnapshot font, ReadOnlySpan<char> text)
    {
        var width = 0f;
        foreach (var c in text)
        {
            width += font.GetGlyph(c).AdvanceX;
        }

        return width;
    }

    private static void AddText(FontSnapshot font, ReadOnlySpan<char> text, Vector2 position, float maxWidth, Color color)
    {
        var x = 0f;
        var origin = new Vector2(MathF.Floor(position.X), MathF.Floor(position.Y));
        foreach (var c in text)
        {
            ref readonly var glyph = ref font.GetGlyph(c);
            if (x + glyph.AdvanceX > maxWidth)
                break;

            if (glyph.IsVisible)
            {
                var glyphOrigin = origin + new Vector2(x, 0);
                AddQuad(glyphOrigin + glyph.Min, glyphOrigin + glyph.Max, glyph.UvMin, glyph.UvMax, color);
            }

            x += glyph.AdvanceX;
        }
    }

    /// <summary>
    /// Draws the filled part of the progress bar with a brightness wave travelling right, so that the
    /// bar keeps moving while the progress itself barely does.
    /// </summary>
    /// <remarks>
    /// The wave is approximated by strips whose corner colors the rasterizer interpolates between.
    /// </remarks>
    private static void AddProgressFill(Vector2 min, Vector2 size, Color color, float phase, float waveLength, float segmentWidth)
    {
        if (size.X <= 0)
            return;

        var segmentCount = Math.Clamp((int)(size.X / segmentWidth), 1, MaxProgressSegments);
        var actualWidth = size.X / segmentCount;
        var leftColor = color.Fade(GetShimmer(min.X, phase, waveLength));

        for (var i = 0; i < segmentCount; i++)
        {
            var right = min.X + (i + 1) * actualWidth;
            var rightColor = color.Fade(GetShimmer(right, phase, waveLength));
            AddQuad(new Vector2(right - actualWidth, min.Y), new Vector2(right, min.Y + size.Y),
                    _whitePixelUv, _whitePixelUv, leftColor, rightColor);
            leftColor = rightColor;
        }
    }

    /// <summary>A crest sits wherever <paramref name="x"/> has travelled a full wave length.</summary>
    private static float GetShimmer(float x, float phase, float waveLength)
    {
        return 1 - ShimmerDepth * (0.5f + 0.5f * MathF.Cos(MathF.Tau * (x / waveLength - phase)));
    }

    private static void AddRect(Vector2 min, Vector2 max, Color color)
    {
        AddQuad(min, max, _whitePixelUv, _whitePixelUv, color);
    }

    private static void AddQuad(Vector2 min, Vector2 max, Vector2 uvMin, Vector2 uvMax, Color color)
    {
        AddQuad(min, max, uvMin, uvMax, color, color);
    }

    private static void AddQuad(Vector2 min, Vector2 max, Vector2 uvMin, Vector2 uvMax, Color leftColor, Color rightColor)
    {
        if (_vertexCount + 6 > Vertices.Length)
            return;

        uint packedLeft = leftColor;
        uint packedRight = rightColor;
        var topLeft = new ImDrawVert { pos = min, uv = uvMin, col = packedLeft };
        var topRight = new ImDrawVert { pos = new Vector2(max.X, min.Y), uv = new Vector2(uvMax.X, uvMin.Y), col = packedRight };
        var bottomRight = new ImDrawVert { pos = max, uv = uvMax, col = packedRight };
        var bottomLeft = new ImDrawVert { pos = new Vector2(min.X, max.Y), uv = new Vector2(uvMin.X, uvMax.Y), col = packedLeft };

        Vertices[_vertexCount++] = topLeft;
        Vertices[_vertexCount++] = topRight;
        Vertices[_vertexCount++] = bottomRight;
        Vertices[_vertexCount++] = topLeft;
        Vertices[_vertexCount++] = bottomRight;
        Vertices[_vertexCount++] = bottomLeft;
    }

    /// <summary>Writes e.g. "12s" into <see cref="_numberBuffer"/> without allocating.</summary>
    private static int FormatSeconds(double seconds)
    {
        ((int)seconds).TryFormat(_numberBuffer, out var length);
        _numberBuffer[length++] = 's';
        return length;
    }

    private readonly record struct Glyph(Vector2 Min, Vector2 Max, Vector2 UvMin, Vector2 UvMax, float AdvanceX, bool IsVisible);

    private sealed class FontSnapshot(Glyph[] glyphs, float lineHeight)
    {
        public readonly float LineHeight = lineHeight;

        public ref readonly Glyph GetGlyph(char c)
        {
            var index = c is >= FirstSnapshotChar and <= LastSnapshotChar ? c - FirstSnapshotChar : FallbackChar - FirstSnapshotChar;
            return ref glyphs[index];
        }
    }

    private const char FirstSnapshotChar = (char)32;
    private const char LastSnapshotChar = (char)255;
    private const char FallbackChar = '?';
    private const double FadeInSeconds = 1.0;

    /// <summary>Used until the work has been timed once, so that the bar is never indeterminate.</summary>
    private const double DefaultEstimateSeconds = 5;

    /// <summary>Reaches half of the estimate in 40% of its time, then slows down.</summary>
    private const double ProgressApproachRate = 1.75;

    private const float MaxProgress = 0.97f;
    private const float ShimmerDepth = 0.25f;
    private const float ShimmerWaveLength = 90;
    private const float SegmentWidth = 8;
    private const int MaxProgressSegments = 128;

    private static readonly Lock _fontLock = new();
    private static readonly char[] _numberBuffer = new char[16];
    private static FontSnapshot? _titleFont;
    private static FontSnapshot? _detailFont;
    private static Vector2 _whitePixelUv;
    private static double _estimatedSeconds;
    private static float _progress;
    private static volatile string? _lastLogMessage;
    private static int _vertexCount;
}
