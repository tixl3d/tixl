#nullable enable

namespace T3.Editor.Gui.UiHelpers;

public class CSharpValidator
{
    public static bool IsValidNamespace(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.StartsWith('.') || input.EndsWith('.'))
            return false;

        var span = input.AsSpan();
        var lastDot = -1;

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '.')
            {
                if (i == lastDot + 1) return false; // Consecutive dots
                if (!IsValidIdentifier(span.Slice(lastDot + 1, i - lastDot - 1))) return false;
                lastDot = i;
            }
        }

        return IsValidIdentifier(span.Slice(lastDot + 1));
    }

    public static bool IsValidClassName(string className)
    {
        return IsValidIdentifier(className.AsSpan());
    }

    private static bool IsValidIdentifier(ReadOnlySpan<char> identifier)
    {
        if (identifier.IsEmpty || IsCSharpKeyword(identifier))
            return false;

        if (!IsValidIdentifierStart(identifier[0])) return false;

        for (var i = 1; i < identifier.Length; i++)
        {
            if (!IsValidIdentifierChar(identifier[i])) return false;
        }

        return true;
    }

    private static bool IsValidIdentifierStart(char c)
    {
        return char.IsLetter(c) || c == '_';
    }

    private static bool IsValidIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private static bool IsCSharpKeyword(ReadOnlySpan<char> identifier)
    {
        return _cSharpKeywords.Contains(identifier.ToString()); // Minimal allocation here
    }

    private static readonly HashSet<string> _cSharpKeywords
        = new(StringComparer.Ordinal)
              {
                  "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
                  "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
                  "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in",
                  "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator",
                  "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte",
                  "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
                  "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void",
                  "volatile", "while"
              };
}