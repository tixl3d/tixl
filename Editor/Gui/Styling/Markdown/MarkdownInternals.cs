#nullable enable

namespace T3.Editor.Gui.Styling.Markdown;

internal enum LineKind : byte
{
    Paragraph,
    H1,
    H2,
    H3,
    Bullet,
    Numbered,
    Blank
}

[Flags]
internal enum RunStyle : byte
{
    None = 0,
    Bold = 1 << 0,
    Code = 1 << 1,
    Link = 1 << 2,
    OpRef = 1 << 3
}

internal readonly struct Slice
{
    public readonly int Start;
    public readonly int Length;

    public Slice(int start, int length)
    {
        Start = start;
        Length = length;
    }
}

internal readonly struct Run
{
    public readonly Slice Text;
    public readonly RunStyle Style;
    public readonly int UrlIndex;

    public Run(Slice text, RunStyle style, int urlIndex)
    {
        Text = text;
        Style = style;
        UrlIndex = urlIndex;
    }
}

internal struct Line
{
    public LineKind Kind;
    public byte IndentLevel;
    public int Number;
    public int RunStart;
    public int RunCount;
}

internal sealed class ParsedDoc
{
    public string Source = string.Empty;
    public readonly List<Line> Lines = new(64);
    public readonly List<Run> Runs = new(128);
    public readonly List<Slice> Urls = new(8);

    public void Reset(string source)
    {
        Source = source;
        Lines.Clear();
        Runs.Clear();
        Urls.Clear();
    }
}

internal readonly struct Fragment
{
    public readonly string Text;
    public readonly RunStyle Style;
    public readonly int UrlIndex;

    public Fragment(string text, RunStyle style, int urlIndex)
    {
        Text = text;
        Style = style;
        UrlIndex = urlIndex;
    }
}

internal struct LineBox
{
    public float Y;
    public float H;
    public LineKind Kind;
    public int IndentLevel;
    public bool DrawMarker;
    public string? MarkerText;
    public int FragmentStart;
    public int FragmentCount;
}

internal sealed class LayoutResult
{
    public readonly List<LineBox> Boxes = new(64);
    public readonly List<Fragment> Fragments = new(128);
    public readonly List<string> Urls = new(8);
    public float TotalHeight;

    public void Reset()
    {
        Boxes.Clear();
        Fragments.Clear();
        Urls.Clear();
        TotalHeight = 0;
    }
}
