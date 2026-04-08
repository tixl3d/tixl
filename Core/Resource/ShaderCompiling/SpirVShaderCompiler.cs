#if !PLATFORM_WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using T3.Core.DataTypes;
using T3.Core.Logging;

namespace T3.Core.Resource.ShaderCompiling;

/// <summary>
/// Linux shader compiler that uses Microsoft's dxc (DirectXShaderCompiler) to compile
/// HLSL source to SPIR-V bytecode. The dxc binary must be available on PATH or at a known location.
/// </summary>
public sealed class SpirVShaderCompiler : ShaderCompiler
{
    private static readonly Dictionary<Type, string> ShaderProfiles = new()
    {
        { typeof(ComputeShader), "cs_6_0" },
        { typeof(PixelShader), "ps_6_0" },
        { typeof(VertexShader), "vs_6_0" },
        { typeof(GeometryShader), "gs_6_0" },
    };

    private string? _dxcPath;

    private string FindDxc()
    {
        if (_dxcPath != null)
            return _dxcPath;

        // Check common locations
        string[] searchPaths =
        [
            "dxc", // on PATH
            "/usr/bin/dxc",
            "/usr/local/bin/dxc",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dxc"),
        ];

        foreach (var path in searchPaths)
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (process != null)
                {
                    process.WaitForExit(3000);
                    if (process.ExitCode == 0)
                    {
                        _dxcPath = path;
                        Log.Debug($"[SpirVShaderCompiler] Found dxc at: {path}");
                        return path;
                    }
                }
            }
            catch
            {
                // not found at this path
            }
        }

        throw new FileNotFoundException(
            "dxc (DirectXShaderCompiler) not found. Install it via your package manager " +
            "(e.g., 'sudo apt install dxc' or 'sudo pacman -S directx-shader-compiler').");
    }

    protected override bool CompileShaderFromSource<TShader>(ShaderCompilationArgs args, out byte[] blob, out string errorMessage)
    {
        blob = [];
        errorMessage = string.Empty;

        if (!ShaderProfiles.TryGetValue(typeof(TShader), out var profile))
        {
            errorMessage = $"Unsupported shader type: {typeof(TShader).Name}";
            return false;
        }

        try
        {
            var dxcPath = FindDxc();

            // Write source to a temp file (dxc needs a file input for includes)
            var tempDir = Path.Combine(Path.GetTempPath(), "tixl-shaders");
            Directory.CreateDirectory(tempDir);
            var id = Path.GetRandomFileName().Replace(".", "");
            var sourceFile = Path.Combine(tempDir, $"{id}.hlsl");
            var outputFile = Path.Combine(tempDir, $"{id}.spv");
            File.WriteAllText(sourceFile, args.SourceCode, Encoding.UTF8);

            // Build dxc command: compile HLSL to SPIR-V
            var dxcArgs = new StringBuilder();
            dxcArgs.Append($"-spirv ");
            dxcArgs.Append($"-T {profile} ");
            dxcArgs.Append($"-E {args.EntryPoint} ");
            dxcArgs.Append($"-Fo \"{outputFile}\" ");
            dxcArgs.Append($"\"{sourceFile}\" ");
            // Vulkan 1.1 target
            dxcArgs.Append("-fspv-target-env=vulkan1.1 ");
            // Shift register bindings for Vulkan compatibility
            dxcArgs.Append("-fvk-use-dx-layout ");

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = dxcPath,
                Arguments = dxcArgs.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process == null)
            {
                errorMessage = "Failed to start dxc process";
                return false;
            }

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            if (process.ExitCode != 0)
            {
                errorMessage = $"dxc compilation failed (exit code {process.ExitCode}):\n{stderr}";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Log.Warning($"[SpirVShaderCompiler] dxc warnings for {args.Name}:\n{stderr}");
            }

            if (File.Exists(outputFile))
            {
                blob = File.ReadAllBytes(outputFile);
                TryDeleteFile(sourceFile);
                TryDeleteFile(outputFile);
                return true;
            }

            TryDeleteFile(sourceFile);
            errorMessage = "dxc produced no output file";
            return false;
        }
        catch (Exception e)
        {
            errorMessage = $"SpirV compilation exception: {e.Message}";
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { /* best-effort cleanup */ }
    }

    protected override void CreateShaderInstance<TShader>(string name, in byte[] blob, out TShader shader)
    {
        // Create stub shader instances that hold the SPIR-V bytecode.
        // The actual Veldrid shader objects will be created when the GPU backend loads them.
        AbstractShader instance;

        if (typeof(TShader) == typeof(ComputeShader))
            instance = new ComputeShader();
        else if (typeof(TShader) == typeof(PixelShader))
            instance = new PixelShader();
        else if (typeof(TShader) == typeof(VertexShader))
            instance = new VertexShader();
        else if (typeof(TShader) == typeof(GeometryShader))
            instance = new GeometryShader();
        else
            throw new NotSupportedException($"Unsupported shader type: {typeof(TShader).Name}");

        instance.Name = name;
        instance.CompiledBytecode = blob;
        shader = (TShader)instance;
    }
}
#endif
