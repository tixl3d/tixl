#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Resource.Assets;
using T3.Core.DataTypes.Vector;
using T3.Graphics;

namespace T3.Core.Resource.ShaderCompiling;

/// <summary>
/// Compiles TiXL's HLSL to SPIR-V with Slang, for the Vulkan backend. The shaders are not modified: Slang
/// reads the same sources FXC does, and the register classes are mapped onto binding numbers by
/// <see cref="ShaderSlots"/>, which the backend resolves them with.
/// </summary>
/// <remarks>
/// Runs slangc as a process. The compiler's own API would avoid that, but a process per shader is only paid
/// on a cache miss — the compiled blob is cached on disk by source hash like any other — and it keeps TiXL
/// free of a native dependency it would have to ship for three platforms.
/// </remarks>
public sealed class SlangShaderCompiler : ShaderCompiler
{
    public SlangShaderCompiler(T3.Graphics.Compat.Device device)
    {
        Device = device;
    }

    public T3.Graphics.Compat.Device Device { get; set; }

    /// <summary>Where slangc is, or null when it cannot be found — which is worth saying once, clearly.</summary>
    public static string? FindCompiler()
    {
        var configured = Environment.GetEnvironmentVariable("TIXL_SLANGC");

        if (!string.IsNullOrEmpty(configured))
            return File.Exists(configured) ? configured : null;

        var executable = OperatingSystem.IsWindows() ? "slangc.exe" : "slangc";

        var pinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                  ".local", "opt", "slang-" + PinnedVersion, "bin", executable);

        if (File.Exists(pinned))
            return pinned;

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            var candidate = Path.Combine(directory, executable);

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Compiles one shader without going through the resource system, for tools that check shaders in bulk
    /// and for tests. The normal path is <see cref="ShaderCompiler.TryCompileShaderFromSource{TShader}"/>,
    /// which also caches and resolves includes through the asset registry.
    /// </summary>
    public bool TryCompileSource<TShader>(ShaderCompilationArgs args, out byte[] blob, out string errorMessage)
        where TShader : AbstractShader
        => CompileShaderFromSource<TShader>(args, out blob, out errorMessage);

    protected override bool CompileShaderFromSource<TShader>(ShaderCompilationArgs args, out byte[] blob, out string errorMessage)
    {
        blob = null!;
        var compiler = FindCompiler();

        if (compiler == null)
        {
            errorMessage = $"slangc was not found. Install Slang {PinnedVersion} or point TIXL_SLANGC at it.";
            return false;
        }

        var stage = _stages[typeof(TShader)];
        var workingDirectory = Path.Combine(Path.GetTempPath(), "tixl-slang");
        Directory.CreateDirectory(workingDirectory);

        // Compiled from a file rather than stdin: slangc resolves includes relative to the source, and the
        // editor compiles unsaved buffers, so the text on disk is not necessarily what is being compiled.
        var baseName = Path.Combine(workingDirectory, $"{Path.GetFileNameWithoutExtension(args.Name)}.{args.EntryPoint}.{Environment.CurrentManagedThreadId}");
        // slangc infers the language from the extension, and a shader compiled from an operator's inline
        // source has no file name to take one from.
        var extension = Path.GetExtension(args.Name);
        var sourcePath = baseName + (string.IsNullOrEmpty(extension) ? ".hlsl" : extension);
        var spirvPath = baseName + ".spv";
        var reflectionPath = baseName + ".json";

        try
        {
            File.WriteAllText(sourcePath, args.SourceCode);

            var arguments = BuildArguments(sourcePath, args, stage, spirvPath, reflectionPath);

            if (!TryRun(compiler, arguments, out var diagnostics))
            {
                errorMessage = DX11ShaderCompiler.ExtractMeaningfulShaderErrorMessage(diagnostics);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(diagnostics))
                Log.Warning($"slangc {args.Name}@{args.EntryPoint}\n{diagnostics}");

            var bindings = ReadBindings(reflectionPath, stage, out var threadGroups);
            blob = SpirvBlob.Pack(File.ReadAllBytes(spirvPath), bindings, threadGroups);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            return false;
        }
        finally
        {
            Delete(sourcePath);
            Delete(spirvPath);
            Delete(reflectionPath);
        }
    }

    private List<string> BuildArguments(string sourcePath, in ShaderCompilationArgs args, ShaderStage stage, string spirvPath,
                                        string reflectionPath)
    {
        var stageBase = ShaderSlots.BaseOf(stage);

        List<string> arguments =
            [
                sourcePath,
                "-entry", args.EntryPoint,
                "-stage", StageName(stage),
                "-target", "spirv",
                "-profile", "sm_5_0",

                // The SPIR-V the backend's Vulkan 1.3 device accepts.
                "-capability", "spirv_1_5",

                // A D3D9 keyword some 225 of TiXL's shaders still use, which FXC accepts and Slang does not.
                "-D", "sampler=SamplerState",

                // Constant buffers keep D3D's packing, because the operators' C# structs are laid out for it.
                "-fvk-use-dx-layout",

                // Each register class lands in its own range, offset per stage so a vertex shader's t0 and a
                // pixel shader's t0 do not collide in the one descriptor set.
                "-fvk-s-shift", (stageBase + ShaderSlots.SamplerBase).ToString(), "0",
                "-fvk-b-shift", (stageBase + ShaderSlots.ConstantBufferBase).ToString(), "0",
                "-fvk-t-shift", (stageBase + ShaderSlots.ShaderResourceBase).ToString(), "0",
                "-fvk-u-shift", (stageBase + ShaderSlots.UnorderedAccessBase).ToString(), "0",
                "-o", spirvPath,
                "-reflection-json", reflectionPath,
            ];

        foreach (var directory in IncludeDirectories(args))
        {
            arguments.Add("-I");
            arguments.Add(directory);
        }

        return arguments;
    }

    /// <summary>
    /// Where slangc looks for an <c>#include</c>. TiXL resolves those through the asset registry, which can
    /// place a shared include in any package, so every directory that actually holds one is passed.
    /// </summary>
    private static IEnumerable<string> IncludeDirectories(in ShaderCompilationArgs args)
    {
        var directories = new List<string>();

        foreach (var package in args.Owner.AvailableResourcePackages)
        {
            Add(package.AssetsFolder);
            Add(Path.Combine(package.AssetsFolder, "shaders"));
        }

        foreach (var include in GetIncludesFrom(args.SourceCode))
        {
            if (TryResolveSharedIncludeAsset(include, out var asset))
                Add(Path.GetDirectoryName(asset.FullPath));
        }

        return directories;

        void Add(string? directory)
        {
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory) && !directories.Contains(directory))
                directories.Add(directory);
        }
    }

    private static bool TryRun(string compiler, List<string> arguments, out string diagnostics)
    {
        var startInfo = new ProcessStartInfo(compiler)
                            {
                                RedirectStandardError = true,
                                RedirectStandardOutput = true,
                                UseShellExecute = false,
                            };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {compiler}.");
        diagnostics = (process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd()).Trim();

        if (!process.WaitForExit(CompileTimeoutMs))
        {
            process.Kill(entireProcessTree: true);
            diagnostics = $"slangc did not finish within {CompileTimeoutMs / 1000} seconds.";
            return false;
        }

        return process.ExitCode == 0;
    }

    /// <summary>
    /// What the entry point actually uses, which is not the same as what the file declares: Slang reports
    /// every resource in the file, and binding one a shader ignores would fail against its layout.
    /// </summary>
    private static List<ShaderBinding> ReadBindings(string reflectionPath, ShaderStage stage, out Int3 threadGroups)
    {
        var bindings = new List<ShaderBinding>();
        threadGroups = new Int3(1, 1, 1);
        using var document = JsonDocument.Parse(File.ReadAllText(reflectionPath));

        if (!document.RootElement.TryGetProperty("entryPoints", out var entryPoints) || entryPoints.GetArrayLength() == 0)
            return bindings;

        var entryPoint = entryPoints[0];

        // A compute shader's group size used to be read out of its DXBC; it comes from here now.
        if (entryPoint.TryGetProperty("threadGroupSize", out var groupSize) && groupSize.GetArrayLength() == 3)
            threadGroups = new Int3(groupSize[0].GetInt32(), groupSize[1].GetInt32(), groupSize[2].GetInt32());

        if (!entryPoint.TryGetProperty("bindings", out var parameters))
            return bindings;

        // What each resource actually is. The entry point's list gives the register class only, and a t
        // register is a texture or a buffer depending on how the shader declared it - which decides the
        // descriptor type the pipeline is built with.
        var shapes = ReadResourceShapes(document.RootElement);

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (!parameter.TryGetProperty("binding", out var binding))
                continue;

            if (!binding.TryGetProperty("used", out var used) || used.GetInt32() == 0)
                continue;

            if (!binding.TryGetProperty("index", out var index))
                continue;

            var name = parameter.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? string.Empty : string.Empty;
            var isBuffer = shapes.TryGetValue(name, out var shape) && shape.Contains("buffer", StringComparison.OrdinalIgnoreCase);
            var kindName = binding.GetProperty("kind").GetString();

            var kind = kindName switch
                           {
                               "constantBuffer"  => BindingKind.ConstantBuffer,
                               "shaderResource"  => isBuffer ? BindingKind.StructuredBuffer : BindingKind.SampledTexture,
                               "samplerState"    => BindingKind.Sampler,
                               "unorderedAccess" => isBuffer ? BindingKind.StorageBuffer : BindingKind.StorageTexture,
                               _                 => (BindingKind?)null,
                           };

            // A texel buffer is its own descriptor type in Vulkan and the backend has none; three shaders
            // use one, and they would bind the wrong thing silently.
            if (shape == "textureBuffer")
                Log.Warning($"'{name}' is a Buffer<T>, which the Vulkan backend does not support yet.");

            if (kind == null)
            {
                Log.Warning($"Ignoring shader binding of unknown kind '{kindName}' in {Path.GetFileName(reflectionPath)}.");
                continue;
            }

            var set = binding.TryGetProperty("space", out var space) ? space.GetInt32() : 0;
            bindings.Add(new ShaderBinding(set, index.GetInt32(), kind.Value, name));
        }

        return bindings;
    }

    /// <summary>
    /// The declared shape of every resource in the file, by name: texture2D, structuredBuffer and so on.
    /// Slang reports it in the global parameter list rather than on the entry point's bindings.
    /// </summary>
    private static Dictionary<string, string> ReadResourceShapes(JsonElement root)
    {
        var shapes = new Dictionary<string, string>();

        if (!root.TryGetProperty("parameters", out var parameters))
            return shapes;

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (!parameter.TryGetProperty("name", out var name) || !parameter.TryGetProperty("type", out var type))
                continue;

            if (type.TryGetProperty("baseShape", out var baseShape))
                shapes[name.GetString() ?? string.Empty] = baseShape.GetString() ?? string.Empty;
        }

        return shapes;
    }

    protected override void CreateShaderInstance<TShader>(string name, in byte[] blob, out TShader shader)
    {
        if (!SpirvBlob.TryUnpack(blob, out var spirv, out var bindings, out _))
            throw new InvalidOperationException($"The cached shader '{name}' is not SPIR-V. Delete the shader cache and restart.");

        var stage = _stages[typeof(TShader)];

        // Slang names every SPIR-V entry point main, whatever the HLSL called it.
        var gpuShader = Device.Backend.CreateShader(stage, spirv, "main", bindings, name);

        AbstractShader created = stage switch
                                     {
                                         ShaderStage.Vertex   => new VertexShader(new T3.Graphics.Compat.VertexShader(Device, gpuShader), blob),
                                         ShaderStage.Pixel    => new PixelShader(new T3.Graphics.Compat.PixelShader(Device, gpuShader), blob),
                                         ShaderStage.Geometry => new GeometryShader(new T3.Graphics.Compat.GeometryShader(Device, gpuShader), blob),
                                         _                    => new ComputeShader(new T3.Graphics.Compat.ComputeShader(Device, gpuShader), blob),
                                     };

        created.Name = name;
        shader = (TShader)created;
    }

    private static string StageName(ShaderStage stage)
    {
        return stage switch
                   {
                       ShaderStage.Vertex   => "vertex",
                       ShaderStage.Pixel    => "fragment",
                       ShaderStage.Geometry => "geometry",
                       _                    => "compute",
                   };
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover in the temp folder is not worth failing a compile over.
        }
    }

    private const string PinnedVersion = "2026.18";
    private const int CompileTimeoutMs = 60_000;

    private static readonly IReadOnlyDictionary<Type, ShaderStage> _stages = new Dictionary<Type, ShaderStage>
                                                                                {
                                                                                    { typeof(VertexShader), ShaderStage.Vertex },
                                                                                    { typeof(PixelShader), ShaderStage.Pixel },
                                                                                    { typeof(GeometryShader), ShaderStage.Geometry },
                                                                                    { typeof(ComputeShader), ShaderStage.Compute },
                                                                                };

}
