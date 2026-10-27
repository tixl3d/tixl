#nullable enable
using System.Globalization;

namespace T3.Editor.Gui.MagGraph.Interaction.KeyboardEditing;

/// <summary>
/// Turns typed color names and hex codes into RGBA values for keyboard parameter entry.
/// </summary>
internal static class ColorValueParsing
{
    private readonly record struct NamedColor(string Name, Vector4 Color);

    /// <summary>
    /// "#f80", "#ff8800" and "#ff880080". Without "#" only when the text mixes digits and letters, so words like
    /// "bad" and numbers like "100" aren't read as colors.
    /// </summary>
    internal static bool TryParseHex(string text, out Vector4 color)
    {
        color = default;
        var hasHash = text.StartsWith('#');
        var hex = hasHash ? text[1..] : text;
        if (hex.Length is not (3 or 6 or 8) || (!hasHash && !(hex.Any(char.IsDigit) && hex.Any(char.IsLetter))))
            return false;

        if (hex.Length == 3)
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            return false;

        if (hex.Length == 6)
            packed = packed << 8 | 0xff;

        color = new Vector4((packed >> 24 & 0xff) / 255f,
                            (packed >> 16 & 0xff) / 255f,
                            (packed >> 8 & 0xff) / 255f,
                            (packed & 0xff) / 255f);
        return true;
    }

    /// <summary>
    /// Finds the first named color starting with the text. Names are ordered by how often they are needed, so
    /// short prefixes pick the common ones ("g" is gray, "gr" green needs "gre").
    /// </summary>
    internal static bool TryFindName(string text, out string name, out Vector4 color)
    {
        foreach (var namedColor in _namedColors)
        {
            if (!namedColor.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                continue;

            name = namedColor.Name;
            color = namedColor.Color;
            return true;
        }

        name = string.Empty;
        color = default;
        return false;
    }

    private static readonly NamedColor[] _namedColors =
        [
            new("white", new Vector4(1, 1, 1, 1)),
            new("black", new Vector4(0, 0, 0, 1)),
            new("gray", new Vector4(0.5f, 0.5f, 0.5f, 1)),
            new("red", new Vector4(1, 0, 0, 1)),
            new("green", new Vector4(0, 1, 0, 1)),
            new("blue", new Vector4(0, 0, 1, 1)),
            new("yellow", new Vector4(1, 1, 0, 1)),
            new("orange", new Vector4(1, 0.5f, 0, 1)),
            new("cyan", new Vector4(0, 1, 1, 1)),
            new("magenta", new Vector4(1, 0, 1, 1)),
            new("purple", new Vector4(0.5f, 0, 1, 1)),
            new("pink", new Vector4(1, 0.4f, 0.7f, 1)),
            new("brown", new Vector4(0.6f, 0.3f, 0.1f, 1)),
            new("teal", new Vector4(0, 0.5f, 0.5f, 1)),
            new("lime", new Vector4(0.5f, 1, 0, 1)),
            new("lightGray", new Vector4(0.75f, 0.75f, 0.75f, 1)),
            new("darkGray", new Vector4(0.25f, 0.25f, 0.25f, 1)),
            new("grey", new Vector4(0.5f, 0.5f, 0.5f, 1)),
            new("transparent", new Vector4(0, 0, 0, 0)),
            new("invisibleWhite", new Vector4(1, 1, 1, 0)),
            new("invisibleBlack", new Vector4(0, 0, 0, 0)),
        ];
}
