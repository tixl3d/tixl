#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace T3.Core.Resource.ShaderCompiling;

/// <summary>
/// Turns slangc's multi-line diagnostics into the short message shown for a failed shader: where the first
/// error is, what it says, and how to fix it if the cause is a known difference from earlier TiXL versions.
/// <see cref="Parse"/> keeps all of them for the code editor's problem list.
/// </summary>
/// <remarks>
/// The message names only the first error, because the following ones are often consequences of it.
/// </remarks>
internal static partial class SlangDiagnostics
{
    /// <param name="diagnostics">The compiler's complete output.</param>
    /// <param name="compiledSourcePath">The temp file the source was written to, which means nothing to the user.</param>
    /// <param name="shaderName">What to call that file in the message instead.</param>
    public static string DescribeFirstError(string diagnostics, string compiledSourcePath, string shaderName)
    {
        var parsed = new List<ShaderCompileFailures.Diagnostic>();
        Parse(diagnostics, compiledSourcePath, parsed);
        foreach (var diagnostic in parsed)
        {
            if (!diagnostic.IsError)
                continue;

            var location = diagnostic.File.Length == 0 ? shaderName : diagnostic.File;
            var message = diagnostic.Line > 0 ? $"Line {diagnostic.Line}: {diagnostic.Text}\n\n{location}" : diagnostic.Text;
            var hint = FindHint(diagnostic.Text);
            return hint == null ? message : $"{message}\n\n{hint}";
        }

        return diagnostics;
    }

    /// <summary>Adds every error and warning in slangc's output, in the order reported.</summary>
    public static void Parse(string diagnostics, string compiledSourcePath, List<ShaderCompileFailures.Diagnostic> result)
    {
        var lines = diagnostics.Split('\n');

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var header = HeaderRegex().Match(lines[lineIndex].TrimEnd());
            if (!header.Success)
                continue;

            var isError = header.Groups["severity"].Value == "error";
            var text = header.Groups["text"].Value;
            var file = string.Empty;
            var lineNumber = 0;
            var column = 0;

            for (var detailIndex = lineIndex + 1; detailIndex < lines.Length; detailIndex++)
            {
                var detailLine = lines[detailIndex].TrimEnd();
                if (detailLine.Length == 0 || HeaderRegex().IsMatch(detailLine))
                    break;

                var locationMatch = LocationRegex().Match(detailLine);
                if (locationMatch.Success && lineNumber == 0)
                {
                    var path = locationMatch.Groups["path"].Value;
                    file = SamePath(path, compiledSourcePath) ? string.Empty : Path.GetFileName(path);
                    lineNumber = int.Parse(locationMatch.Groups["line"].Value);
                    column = int.Parse(locationMatch.Groups["column"].Value);
                    continue;
                }

                // The text beside the carets is more specific than the header: it names the identifier or types.
                var caretMatch = CaretRegex().Match(detailLine);
                if (caretMatch.Success)
                {
                    text = caretMatch.Groups["text"].Value;
                    break;
                }
            }

            result.Add(new ShaderCompileFailures.Diagnostic(isError, file, lineNumber, column, text));
        }
    }

    /** Null when the error says nothing specific to moving a shader from an earlier TiXL version. */
    private static string? FindHint(string text)
    {
        if (text.Contains("LegacyPoint"))
            return "LegacyPoint was removed in TiXL 4.3. Use Point: W is now FX1, Stretch is Scale and Selected is FX2.";

        if (text.Contains("rotate_vector"))
            return "rotate_vector was renamed to qRotateVec3, with the same arguments.";

        if (text.Contains("sampler2D") || text.Contains("tex2D"))
            return "This is a D3D9 name. Use Texture2D with a SamplerState and call Sample().";

        if (text.Contains("SLANG_ParameterGroup"))
            return "Two cbuffers in this shader share a name. Rename one; the name is not used anywhere else.";

        if (text.Contains("not an l-value"))
            return "Constant buffers and read-only textures can no longer be written to. Copy the value into a local variable first.";

        var mismatch = VectorSizesRegex().Match(text);
        if (mismatch.Success && mismatch.Groups["a"].Value != mismatch.Groups["b"].Value)
            return "Vector sizes must match. Earlier TiXL versions silently dropped components, now you have to pick them, for example value.xyz.";

        return null;
    }

    private static bool SamePath(string a, string b)
    {
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^(?<severity>error|warning)(\[\w+\])?:\s*(?<text>.*)$")]
    private static partial Regex HeaderRegex();

    /** Greedy path, because a Windows path contains a colon itself. */
    [GeneratedRegex(@"^\s*-+>\s*(?<path>.+):(?<line>\d+):(?<column>\d+)$")]
    private static partial Regex LocationRegex();

    [GeneratedRegex(@"^\s*\|\s*\^+\s+(?<text>.+)$")]
    private static partial Regex CaretRegex();

    /** Matches both "expected ... 'vector&lt;float,3&gt;', got 'vector&lt;float,4&gt;'" and an overload's "(vector&lt;float,4&gt;, vector&lt;float,3&gt;)". */
    [GeneratedRegex(@"vector<\w+,(?<a>\d)>.*vector<\w+,(?<b>\d)>")]
    private static partial Regex VectorSizesRegex();
}
