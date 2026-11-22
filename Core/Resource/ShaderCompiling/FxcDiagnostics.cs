#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace T3.Core.Resource.ShaderCompiling;

/// <summary>
/// Turns the D3D compiler's output into the short message shown for a failed shader, and keeps every error and
/// warning for the code editor's problem list.
/// </summary>
/// <remarks>
/// The compiler reports lines like <c>Blur.hlsl(32,12-56): error X3004: undeclared identifier 'q'</c>. The location can
/// also be a range (<c>(32,12-33,4)</c>), so only the first line and column are kept.
/// </remarks>
internal static partial class FxcDiagnostics
{
    /// <param name="output">The compiler's complete output.</param>
    /// <param name="compiledSourceName">The name the source was compiled under; locations in it are reported with an empty file.</param>
    /// <param name="result">Receives every error and warning, in the order reported.</param>
    /// <returns>"Line N: text", a blank line and the file, for the first error, otherwise the first warning.</returns>
    public static string Describe(string output, string compiledSourceName, List<ShaderCompileFailures.Diagnostic> result)
    {
        var firstNew = result.Count;
        var lines = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var located = LocatedRegex().Match(line);
            if (located.Success)
            {
                var file = located.Groups["file"].Value.Trim();
                result.Add(new ShaderCompileFailures.Diagnostic(IsError(located),
                                                                 string.Equals(file, compiledSourceName, StringComparison.OrdinalIgnoreCase) ? string.Empty : file,
                                                                 int.Parse(located.Groups["line"].Value),
                                                                 located.Groups["column"].Success ? int.Parse(located.Groups["column"].Value) : 0,
                                                                 located.Groups["text"].Value.Trim()));
                continue;
            }

            var unlocated = UnlocatedRegex().Match(line);
            if (unlocated.Success)
                result.Add(new ShaderCompileFailures.Diagnostic(IsError(unlocated), string.Empty, 0, 0,
                                                                 unlocated.Groups["text"].Value.Trim()));
        }

        var shown = FindFirst(result, firstNew, isError: true) ?? FindFirst(result, firstNew, isError: false);
        if (shown is not { } diagnostic)
            return output;

        if (diagnostic.Line == 0)
            return diagnostic.Text;

        var location = diagnostic.File.Length == 0 ? compiledSourceName : diagnostic.File;
        return $"Line {diagnostic.Line}: {diagnostic.Text}\n\n{location}";
    }

    private static bool IsError(Match match) => string.Equals(match.Groups["severity"].Value, "error", StringComparison.OrdinalIgnoreCase);

    private static ShaderCompileFailures.Diagnostic? FindFirst(List<ShaderCompileFailures.Diagnostic> diagnostics, int start, bool isError)
    {
        for (var index = start; index < diagnostics.Count; index++)
        {
            if (diagnostics[index].IsError == isError)
                return diagnostics[index];
        }

        return null;
    }

    [GeneratedRegex(@"^(?<file>.*?)\((?<line>\d+)(,(?<column>\d+))?[^)]*\)\s*:\s*(?<severity>error|warning)\s*(\w+)?\s*:\s*(?<text>.*)$",
                    RegexOptions.IgnoreCase)]
    private static partial Regex LocatedRegex();

    [GeneratedRegex(@"^(?<severity>error|warning)\s*(\w+)?\s*:\s*(?<text>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex UnlocatedRegex();
}
