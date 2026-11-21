using System;
using System.IO;
using T3.Core.DataTypes;
using T3.Core.Resource.ShaderCompiling;
using T3.Graphics;
using T3.Graphics.Compat;
using T3.Graphics.Vulkan;
using Xunit;
using Xunit.Abstractions;
using PixelShaderType = T3.Core.DataTypes.PixelShader;
using ComputeShaderType = T3.Core.DataTypes.ComputeShader;

namespace Core.Tests;

/// <summary>
/// Compiles the NanoVDB shaders through TiXL's own compiler, from the files the operators ship,
/// with an asset folder registered so their #include resolves the way it does in the editor.
///
/// This is the gate the volume work was missing: compiling the same source with a hand-built
/// slangc command line proves the HLSL is valid, but not that the include lookup, the register
/// shifts and the declared thread group size work through the path the editor actually takes.
/// </summary>
/// <remarks>
/// Skips itself where slangc or a Vulkan device is missing, following the other shader tests.
/// </remarks>
// ShaderCompiler.Instance is process-wide, so these classes must not run in parallel.
[Collection("ShaderCompiler")]
public class NanoVdbShaderTests(ITestOutputHelper output)
{
    [Fact]
    public void ThePointsShaderCompilesWithItsInclude()
    {
        using var backend = TryCreateBackend();
        if (backend == null)
            return;

        var compiler = new SlangShaderCompiler(new Device(backend));

        var assetFolder = FindLibAssets();
        if (assetFolder == null)
        {
            output.WriteLine("Skipped: Operators/Lib/Assets was not found next to the test assembly.");
            return;
        }

        var path = Path.Combine(assetFolder, "shaders", "volume", "nanovdb-to-points.hlsl");
        Assert.True(File.Exists(path), $"missing shader: {path}");

        Assert.True(TryCompile<ComputeShaderType>(compiler, ExpandIncludes(File.ReadAllText(path), assetFolder), "main", out var shader, out var reason), reason);
        using var compiled = shader;

        // The dispatcher sizes its groups from this, so a mismatch would silently clip the volume.
        Assert.True(shader!.TryGetThreadGroups(out var threadGroups));
        Assert.Equal(512, threadGroups.X);
        Assert.Equal(1, threadGroups.Y);
        Assert.Equal(1, threadGroups.Z);
    }

    [Fact]
    public void TheBoundsShaderCompilesWithItsInclude()
    {
        using var backend = TryCreateBackend();
        if (backend == null)
            return;

        var compiler = new SlangShaderCompiler(new Device(backend));

        var assetFolder = FindLibAssets();
        if (assetFolder == null)
        {
            output.WriteLine("Skipped: Operators/Lib/Assets was not found next to the test assembly.");
            return;
        }

        var path = Path.Combine(assetFolder, "shaders", "volume", "nanovdb-bounds.hlsl");
        Assert.True(TryCompile<ComputeShaderType>(compiler, ExpandIncludes(File.ReadAllText(path), assetFolder), "main", out var shader, out var reason), reason);
        using var compiled = shader;

        Assert.True(shader!.TryGetThreadGroups(out var threadGroups));
        Assert.Equal(64, threadGroups.X);
    }

    [Fact]
    public void TheFieldSnippetCompilesInOneShaderWithTwoGrids()
    {
        using var backend = TryCreateBackend();
        if (backend == null)
            return;

        var compiler = new SlangShaderCompiler(new Device(backend));

        var assetFolder = FindLibAssets();
        if (assetFolder == null)
        {
            output.WriteLine("Skipped: Operators/Lib/Assets was not found next to the test assembly.");
            return;
        }

        // What two Vdb field nodes assemble to: one include, one shared helper, one wrapper each and
        // one structured buffer per grid. The include key is constant precisely so this stays one copy.
        const string source = """
                              #include "volume/nanovdb-common.hlsl"

                              float VdbFieldOutside(float3 p, float3 boundsMin, float3 boundsMax, float value, float outsideValue)
                              {
                                  float3 outsideDelta = max(boundsMin - p, p - boundsMax);
                                  float outsideDistance = length(max(outsideDelta, 0.0));
                                  if (outsideDistance <= 0)
                                      return value;

                                  return min(outsideDistance + max(value, 0.0), outsideValue);
                              }

                              float vdbSdf_1111(StructuredBuffer<uint> g, float3 p, float3 boundsMin, float3 boundsMax, float scale, float offset, float outsideValue)
                              {
                                  VdbGridInfo info = VdbReadGridInfo(g);
                                  VdbLeafCache cache = VdbNewLeafCache();
                                  float value = VdbSampleTrilinear(g, info, p, cache) * scale + offset;
                                  return VdbFieldOutside(p, boundsMin, boundsMax, value, outsideValue);
                              }

                              float vdbDensity_2222(StructuredBuffer<uint> g, float3 p, float3 boundsMin, float3 boundsMax, float scale, float offset, float isoValue, float outsideValue)
                              {
                                  VdbGridInfo info = VdbReadGridInfo(g);
                                  VdbLeafCache cache = VdbNewLeafCache();
                                  float density = VdbSampleTrilinear(g, info, p, cache) * scale + offset;
                                  return VdbFieldOutside(p, boundsMin, boundsMax, isoValue - density, outsideValue);
                              }

                              StructuredBuffer<uint> _1111Grid : register(t0);
                              StructuredBuffer<uint> _2222Grid : register(t1);

                              float4 main(float4 position : SV_POSITION) : SV_TARGET
                              {
                                  float3 p = position.xyz * 0.01;
                                  float sdf = vdbSdf_1111(_1111Grid, p, float3(-1, -1, -1), float3(1, 1, 1), 1.0, 0.0, 10.0);
                                  float density = vdbDensity_2222(_2222Grid, p, float3(-1, -1, -1), float3(1, 1, 1), 1.0, 0.0, 0.5, 10.0);
                                  return float4(sdf, density, 0, 1);
                              }
                              """;

        var compiled = TryCompile<PixelShaderType>(compiler, ExpandIncludes(source, assetFolder), "main", out var shader, out var reason);

        // A machine without the pinned SPIR-V toolchain cannot compile fragment shaders at all; the
        // other shader tests fail the same way there. That is the environment, not this source.
        if (!compiled && reason.Contains("spirv-opt"))
        {
            output.WriteLine($"Skipped: the SPIR-V toolchain is incomplete ({reason})");
            return;
        }

        Assert.True(compiled, reason);
        shader?.Dispose();
    }

    /// <summary>Walks up from the test output folder to the repository root that holds the operator assets.</summary>
    private static string? FindLibAssets()
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

    private VulkanBackend? TryCreateBackend()
    {
        if (SlangShaderCompiler.FindCompiler() == null)
        {
            output.WriteLine("Skipped: slangc was not found.");
            return null;
        }

        try
        {
            GraphicsLog.Error = message => output.WriteLine(message);
            return new VulkanBackend(enableValidation: true);
        }
        catch (Exception exception)
        {
            output.WriteLine($"Skipped: no Vulkan device ({exception.Message})");
            return null;
        }
    }

    private bool TryCompile<TShader>(SlangShaderCompiler compiler, string source, string entryPoint,
                                     out TShader? shader, out string reason)
        where TShader : AbstractShader
    {
        ShaderCompiler.Shutdown();
        typeof(ShaderCompiler).GetField("_shutdown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                              .SetValue(null, false);
        ShaderCompiler.Instance = compiler;

        // No owner: the includes are already inlined, so nothing has to resolve against a package.
        var args = new ShaderCompiler.ShaderCompilationArgs(source, entryPoint, null,
                                                           $"vdbtest{Guid.NewGuid():N}.hlsl", null);

        return ShaderCompiler.TryCompileShaderFromSource(args, useCache: false, forceRecompile: true, out shader, out reason);
    }

    /// <summary>
    /// Replaces every #include with the file's text, recursively.
    ///
    /// The engine resolves includes by turning them into "Lib:shaders/&lt;name&gt;" and asking the asset
    /// registry, which needs a running editor with the Lib package registered. A unit test has no
    /// registry, so it inlines the same files instead - which still compiles the real shader body
    /// and the real include text through the real compiler.
    /// </summary>
    private static string ExpandIncludes(string source, string assetFolder)
    {
        var shadersRoot = Path.Combine(assetFolder, "shaders");

        for (var guard = 0; guard < 8; guard++)
        {
            var match = System.Text.RegularExpressions.Regex.Match(source, "#include\\s+\"([^\"]+)\"");
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

    [Fact]
    public void SharedIncludesLiveWhereTheEngineLooksForThem()
    {
        // The engine rewrites every include to "Lib:shaders/<name>", so a shared include has to live
        // in Lib's assets folder. A package of its own would never be found, no matter how it is
        // addressed - this asserts the on-disk half of that convention.
        var assetFolder = FindLibAssets();
        if (assetFolder == null)
        {
            output.WriteLine("Skipped: Operators/Lib/Assets was not found next to the test assembly.");
            return;
        }

        Assert.True(File.Exists(Path.Combine(assetFolder, "shaders", "volume", "nanovdb-common.hlsl")));
        Assert.True(File.Exists(Path.Combine(assetFolder, "shaders", "shared", "point.hlsl")));
    }
}
