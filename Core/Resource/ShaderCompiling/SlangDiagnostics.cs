#nullable enable
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace T3.Core.Resource.ShaderCompiling;

/// <summary>
/// Turns slangc's multi-line diagnostics into the short message shown for a failed shader: where the first
/// error is, what it says, and how to fix it if the cause is a known difference from earlier TiXL versions.
/// </summary>
/// <remarks>
/// Only the first error is reported, because the following ones are often consequences of it.
/// </remarks>
internal static partial class SlangDiagnostics
{
    /// <param name="diagnostics">The compiler's complete output.</param>
    /// <param name="compiledSourcePath">The temp file the source was written to, which means nothing to the user.</param>
    /// <param name="shaderName">What to call that file in the message instead.</param>
    public static string DescribeFirstError(string diagnostics, string compiledSourcePath, string shaderName)
    {
        var lines = diagnostics.Split('\n');

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var header = ErrorHeaderRegex().Match(lines[lineIndex].TrimEnd());
            if (!header.Success)
                continue;

            var text = header.Groups["text"].Value;
            var location = string.Empty;
            var lineNumber = string.Empty;

            for (var detailIndex = lineIndex + 1; detailIndex < lines.Length; detailIndex++)
            {
                var detailLine = lines[detailIndex].TrimEnd();
                if (detailLine.Length == 0 || ErrorHeaderRegex().IsMatch(detailLine))
                    break;

                var locationMatch = LocationRegex().Match(detailLine);
                if (locationMatch.Success && lineNumber.Length == 0)
                {
                    var path = locationMatch.Groups["path"].Value;
                    location = SamePath(path, compiledSourcePath) ? shaderName : Path.GetFileName(path);
                    lineNumber = locationMatch.Groups["line"].Value;
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

            var message = lineNumber.Length > 0 ? $"Line {lineNumber}: {text}\n\n{location}" : text;
            var hint = FindHint(text);
            return hint == null ? message : $"{message}\n\n{hint}";
        }

        return diagnostics;
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

    [GeneratedRegex(@"^error(\[\w+\])?:\s*(?<text>.*)$")]
    private static partial Regex ErrorHeaderRegex();

    /** Greedy path, because a Windows path contains a colon itself. */
    [GeneratedRegex(@"^\s*-+>\s*(?<path>.+):(?<line>\d+):\d+$")]
    private static partial Regex LocationRegex();

    [GeneratedRegex(@"^\s*\|\s*\^+\s+(?<text>.+)$")]
    private static partial Regex CaretRegex();

    /** Matches both "expected ... 'vector&lt;float,3&gt;', got 'vector&lt;float,4&gt;'" and an overload's "(vector&lt;float,4&gt;, vector&lt;float,3&gt;)". */
    [GeneratedRegex(@"vector<\w+,(?<a>\d)>.*vector<\w+,(?<b>\d)>")]
    private static partial Regex VectorSizesRegex();
}
