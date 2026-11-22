#nullable enable
using System.Reflection;
using System.Text.RegularExpressions;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Resource.ShaderCompiling;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.InputUi.SimpleInputUis;

/// <summary>
/// The problem to show below a <see cref="StringInputUi.UsageType.Code"/> input. Ops that report it themselves
/// implement <see cref="IInputStatusProvider"/>. For template-based ops like the Custom* shaders, the error comes from
/// a child shader op compiling the spliced source: it is shown below the code input whose text contains the error
/// line (with the line counted within that input), otherwise below the op's first code input. Errors in included files
/// and inputs whose text occurs more than once in the source are never mapped, because their line would be a guess.
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

    /// <summary>
    /// For the code editor: the source a child shader op compiled from this input's text, the line the text starts at in
    /// it (0 when it can't be found exactly once) and every problem the compiler reported, empty when it compiled.
    /// </summary>
    public static bool TryGetCompiledSource(Instance instance, Guid inputId, out string compiledSource, out int firstLine,
                                            out IReadOnlyList<ShaderCompileFailures.Diagnostic> diagnostics)
    {
        compiledSource = string.Empty;
        firstLine = 0;
        diagnostics = [];
        if (!TryGetText(instance, inputId, out var code))
            return false;

        // The searches only run when the text or the compiled source changed, since the popup asks every frame.
        var isCached = ReferenceEquals(code, _contextCode) && _contextSource != null && IsStillCompiledFrom(instance, _contextSource);
        if (isCached)
        {
            compiledSource = _contextSource!;
            firstLine = _contextFirstLine;
            diagnostics = _contextDiagnostics;
            return true;
        }

        if (!TryFindShaderError(instance, 0, out _, out var source) || source == null || FindUniqueStart(source, code) < 0)
        {
            source = FindSourceContaining(instance, code, 0);
            if (source == null)
                return false;
        }

        if (!ReferenceEquals(source, _contextSource) || !ReferenceEquals(code, _contextCode))
        {
            _contextSource = source;
            _contextCode = code;
            var start = FindUniqueStart(source, code);
            _contextFirstLine = start < 0 ? 0 : CountLines(source, start) + 1;
            _contextDiagnostics = ShaderCompileFailures.TryFindBySource(source, out var failure) ? failure.Diagnostics : [];
        }

        compiledSource = source;
        firstLine = _contextFirstLine;
        diagnostics = _contextDiagnostics;
        return true;
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
        var errorLine = lineMatch.Success && IsInCompiledSource(rawMessage, lineMatch, source) ? int.Parse(lineMatch.Groups[1].Value) : 0;
        var symbolUi = instance.GetSymbolUi();
        foreach (var definition in instance.Symbol.InputDefinitions)
        {
            if (!symbolUi.InputUis.TryGetValue(definition.Id, out var inputUi) || inputUi is not StringInputUi { Usage: StringInputUi.UsageType.Code })
                continue;

            if (_inputId == Guid.Empty)
                _inputId = definition.Id;

            if (errorLine == 0 || source == null || !TryGetText(instance, definition.Id, out var code))
                continue;

            var start = FindUniqueStart(source, code);
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

    /// <summary>
    /// Whether the reported line is in the compiled source and not in one of its includes. The kept diagnostics say so
    /// directly; without them, the compilers name the source "Failed to compile NAME: Line N: text", a blank line and NAME.
    /// </summary>
    private static bool IsInCompiledSource(string rawMessage, Match lineMatch, string? source)
    {
        if (source != null && ShaderCompileFailures.TryFindBySource(source, out var failure))
        {
            foreach (var diagnostic in failure.Diagnostics)
            {
                if (diagnostic.IsError)
                    return diagnostic.File.Length == 0;
            }
        }

        const string prefix = "Failed to compile ";
        var nameEnd = lineMatch.Index - 2;
        if (!rawMessage.StartsWith(prefix, StringComparison.Ordinal) || nameEnd <= prefix.Length)
            return true;

        var name = rawMessage[prefix.Length..nameEnd];
        var locationStart = rawMessage.IndexOf("\n\n", lineMatch.Index, StringComparison.Ordinal);
        if (locationStart < 0)
            return true;

        locationStart += 2;
        var locationEnd = rawMessage.IndexOf('\n', locationStart);
        var location = locationEnd < 0 ? rawMessage[locationStart..] : rawMessage[locationStart..locationEnd];
        return string.Equals(location, name, StringComparison.Ordinal);
    }

    /** -1 unless the text occurs exactly once, e.g. a short define could also match inside the template. */
    private static int FindUniqueStart(string source, string code)
    {
        var start = source.IndexOf(code, StringComparison.Ordinal);
        if (start < 0 || source.IndexOf(code, start + 1, StringComparison.Ordinal) >= 0)
            return -1;

        return start;
    }

    /** Whether a child shader op (up to two levels down) still holds exactly this source. */
    private static bool IsStillCompiledFrom(Instance instance, string source, int depth = 0)
    {
        foreach (var child in instance.Children.Values)
        {
            if (GetShaderCodeProperty(child.GetType())?.GetValue(child) is InputSlot<string> { Value: { } childSource }
                && ReferenceEquals(childSource, source))
                return true;

            if (depth < MaxDepth && IsStillCompiledFrom(child, source, depth + 1))
                return true;
        }

        return false;
    }

    /** The source of a child shader op (up to two levels down) that contains the text, compiled or not. */
    private static string? FindSourceContaining(Instance instance, string code, int depth)
    {
        foreach (var child in instance.Children.Values)
        {
            if (GetShaderCodeProperty(child.GetType())?.GetValue(child) is InputSlot<string> { Value: { } childSource }
                && childSource.Contains(code, StringComparison.Ordinal))
                return childSource;

            if (depth < MaxDepth && FindSourceContaining(child, code, depth + 1) is { } found)
                return found;
        }

        return null;
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

    private static string? _contextSource;
    private static string? _contextCode;
    private static int _contextFirstLine;
    private static IReadOnlyList<ShaderCompileFailures.Diagnostic> _contextDiagnostics = [];
}
