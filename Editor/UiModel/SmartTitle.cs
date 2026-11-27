#nullable enable
using System.Globalization;
using System.IO;
using System.Text;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Editor.UiModel;

/// <summary>
/// A symbol's title template, e.g. <c>* {B}</c>, that graph nodes show instead of the symbol name, filled in
/// with each child's parameter values. A custom child name still takes precedence.
/// </summary>
/// <remarks>
/// Placeholders reference inputs by id (<c>{guid}</c> or <c>{guid:format}</c>), so renaming an input keeps the
/// template intact. Users edit the name form (<c>{B:0.00}</c>), converted by <see cref="ToDisplayTemplate"/> and
/// <see cref="TryFromDisplayTemplate"/>. Immutable: an edit replaces the instance, which is how caches notice it.
/// </remarks>
internal sealed class SmartTitle
{
    private SmartTitle(string template, Segment[] segments, int placeholderCount)
    {
        Template = template;
        _segments = segments;
        _placeholderCount = placeholderCount;
    }

    /// <summary>The serialized form with input ids.</summary>
    internal string Template { get; }

    /// <returns>null for an empty template.</returns>
    internal static SmartTitle? FromTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var segments = new List<Segment>();
        var literal = new StringBuilder();
        var placeholderCount = 0;
        var index = 0;
        while (index < template.Length)
        {
            if (template[index] == '{'
                && TryReadPlaceholder(template, index, out var body, out var end)
                && Guid.TryParse(SplitFormat(body, out var format), out var inputId))
            {
                if (literal.Length > 0)
                {
                    segments.Add(new Segment(literal.ToString(), Guid.Empty, null));
                    literal.Clear();
                }

                segments.Add(new Segment(null, inputId, format));
                placeholderCount++;
                index = end + 1;
                continue;
            }

            literal.Append(template[index]);
            index++;
        }

        if (literal.Length > 0)
            segments.Add(new Segment(literal.ToString(), Guid.Empty, null));

        return new SmartTitle(template, segments.ToArray(), placeholderCount);
    }

    internal static SmartTitle ForInput(Guid inputId) => FromTemplate("{" + inputId + "}")!;

    /// <summary>True if the template is exactly one placeholder for this input, as set by "Use as Title".</summary>
    internal bool IsSingleInput(Guid inputId)
    {
        return _segments.Length == 1 && _segments[0].Literal == null && _segments[0].InputId == inputId && _segments[0].Format == null;
    }

    /// <summary>The name form shown for editing. Placeholders of inputs that no longer exist show as <c>{?}</c>.</summary>
    internal string ToDisplayTemplate(Symbol symbol)
    {
        var sb = new StringBuilder();
        foreach (var segment in _segments)
        {
            if (segment.Literal != null)
            {
                sb.Append(segment.Literal);
                continue;
            }

            sb.Append('{').Append(FindInputDefinition(symbol, segment.InputId)?.Name ?? "?");
            if (segment.Format != null)
                sb.Append(':').Append(segment.Format);
            sb.Append('}');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts the name form the user types into a template. A placeholder naming an input the symbol doesn't
    /// have is kept as literal text and reported in <paramref name="unknownName"/>.
    /// </summary>
    /// <returns>false if a placeholder names an unknown input.</returns>
    internal static bool TryFromDisplayTemplate(Symbol symbol, string displayTemplate, out SmartTitle? smartTitle, out string? unknownName)
    {
        unknownName = null;
        var sb = new StringBuilder();
        var index = 0;
        while (index < displayTemplate.Length)
        {
            if (displayTemplate[index] == '{' && TryReadPlaceholder(displayTemplate, index, out var body, out var end))
            {
                var name = SplitFormat(body, out var format).Trim();
                var inputDefinition = FindInputDefinition(symbol, name);
                if (inputDefinition != null)
                {
                    sb.Append('{').Append(inputDefinition.Id);
                    if (format != null)
                        sb.Append(':').Append(format);
                    sb.Append('}');
                }
                else
                {
                    unknownName ??= name;
                    sb.Append(displayTemplate, index, end - index + 1);
                }

                index = end + 1;
                continue;
            }

            sb.Append(displayTemplate[index]);
            index++;
        }

        smartTitle = FromTemplate(sb.ToString());
        return unknownName == null;
    }

    /// <summary>For a duplicated symbol, whose inputs got new ids.</summary>
    internal SmartTitle RemapInputIds(IReadOnlyDictionary<Guid, Guid> oldToNewIds)
    {
        var sb = new StringBuilder();
        foreach (var segment in _segments)
        {
            if (segment.Literal != null)
            {
                sb.Append(segment.Literal);
                continue;
            }

            var id = oldToNewIds.TryGetValue(segment.InputId, out var newId) ? newId : segment.InputId;
            sb.Append('{').Append(id);
            if (segment.Format != null)
                sb.Append(':').Append(segment.Format);
            sb.Append('}');
        }

        return FromTemplate(sb.ToString())!;
    }

    /// <summary>The title as a child with default values would show it, for previews.</summary>
    internal string FormatWithDefaults(Symbol symbol)
    {
        var sb = new StringBuilder();
        foreach (var segment in _segments)
        {
            if (segment.Literal != null)
            {
                sb.Append(segment.Literal);
                continue;
            }

            var inputDefinition = FindInputDefinition(symbol, segment.InputId);
            if (inputDefinition != null)
                AppendValue(sb, inputDefinition.DefaultValue, segment.Format, inputDefinition.Name);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Holds the resolved title of one graph node. It compares the referenced values every frame without
    /// allocating and only formats a new string when one of them changed.
    /// </summary>
    internal sealed class Cache
    {
        /// <returns>The title, or null if a referenced input is connected, animated or gone, or the title is empty.</returns>
        internal string? GetText(SmartTitle title, Symbol.Child child, Instance? instance)
        {
            var changed = !ReferenceEquals(title, _title);
            if (changed)
            {
                _title = title;
                if (_keys.Length != title._placeholderCount)
                    _keys = new ValueKey[title._placeholderCount];
            }

            var keyIndex = 0;
            var isAvailable = true;
            foreach (var segment in title._segments)
            {
                if (segment.Literal != null)
                    continue;

                var key = ReadKey(segment.InputId, child, instance);
                isAvailable &= key.IsAvailable;
                if (changed || !key.Equals(_keys[keyIndex]))
                {
                    _keys[keyIndex] = key;
                    changed = true;
                }

                keyIndex++;
            }

            if (!isAvailable)
                return null;

            if (changed)
                _text = title.Format(child);

            return _text;
        }

        private SmartTitle? _title;
        private ValueKey[] _keys = [];
        private string? _text;
    }

    private string? Format(Symbol.Child child)
    {
        var sb = new StringBuilder();
        foreach (var segment in _segments)
        {
            if (segment.Literal != null)
            {
                sb.Append(segment.Literal);
                continue;
            }

            if (child.Inputs.TryGetValue(segment.InputId, out var input))
                AppendValue(sb, GetEffectiveValue(input), segment.Format, input.Name);
        }

        var text = sb.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// The parameter window copies the default into <see cref="Symbol.Child.Input.Value"/> only while it draws
    /// that input, so a child left at its default may still hold a stale value.
    /// </summary>
    private static InputValue GetEffectiveValue(Symbol.Child.Input input) => input.IsDefault ? input.DefaultValue : input.Value;

    private static ValueKey ReadKey(Guid inputId, Symbol.Child child, Instance? instance)
    {
        if (!child.Inputs.TryGetValue(inputId, out var input))
            return default;

        if (instance?.GetInput(inputId) is { HasInputConnections: true })
            return default;

        if (child.Parent != null && child.Parent.Animator.IsAnimated(child.Id, inputId))
            return default;

        return GetEffectiveValue(input) switch
                   {
                       InputValue<float> f   => new ValueKey(new Vector4(f.Value, 0, 0, 0), null, true),
                       InputValue<int> i     => new ValueKey(new Vector4(i.Value, 0, 0, 0), null, true),
                       InputValue<bool> b    => new ValueKey(new Vector4(b.Value ? 1 : 0, 0, 0, 0), null, true),
                       InputValue<Vector2> v => new ValueKey(new Vector4(v.Value, 0, 0), null, true),
                       InputValue<Vector3> v => new ValueKey(new Vector4(v.Value, 0), null, true),
                       InputValue<Vector4> v => new ValueKey(v.Value, null, true),
                       InputValue<Int2> v    => new ValueKey(new Vector4(v.Value.X, v.Value.Y, 0, 0), null, true),
                       InputValue<Int3> v    => new ValueKey(new Vector4(v.Value.X, v.Value.Y, v.Value.Z, 0), null, true),
                       InputValue<string> s  => new ValueKey(default, s.Value, true),
                       _                     => new ValueKey(default, null, true),
                   };
    }

    private static void AppendValue(StringBuilder sb, InputValue value, string? format, string inputName)
    {
        try
        {
            switch (value)
            {
                case InputValue<float> f:
                    sb.Append(f.Value.ToString(format ?? DefaultFloatFormat, CultureInfo.InvariantCulture));
                    break;
                case InputValue<int> i:
                    sb.Append(i.Value.ToString(format ?? "G", CultureInfo.InvariantCulture));
                    break;
                case InputValue<bool> b:
                    // Reads naturally in templates like "Blur {Invert}".
                    if (b.Value)
                        sb.Append(inputName);
                    break;
                case InputValue<Vector2> v:
                    AppendComponents(sb, format, v.Value.X, v.Value.Y);
                    break;
                case InputValue<Vector3> v:
                    AppendComponents(sb, format, v.Value.X, v.Value.Y, v.Value.Z);
                    break;
                case InputValue<Vector4> v:
                    AppendComponents(sb, format, v.Value.X, v.Value.Y, v.Value.Z, v.Value.W);
                    break;
                case InputValue<Int2> v:
                    sb.Append(v.Value.X).Append(", ").Append(v.Value.Y);
                    break;
                case InputValue<Int3> v:
                    sb.Append(v.Value.X).Append(", ").Append(v.Value.Y).Append(", ").Append(v.Value.Z);
                    break;
                case InputValue<string> s:
                    AppendString(sb, s.Value, format);
                    break;
            }
        }
        catch (FormatException)
        {
            // An invalid format string from the template: show the value with the default format instead.
            if (format != null)
                AppendValue(sb, value, null, inputName);
        }
    }

    private static void AppendComponents(StringBuilder sb, string? format, params float[] components)
    {
        for (var index = 0; index < components.Length; index++)
        {
            if (index > 0)
                sb.Append(", ");
            sb.Append(components[index].ToString(format ?? DefaultVectorFormat, CultureInfo.InvariantCulture));
        }
    }

    private static void AppendString(StringBuilder sb, string? text, string? format)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (format == FileFormat)
            text = Path.GetFileName(text);

        // Multi-line texts read as one line on the node.
        if (text.AsSpan().IndexOfAny('\r', '\n') >= 0)
            text = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        if (text.Length > MaxStringLength)
            text = text[..(MaxStringLength - 1)] + "…";

        sb.Append(text);
    }

    private static bool TryReadPlaceholder(string text, int openIndex, out string body, out int closeIndex)
    {
        closeIndex = text.IndexOf('}', openIndex + 1);
        if (closeIndex < 0)
        {
            body = string.Empty;
            return false;
        }

        body = text.Substring(openIndex + 1, closeIndex - openIndex - 1);
        return body.Length > 0;
    }

    private static string SplitFormat(string body, out string? format)
    {
        var colonIndex = body.IndexOf(':');
        if (colonIndex < 0)
        {
            format = null;
            return body;
        }

        format = colonIndex < body.Length - 1 ? body[(colonIndex + 1)..] : null;
        return body[..colonIndex];
    }

    private static Symbol.InputDefinition? FindInputDefinition(Symbol symbol, Guid inputId)
    {
        foreach (var inputDefinition in symbol.InputDefinitions)
        {
            if (inputDefinition.Id == inputId)
                return inputDefinition;
        }

        return null;
    }

    private static Symbol.InputDefinition? FindInputDefinition(Symbol symbol, string name)
    {
        foreach (var inputDefinition in symbol.InputDefinitions)
        {
            if (string.Equals(inputDefinition.Name, name, StringComparison.OrdinalIgnoreCase))
                return inputDefinition;
        }

        return null;
    }

    /// <summary>Format for string inputs holding a path: shows only the file name.</summary>
    internal const string FileFormat = "file";

    private const string DefaultFloatFormat = "0.###";
    private const string DefaultVectorFormat = "0.##";
    private const int MaxStringLength = 80;

    private readonly Segment[] _segments;
    private readonly int _placeholderCount;

    /// <summary>Either literal text or a placeholder for an input.</summary>
    private readonly record struct Segment(string? Literal, Guid InputId, string? Format);

    /// <summary>What a placeholder showed last time, compared each frame to detect changes.</summary>
    private readonly record struct ValueKey(Vector4 Numbers, string? Text, bool IsAvailable);
}
