using System.Diagnostics;
using System.Text.Json;
using T3.Core.Logging;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// Compiles TiXL's unmodified HLSL to SPIR-V by running <c>slangc</c>, and reads back the bindings Slang assigned.
/// </summary>
/// <remarks>
/// D3D register classes map to fixed binding ranges in descriptor set 0, so every shader shares one layout
/// scheme: s0-15 → 0-15, b0-15 → 16-31, t0-127 → 32-159, u0-7 → 160-167.
/// </remarks>
internal static class SlangShaderCompiler
{
    public static CompiledShader Compile(string shaderRoot, string relativePath, string entryPoint, ShaderStage stage)
    {
        var sourcePath = Path.Combine(shaderRoot, relativePath);
        var outputFolder = Path.Combine(Path.GetTempPath(), "tixl-vulkan-spike");
        Directory.CreateDirectory(outputFolder);
        var baseName = Path.Combine(outputFolder, $"{Path.GetFileNameWithoutExtension(relativePath)}.{entryPoint}");
        var spirvPath = baseName + ".spv";
        var reflectionPath = baseName + ".json";

        string[] arguments =
            [
                sourcePath,
                "-entry", entryPoint,
                "-stage", stage == ShaderStage.Vertex ? "vertex" : "fragment",
                "-target", "spirv",
                "-profile", "sm_5_0",
                "-capability", "spirv_1_5",
                "-I", shaderRoot,
                // Legacy D3D9 keyword used by ~225 of TiXL's shaders; FXC accepts it, Slang doesn't.
                "-D", "sampler=SamplerState",
                "-fvk-s-shift", "0", "0",
                "-fvk-b-shift", "16", "0",
                "-fvk-t-shift", "32", "0",
                "-fvk-u-shift", "160", "0",
                // Constant buffers must keep D3D packing: the operators' C# structs are laid out for it.
                "-fvk-use-dx-layout",
                "-o", spirvPath,
                "-reflection-json", reflectionPath,
            ];

        var startInfo = new ProcessStartInfo(FindSlangc())
                            {
                                RedirectStandardError = true,
                                RedirectStandardOutput = true,
                            };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start slangc");
        var diagnostics = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"slangc failed for {relativePath}:{entryPoint}\n{diagnostics}");

        if (!string.IsNullOrWhiteSpace(diagnostics))
            Log.Warning($"slangc {relativePath}:{entryPoint}\n{diagnostics}");

        Log.Info($"Compiled {relativePath}:{entryPoint} in {stopwatch.ElapsedMilliseconds} ms");
        return new CompiledShader(File.ReadAllBytes(spirvPath), ReadBindings(reflectionPath, stage));
    }

    private static List<ShaderBinding> ReadBindings(string reflectionPath, ShaderStage stage)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(reflectionPath));
        var bindings = new List<ShaderBinding>();

        // Global "parameters" lists every resource in the file; the entry point's list says which ones it uses.
        var entryPoint = document.RootElement.GetProperty("entryPoints")[0];
        foreach (var parameter in entryPoint.GetProperty("bindings").EnumerateArray())
        {
            var binding = parameter.GetProperty("binding");
            if (binding.GetProperty("used").GetInt32() == 0 || !binding.TryGetProperty("index", out var index))
                continue;

            var kind = binding.GetProperty("kind").GetString() switch
                           {
                               "constantBuffer"  => ShaderBindingKind.ConstantBuffer,
                               "shaderResource"  => ShaderBindingKind.ShaderResource,
                               "samplerState"    => ShaderBindingKind.Sampler,
                               "unorderedAccess" => ShaderBindingKind.UnorderedAccess,
                               var other         => throw new NotSupportedException($"Unsupported binding kind '{other}' in {reflectionPath}"),
                           };
            bindings.Add(new ShaderBinding(parameter.GetProperty("name").GetString()!, kind, index.GetUInt32(), stage));
        }

        return bindings;
    }

    private static string FindSlangc()
    {
        var configuredPath = Environment.GetEnvironmentVariable("TIXL_SLANGC");
        if (!string.IsNullOrEmpty(configuredPath))
            return configuredPath;

        var pinnedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                      ".local/opt/slang-" + PinnedSlangVersion + "/bin/slangc");
        return File.Exists(pinnedPath) ? pinnedPath : "slangc";
    }

    private const string PinnedSlangVersion = "2026.18";
}

internal enum ShaderStage
{
    Vertex,
    Fragment,
}

internal enum ShaderBindingKind
{
    ConstantBuffer,
    ShaderResource,
    Sampler,
    UnorderedAccess,
}

internal sealed record ShaderBinding(string Name, ShaderBindingKind Kind, uint Binding, ShaderStage Stage);

internal sealed record CompiledShader(byte[] Spirv, List<ShaderBinding> Bindings);
