#nullable enable
using System.Text;
using System.Text.RegularExpressions;

namespace T3.Editor.Migrations.Steps;

internal sealed partial class To4_SharpDxToGraphicsFacade
{
    /// <summary>
    /// Rewrites one C# source from SharpDX's D3D11 types onto the graphics facade: using lines,
    /// fully qualified names and the interop value types. Pure text work without a compiler, so
    /// it mirrors what a project's global using aliases did for the library packages. What it
    /// cannot translate (image loading, shader compilation, Direct2D) it leaves alone and reports.
    /// </summary>
    internal static class SourceRewriter
    {
        public static string Rewrite(string source, List<string> notes)
        {
            var text = source;
            foreach (var replacement in _qualifiedNames)
            {
                text = replacement.Regex.Replace(text, replacement.Replacement);
            }

            if (TryReplaceUsingLines(text, out var withFacadeUsings))
            {
                text = DropDuplicateUsings(withFacadeUsings);
            }

            foreach (var replacement in _bareNames)
            {
                text = replacement.Regex.Replace(text, replacement.Replacement);
            }

            foreach (var untouched in _untouchedNamespaces)
            {
                if (text.Contains(untouched, StringComparison.Ordinal))
                    notes.Add("still uses " + untouched);
            }

            if (_sharpDxWordRegex.IsMatch(text))
                notes.Add("still mentions SharpDX");

            return text;
        }

        /// <summary>
        /// Maps a fully qualified SharpDX type name onto its facade counterpart, or null when the
        /// facade has no replacement (a type from one of the namespaces that stay on SharpDX).
        /// Serves the csproj's global using aliases, which name types without any source context.
        /// </summary>
        public static string? MapQualifiedName(string typeName)
        {
            foreach (var replacement in _qualifiedNames)
            {
                if (!replacement.Regex.IsMatch(typeName))
                    continue;

                return replacement.Regex.Replace(typeName, replacement.Replacement);
            }

            return null;
        }

        private static bool TryReplaceUsingLines(string text, out string result)
        {
            var lines = text.Split('\n');
            var builder = new StringBuilder(text.Length);
            var replacedAny = false;

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                var endsWithCarriageReturn = line.EndsWith('\r');
                var trimmed = line.Trim();

                if (_usingLines.TryGetValue(trimmed, out var replacements))
                {
                    replacedAny = true;
                    var indentation = line[..(line.Length - line.TrimStart().Length)];
                    for (var replacementIndex = 0; replacementIndex < replacements.Length; replacementIndex++)
                    {
                        builder.Append(indentation).Append(replacements[replacementIndex]);
                        if (endsWithCarriageReturn)
                            builder.Append('\r');

                        if (replacementIndex < replacements.Length - 1)
                            builder.Append('\n');
                    }
                }
                else
                {
                    builder.Append(line);
                }

                if (lineIndex < lines.Length - 1)
                    builder.Append('\n');
            }

            result = replacedAny ? builder.ToString() : text;
            return replacedAny;
        }

        /** Rewritten using lines easily collide with ones the file already had */
        private static string DropDuplicateUsings(string text)
        {
            var lines = text.Split('\n');
            var seen = new HashSet<string>();
            var result = new StringBuilder(text.Length);
            var isFirst = true;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var isPlainUsing = trimmed.StartsWith("using ", StringComparison.Ordinal)
                                   && trimmed.EndsWith(';')
                                   && !trimmed.Contains('=');

                if (isPlainUsing && !seen.Add(trimmed))
                    continue;

                if (!isFirst)
                    result.Append('\n');

                result.Append(line);
                isFirst = false;
            }

            return result.ToString();
        }

        private readonly record struct NameReplacement(Regex Regex, string Replacement);

        private static NameReplacement Qualified(string sharpDxName, string facadeName)
        {
            // A trailing dot is a namespace prefix and matches any member; a full name must not
            // swallow a longer identifier (Texture2D vs. Texture2DDescription).
            var pattern = sharpDxName.EndsWith('.')
                              ? @"\b" + Regex.Escape(sharpDxName)
                              : @"\b" + Regex.Escape(sharpDxName) + @"\b";
            return new NameReplacement(new Regex(pattern, RegexOptions.Compiled), facadeName);
        }

        private static NameReplacement Bare(string sharpDxName, string facadeName)
            => new(new Regex(@"\b" + Regex.Escape(sharpDxName) + @"\b", RegexOptions.Compiled), facadeName);

        /** Most specific first, so a prefix never eats a longer match */
        private static readonly NameReplacement[] _qualifiedNames =
            [
                Qualified("SharpDX.Mathematics.Interop.RawColor4", "System.Numerics.Vector4"),
                Qualified("SharpDX.Mathematics.Interop.RawVector4", "System.Numerics.Vector4"),
                Qualified("SharpDX.Mathematics.Interop.RawVector3", "System.Numerics.Vector3"),
                Qualified("SharpDX.Mathematics.Interop.RawVector2", "System.Numerics.Vector2"),
                Qualified("SharpDX.Mathematics.Interop.RawMatrix", "System.Numerics.Matrix4x4"),
                Qualified("SharpDX.Mathematics.Interop.RawViewportF", "T3.Graphics.Viewport"),
                Qualified("SharpDX.Mathematics.Interop.RawRectangle", "T3.Graphics.ScissorRect"),
                // Operators exchange textures as the Core wrappers, which is also what the slot
                // type registry knows; the compat texture is only reachable through them.
                Qualified("SharpDX.Direct3D11.Texture2D", "T3.Core.DataTypes.Texture2D"),
                Qualified("SharpDX.Direct3D11.Texture3D", "T3.Core.DataTypes.Texture3D"),
                Qualified("SharpDX.Direct3D11.", "T3.Graphics.Compat."),
                Qualified("SharpDX.Direct3D.", "T3.Graphics.Compat."),
                Qualified("SharpDX.DXGI.Format", "T3.Graphics.Format"),
                Qualified("SharpDX.DXGI.SampleDescription", "T3.Graphics.SampleDescription"),
                Qualified("SharpDX.DXGI.", "T3.Graphics.Compat."),
                Qualified("SharpDX.DataStream", "T3.Graphics.Compat.DataStream"),
                Qualified("SharpDX.DataBox", "T3.Graphics.Compat.DataBox"),
                Qualified("SharpDX.DataRectangle", "T3.Graphics.Compat.DataRectangle"),
                Qualified("SharpDX.Utilities", "T3.Graphics.Compat.Utilities"),
            ];

        /** Bare type names, valid once the using lines and the project aliases point at the facade */
        private static readonly NameReplacement[] _bareNames =
            [
                Bare("RawColor4", "Vector4"),
                Bare("RawVector4", "Vector4"),
                Bare("RawVector3", "Vector3"),
                Bare("RawVector2", "Vector2"),
                Bare("RawMatrix", "Matrix4x4"),
                Bare("RawViewportF", "Viewport"),
                Bare("ViewportF", "Viewport"),
                Bare("RawRectangle", "ScissorRect"),
                Bare("RawBool", "bool"),
            ];

        private static readonly Dictionary<string, string[]> _usingLines =
            new()
                {
                    ["using SharpDX.Direct3D11;"] = ["using T3.Graphics.Compat;"],
                    ["using SharpDX.Direct3D;"] = ["using T3.Graphics.Compat;"],
                    ["using SharpDX.DXGI;"] = ["using T3.Graphics;", "using T3.Graphics.Compat;"],
                    ["using SharpDX.Mathematics.Interop;"] = ["using System.Numerics;"],
                    ["using SharpDX;"] = ["using T3.Graphics.Compat;"],
                };

        /** Image loading, shader compilation, text and the Windows helpers are not what the facade replaces */
        private static readonly string[] _untouchedNamespaces =
            [
                "SharpDX.WIC", "SharpDX.D3DCompiler", "SharpDX.Direct2D1", "SharpDX.DirectWrite",
                "SharpDX.Windows", "SharpDX.IO", "SharpDX.XInput", "SharpDX.MediaFoundation",
                "SharpDX.Multimedia",
            ];

        private static readonly Regex _sharpDxWordRegex = new(@"\bSharpDX\b", RegexOptions.Compiled);
    }
}
