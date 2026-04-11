#nullable enable

namespace Lib.Utils.Slug;

/// <summary>
/// Canonical Slug shader locations used by runtime integrations.
/// The Slug pipeline requires both shaders:
/// - SlugVertexShader.hlsl
/// - SlugPixelShader.hlsl
/// </summary>
public static class SlugShaderPaths
{
    public const string RelativeShaderDirectory = "Operators/Lib/Assets/shaders/slug";
    public const string PixelShaderFileName = "SlugPixelShader.hlsl";
    public const string VertexShaderFileName = "SlugVertexShader.hlsl";

    public static string RelativePixelShaderPath => $"{RelativeShaderDirectory}/{PixelShaderFileName}";
    public static string RelativeVertexShaderPath => $"{RelativeShaderDirectory}/{VertexShaderFileName}";

    public static bool TryResolveAbsoluteShaderPaths([NotNullWhen(true)] out string? pixelShaderPath,
                                                     [NotNullWhen(true)] out string? vertexShaderPath)
    {
        pixelShaderPath = null;
        vertexShaderPath = null;

        foreach (var root in EnumerateLikelyRoots())
        {
            var slugDir = Path.Combine(root, "Operators", "Lib", "Assets", "shaders", "slug");
            var pixel = Path.Combine(slugDir, PixelShaderFileName);
            var vertex = Path.Combine(slugDir, VertexShaderFileName);

            if (!File.Exists(pixel) || !File.Exists(vertex))
                continue;

            pixelShaderPath = pixel;
            vertexShaderPath = vertex;
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateLikelyRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TryAddAncestors(Directory.GetCurrentDirectory(), roots);
        TryAddAncestors(AppContext.BaseDirectory, roots);

        return roots;
    }

    private static void TryAddAncestors(string? startPath, ISet<string> roots)
    {
        if (string.IsNullOrWhiteSpace(startPath))
            return;

        var dir = new DirectoryInfo(startPath);
        if (!dir.Exists)
            return;

        for (var i = 0; i < 10 && dir != null; i++)
        {
            roots.Add(dir.FullName);
            dir = dir.Parent;
        }
    }
}
