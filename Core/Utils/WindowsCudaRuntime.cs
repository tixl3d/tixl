using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace T3.Core.Utils;

/// <summary>Windows only: loads the CUDA 12 runtime and cuDNN 9 that the ONNX Runtime GPU build links against, before
/// the CUDA execution provider is appended. The toolkit installer puts CUDA on PATH, but cuDNN's installer keeps its
/// DLLs in a folder of its own that PATH doesn't name, and the provider then fails with "Failed to load shared
/// library" without saying which one is missing.
/// <para>Libraries are looked up through the normal DLL search first, then in a <c>CudaRuntime</c> folder next to the
/// operator, in <c>CUDA_PATH_V12_*</c> and the usual install folders of toolkit and cuDNN. Loaded by absolute path
/// they stay resident, which satisfies the provider's own imports by name without PATH having to change.</para></summary>
public static class WindowsCudaRuntime
{
    /** What onnxruntime_providers_cuda.dll of the 1.24 GPU package links against, plus cuDNN. */
    private static readonly string[] RequiredLibraries =
    {
        "cudart64_12.dll", "cublasLt64_12.dll", "cublas64_12.dll", "curand64_10.dll", "cufft64_11.dll", "cudnn64_9.dll"
    };

    /** The TensorRT 10 runtime the provider DLL imports. Optional helpers are skipped when absent. */
    private static readonly string[] TensorRtLibraries =
    {
        "nvinfer_10.dll", "nvinfer_dispatch_10.dll", "nvinfer_lean_10.dll", "nvinfer_plugin_10.dll", "nvonnxparser_10.dll"
    };

    private static readonly string[] TensorRtRequiredLibraries = { "nvinfer_10.dll", "nvonnxparser_10.dll" };

    private static readonly object Lock = new();
    private static readonly List<IntPtr> LoadedHandles = new();
    private static string _failure;
    private static bool _probed;
    private static string _tensorRtFailure;
    private static string _tensorRtDirectory;
    private static bool _tensorRtProbed;

    public static bool TryPrepare(out string reason)
    {
        lock (Lock)
        {
            if (!_probed)
            {
                _probed = true;
                _failure = Probe();
            }

            reason = _failure ?? string.Empty;
            return _failure == null;
        }
    }

    /// <summary>
    /// Loads the TensorRT 10 runtime before ONNX Runtime appends its TensorRT provider. The provider DLL imports
    /// <c>nvinfer_10.dll</c> by name, so without TensorRT's bin folder on PATH it fails with a bare "Error 126".
    /// Loading the runtime by absolute path (and putting its folder on the process PATH for the builder plugins)
    /// makes the provider load without a system-wide PATH change.
    /// </summary>
    public static bool TryPrepareTensorRt(out string reason, out string directory)
    {
        lock (Lock)
        {
            if (!_tensorRtProbed)
            {
                _tensorRtProbed = true;
                _tensorRtFailure = ProbeTensorRt();
            }

            reason = _tensorRtFailure ?? string.Empty;
            directory = _tensorRtDirectory ?? string.Empty;
            return _tensorRtFailure == null;
        }
    }

    public static bool TryPrepareTensorRt(out string reason) => TryPrepareTensorRt(out reason, out _);

    /// <summary>
    /// Clears the cached probe results, so the next <see cref="TryPrepare"/>/<see cref="TryPrepareTensorRt"/>
    /// re-detects the runtimes. Called when the user changes the CUDA/TensorRT folders in the AI settings.
    /// </summary>
    public static void ResetProbes()
    {
        lock (Lock)
        {
            _probed = false;
            _failure = null;
            _tensorRtProbed = false;
            _tensorRtFailure = null;
            _tensorRtDirectory = null;
        }
    }

    private static string Probe()
    {
        var searchDirs = CollectSearchDirs();
        var missing = new List<string>();

        foreach (var library in RequiredLibraries)
        {
            if (!TryLoad(library, searchDirs))
                missing.Add(library);
        }

        if (missing.Count == 0)
            return null;

        return $"{string.Join(", ", missing)} not found. The ONNX Runtime GPU build needs the CUDA 12 runtime and cuDNN 9 " +
               "(NVIDIA driver 525 or newer, Pascal or newer GPU). Install them, or put their DLLs in a CudaRuntime folder next to the operator";
    }

    private static string ProbeTensorRt()
    {
        var missing = new List<string>();
        foreach (var dir in CollectTensorRtSearchDirs())
        {
            if (!File.Exists(Path.Combine(dir, "nvinfer_10.dll")))
                continue;

            // nvinfer resolves its builder resources and plugins by name at runtime
            AddToProcessPath(dir);
            _tensorRtDirectory = dir;

            missing.Clear();
            foreach (var library in TensorRtLibraries)
            {
                var path = Path.Combine(dir, library);
                if (!File.Exists(path))
                {
                    if (TensorRtRequiredLibraries.Contains(library))
                        missing.Add(library);
                    continue;
                }

                if (NativeLibrary.TryLoad(path, out var handle))
                {
                    LoadedHandles.Add(handle);
                }
                else if (TensorRtRequiredLibraries.Contains(library))
                {
                    missing.Add(library);
                }
            }

            if (missing.Count == 0)
                return null;

            return $"TensorRT 10 found in {dir}, but {string.Join(", ", missing)} could not be loaded";
        }

        return "TensorRT 10 runtime not found (nvinfer_10.dll). Install TensorRT 10, set TENSORRT_ROOT, " +
               "or put its bin folder on PATH";
    }

    private static bool TryLoad(string library, List<string> searchDirs)
    {
        if (NativeLibrary.TryLoad(library, out var handle))
        {
            LoadedHandles.Add(handle);
            return true;
        }

        foreach (var dir in searchDirs)
        {
            var path = Path.Combine(dir, library);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out handle))
            {
                LoadedHandles.Add(handle);
                AddToProcessPath(dir);
                return true;
            }
        }

        return false;
    }

    /** cuDNN 9 loads its engine libraries (cudnn_graph64_9.dll and others) by name when first used, and crashes if they can't be found. */
    private static void AddToProcessPath(string dir)
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(entry.TrimEnd(Path.DirectorySeparatorChar), dir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                return;
        }

        Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + current);
    }

    private static List<string> CollectSearchDirs()
    {
        var dirs = new List<string>();

        void Add(string dir)
        {
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !dirs.Contains(dir))
                dirs.Add(dir);
        }

        var assemblyDir = Path.GetDirectoryName(typeof(WindowsCudaRuntime).Assembly.Location);
        Add(T3.Core.Settings.AiModelSettings.Config.CudaRuntimeDirectory); // the user's choice wins over everything else
        Add(Path.Combine(AppContext.BaseDirectory, "CudaRuntime"));
        if (!string.IsNullOrEmpty(assemblyDir))
            Add(Path.Combine(assemblyDir, "CudaRuntime"));

        // Newest CUDA 12 toolkit first; a CUDA 13 install doesn't carry the 12 runtime and is never a match
        foreach (var variable in Environment.GetEnvironmentVariables().Keys.Cast<string>()
                                            .Where(name => name.StartsWith("CUDA_PATH_V12_", StringComparison.OrdinalIgnoreCase))
                                            .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase))
        {
            Add(Path.Combine(Environment.GetEnvironmentVariable(variable) ?? string.Empty, "bin"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        AddSubfolders(Add, Path.Combine(programFiles, "NVIDIA GPU Computing Toolkit", "CUDA"), "v12.*", "bin");

        // cuDNN 9 installs per CUDA major version: CUDNN\v9.x\bin\12.y\x64
        var cudnnRoot = Path.Combine(programFiles, "NVIDIA", "CUDNN");
        if (Directory.Exists(cudnnRoot))
        {
            foreach (var version in Directory.GetDirectories(cudnnRoot, "v9.*").OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var binDir = Path.Combine(version, "bin");
                if (!Directory.Exists(binDir))
                    continue;

                foreach (var cudaMajor in Directory.GetDirectories(binDir, "12*").OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    Add(Path.Combine(cudaMajor, "x64"));
                    Add(cudaMajor);
                }

                Add(binDir);
            }
        }

        return dirs;
    }

    private static List<string> CollectTensorRtSearchDirs()
    {
        var dirs = new List<string>();

        void Add(string dir)
        {
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !dirs.Contains(dir))
                dirs.Add(dir);
        }

        var configuredTensorRtDir = T3.Core.Settings.AiModelSettings.Config.TensorRtDirectory;
        if (!string.IsNullOrEmpty(configuredTensorRtDir))
        {
            Add(configuredTensorRtDir);
            Add(Path.Combine(configuredTensorRtDir, "bin"));
        }

        foreach (var variable in new[] { "TENSORRT_ROOT", "TENSORRT_PATH", "TRT_PATH", "TENSORRT_HOME" })
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(root))
                continue;

            Add(root);
            Add(Path.Combine(root, "bin"));
            Add(Path.Combine(root, "lib"));
        }

        // A portable drop-in folder next to the editor or operator wins over a machine-wide install
        var assemblyDir = Path.GetDirectoryName(typeof(WindowsCudaRuntime).Assembly.Location);
        Add(Path.Combine(AppContext.BaseDirectory, "TensorRTRuntime"));
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            Add(Path.Combine(assemblyDir, "TensorRTRuntime"));
            Add(Path.Combine(assemblyDir, "CudaRuntime"));
        }

        // Toolkit layout: NVIDIA GPU Computing Toolkit\TensorRT-10.x\bin
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var toolkitRoot = Path.Combine(programFiles, "NVIDIA GPU Computing Toolkit");
        AddSubfolders(Add, toolkitRoot, "TensorRT*", "bin");
        Add(Path.Combine(toolkitRoot, "TensorRT", "bin"));

        // TensorRT 10.8+ installer layout: NVIDIA\TensorRT\10.x\bin or \lib
        var nvidiaRoot = Path.Combine(programFiles, "NVIDIA", "TensorRT");
        AddSubfolders(Add, nvidiaRoot, "*", "bin");
        AddSubfolders(Add, nvidiaRoot, "*", "lib");

        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                               .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            Add(entry.Trim());
        }

        return dirs;
    }

    private static void AddSubfolders(Action<string> add, string root, string pattern, string leaf)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var directory in Directory.GetDirectories(root, pattern).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
        {
            add(Path.Combine(directory, leaf));
        }
    }
}
