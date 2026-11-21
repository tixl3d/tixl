using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Core.Tests;

/// <summary>
/// Locates the operator assets and reads shader source the way the engine would.
///
/// The engine resolves <c>#include</c> by rewriting it to <c>Lib:shaders/&lt;name&gt;</c> and asking the
/// asset registry, which needs a running editor. A test inlines the same files instead, so it still
/// compiles the real shader body and the real include text through the real compiler.
/// </summary>
internal static class NanoVdbShaderSources
{
    /// <summary>Walks up from the test output folder to the repository root that holds the operator assets.</summary>
    public static string? FindLibAssets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Operators", "Lib", "Assets");
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>Replaces every #include with that file's text, recursively.</summary>
    public static string ExpandIncludes(string source, string assetFolder)
    {
        var shadersRoot = Path.Combine(assetFolder, "shaders");

        for (var guard = 0; guard < 8; guard++)
        {
            var match = Regex.Match(source, "#include\\s+\"([^\"]+)\"");
            if (!match.Success)
                return source;

            var includeName = match.Groups[1].Value;
            var includePath = Path.Combine(shadersRoot, includeName.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(includePath))
                throw new FileNotFoundException($"include {includeName} not found under {shadersRoot}", includePath);

            var included = ExpandIncludes(File.ReadAllText(includePath), assetFolder);
            source = string.Concat(source.AsSpan(0, match.Index), included, source.AsSpan(match.Index + match.Length));
        }

        return source;
    }

    /// <summary>Reads a shipping shader with its includes expanded, or null when the assets are not next to the tests.</summary>
    public static string? ReadShader(string relativePath)
    {
        var assets = FindLibAssets();
        if (assets == null)
            return null;

        var path = Path.Combine(assets, "shaders", relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? ExpandIncludes(File.ReadAllText(path), assets) : null;
    }
}
