#nullable enable
using System.Reflection;
using System.Text.RegularExpressions;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.InputUi.SimpleInputUis;

/// <summary>
/// The problem to show below a <see cref="StringInputUi.UsageType.Code"/> input. Ops that report it themselves
/// implement <see cref="IInputStatusProvider"/>. For template-based ops like the Custom* shaders, the error comes from
/// a child shader op compiling the spliced source: it is shown below the code input whose text contains the error
/// line (with the line counted within that input), otherwise below the op's first code input.
/// </summary>
internal static partial class CodeInputStatus
{
    public static bool TryGet(Instance instance, Guid inputId, out InputStatus status)
    {
        if (instance is IInputStatusProvider provider)
            return provider.TryGetInputStatus(inputId, out status);

        Update(instance);
        if (_message != null && _inputId == inputId)
        {
            status = new InputStatus(_message, _line);
            return true;
        }

        status = default;
        return false;
    }

    /** Once per frame and instance; the mapping is redone only when the error or the source changed. */
    private static void Update(Instance instance)
    {
        var frame = ImGui.GetFrameCount();
        if (ReferenceEquals(instance, _instance) && frame == _frame)
            return;

        _instance = instance;
        _frame = frame;
        if (!TryFindShaderError(instance, 0, out var rawMessage, out var source))
        {
            _message = null;
            return;
        }

        if (ReferenceEquals(rawMessage, _rawMessage) && ReferenceEquals(source, _source) && _message != null)
            return;

        _rawMessage = rawMessage;
        _source = source;
        MapToCodeInput(instance, rawMessage, source);
    }

    private static void MapToCodeInput(Instance instance, string rawMessage, string? source)
    {
        _message = rawMessage;
        _line = 0;
        _inputId = Guid.Empty;

        var lineMatch = LineRegex().Match(rawMessage);
        var errorLine = lineMatch.Success ? int.Parse(lineMatch.Groups[1].Value) : 0;
        var symbolUi = instance.GetSymbolUi();
        foreach (var definition in instance.Symbol.InputDefinitions)
        {
            if (!symbolUi.InputUis.TryGetValue(definition.Id, out var inputUi) || inputUi is not StringInputUi { Usage: StringInputUi.UsageType.Code })
                continue;

            if (_inputId == Guid.Empty)
                _inputId = definition.Id;

            if (errorLine == 0 || source == null || !TryGetText(instance, definition.Id, out var code))
                continue;

            var start = source.IndexOf(code, StringComparison.Ordinal);
            if (start < 0)
                continue;

            var firstLine = CountLines(source, start) + 1;
            var lineCount = CountLines(code, code.Length) + 1;
            if (errorLine < firstLine || errorLine >= firstLine + lineCount)
                continue;

            _inputId = definition.Id;
            _line = errorLine - firstLine + 1;
            _message = FormatMappedMessage(rawMessage, lineMatch, _line);
            return;
        }
    }

    /// <summary>
    /// The compilers report "Line N: text", then the file and an optional hint, each after a blank line. Mapped into the
    /// input, the "Failed to compile" prefix and the file only repeat what the field shows; the hint stays.
    /// </summary>
    private static string FormatMappedMessage(string rawMessage, Match lineMatch, int line)
    {
        var paragraphs = rawMessage[(lineMatch.Index + lineMatch.Length)..].Split("\n\n");
        var text = $"Line {line}:{paragraphs[0]}";
        return paragraphs.Length > 2 ? $"{text}\n{string.Join("\n", paragraphs, 2, paragraphs.Length - 2)}" : text;
    }

    /// <summary>A child shader op (up to two levels down) with a compile problem, and the source it compiled.</summary>
    private static bool TryFindShaderError(Instance instance, int depth, out string message, out string? source)
    {
        foreach (var child in instance.Children.Values)
        {
            var codeProperty = GetShaderCodeProperty(child.GetType());
            if (codeProperty != null
                && child is IStatusProvider statusProvider
                && statusProvider.GetStatusLevel() >= IStatusProvider.StatusLevel.Warning
                && statusProvider.GetStatusMessage() is { Length: > 0 } childMessage)
            {
                message = childMessage;
                source = (codeProperty.GetValue(child) as InputSlot<string>)?.Value;
                return true;
            }

            if (depth < MaxDepth && TryFindShaderError(child, depth + 1, out message, out source))
                return true;
        }

        message = string.Empty;
        source = null;
        return false;
    }

    /** The Code slot of an IShaderCodeOperator&lt;T&gt;, cached per type; null for other ops. */
    private static PropertyInfo? GetShaderCodeProperty(Type type)
    {
        if (_shaderCodeProperties.TryGetValue(type, out var property))
            return property;

        foreach (var implemented in type.GetInterfaces())
        {
            if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(IShaderCodeOperator<>))
            {
                property = implemented.GetProperty("Code");
                break;
            }
        }

        _shaderCodeProperties[type] = property;
        return property;
    }

    private static bool TryGetText(Instance instance, Guid inputId, out string text)
    {
        foreach (var input in instance.Inputs)
        {
            if (input.Id != inputId || input is not InputSlot<string> stringInput)
                continue;

            text = stringInput.Value ?? stringInput.TypedInputValue.Value ?? string.Empty;
            return text.Length > 0;
        }

        text = string.Empty;
        return false;
    }

    private static int CountLines(string text, int end)
    {
        var count = 0;
        for (var index = 0; index < end; index++)
        {
            if (text[index] == '\n')
                count++;
        }

        return count;
    }

    [GeneratedRegex(@"Line (\d+):")]
    private static partial Regex LineRegex();

    private const int MaxDepth = 2;

    private static readonly Dictionary<Type, PropertyInfo?> _shaderCodeProperties = new();
    private static Instance? _instance;
    private static int _frame;
    private static string? _rawMessage;
    private static string? _source;
    private static string? _message;
    private static int _line;
    private static Guid _inputId;
}
