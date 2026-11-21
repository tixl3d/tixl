using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using T3.Core.Logging;
using T3.Core.Utils;

namespace t3.streamdiffusion.Onnx;

internal enum ExecutionProvider
{
    Cpu,
    DirectMl,
    Cuda,
    TensorRt,
    CoreMl,

    /** Resolved at session setup to the fastest engine this machine can actually run. */
    Auto
}

/// <summary>
/// Wraps an InferenceSession with GPU execution (CUDA/TensorRT, DirectML fallthrough,
/// CPU fallback) and input/output name discovery tolerant of differing export conventions.
/// </summary>
internal sealed class OnnxModelSession : IDisposable
{
    /// <summary>
    /// TensorRT optimization profile in ORT's <c>name:dim0xdim1,...</c> syntax. Without explicit profiles the
    /// provider only covers the shape the graph was traced with, so every other batch size rebuilds a large engine.
    /// </summary>
    internal sealed record TensorRtProfile(string MinShapes, string OptShapes, string MaxShapes);

    public InferenceSession Session { get; }
    public string Name { get; }
    public string ProviderLabel { get; }

    // ORT's RunWithBinding(RunOptions, OrtIoBinding) dereferences runOptions.Handle without a
    // null check — passing null throws NullReferenceException from managed interop (v1.20.1).
    private readonly RunOptions _runOptions = new();

    private static readonly object PreloadLock = new();
    private static readonly object TensorRtCreationLock = new();
    private static bool _preloadAttempted;
    private static readonly List<IntPtr> PinnedCudaModules = new();

    // The CUDA/cuDNN runtime DLLs the provider DLLs statically import. Loading them
    // explicitly (absolute path) before the provider resolves: the Windows loader
    // then satisfies the provider's imports from these already-loaded modules.
    private static readonly string[] CudaRuntimeDlls =
    {
        "cudart64_13.dll", "cublas64_13.dll", "cublasLt64_13.dll", "cufft64_12.dll",
        "cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll", "cufft64_11.dll", "curand64_10.dll",
        "cudnn_ops64_9.dll", "cudnn_cnn64_9.dll", "cudnn_adv64_9.dll", "cudnn_graph64_9.dll",
        "cudnn_heuristic64_9.dll", "cudnn_engines_precompiled64_9.dll", "cudnn_engines_runtime_compiled64_9.dll",
        "cudnn_engines_tensor_ir64_9.dll", "cudnn_ext64_9.dll", "cudnn64_9.dll",
        "nvinfer.dll", "nvonnxparser.dll"
    };

    private OnnxModelSession(InferenceSession session, string name, string providerLabel)
    {
        Session = session;
        Name = name;
        ProviderLabel = providerLabel;
    }

    public static OnnxModelSession? Create(string modelPath, int deviceId, string name,
        ExecutionProvider provider = ExecutionProvider.Cuda, TensorRtProfile? tensorRtProfile = null)
    {
        if (!File.Exists(modelPath))
        {
            Log.Error($"[StreamDiffusion] {name}: model file not found: {modelPath}");
            return null;
        }

        // The GPU ORT build has no DirectML, and CUDA/TensorRT need NVIDIA runtime
        // DLLs - walk the chain and keep the first provider that initializes; CPU
        // always works and is always last
        var chain = provider switch
        {
            ExecutionProvider.Cpu => new[] { ExecutionProvider.Cpu },
            ExecutionProvider.DirectMl => new[] { ExecutionProvider.DirectMl, ExecutionProvider.TensorRt, ExecutionProvider.Cuda, ExecutionProvider.CoreMl, ExecutionProvider.Cpu },
            ExecutionProvider.Cuda => new[] { ExecutionProvider.Cuda, ExecutionProvider.TensorRt, ExecutionProvider.CoreMl, ExecutionProvider.Cpu },
            ExecutionProvider.TensorRt => new[] { ExecutionProvider.TensorRt, ExecutionProvider.Cuda, ExecutionProvider.CoreMl, ExecutionProvider.Cpu },
            ExecutionProvider.CoreMl => new[] { ExecutionProvider.CoreMl, ExecutionProvider.Cpu },
            _ => new[] { ExecutionProvider.Cpu }
        };

        // CPU has no provider DLL to satisfy; loading the CUDA runtime for it only costs memory and startup time
        if (provider != ExecutionProvider.Cpu)
            PreloadCudaRuntimes(modelPath);

        // Windows has a CUDA and a DirectML ONNX Runtime native; the first request of the process binds one
        var wantedBuild = provider == ExecutionProvider.DirectMl ? OnnxNativeSelector.Build.DirectMl : OnnxNativeSelector.Build.Gpu;
        var build = OnnxNativeSelector.Select(typeof(InferenceSession).Assembly, wantedBuild);
        if (OperatingSystem.IsWindows() && provider is ExecutionProvider.DirectMl or ExecutionProvider.Cuda or ExecutionProvider.TensorRt && build != wantedBuild)
        {
            // A runtime that never ships a DirectML native (the last DirectML build is 1.24.4) cannot be switched to by restarting,
            // so say that instead of suggesting a restart that would never help.
            if (wantedBuild == OnnxNativeSelector.Build.DirectMl
                && !OnnxNativeSelector.IsShipped(typeof(InferenceSession).Assembly, OnnxNativeSelector.Build.DirectMl))
            {
                Log.Warning($"[StreamDiffusion] {name}: this ONNX Runtime ships no DirectML provider (DirectML builds ended " +
                            $"after 1.24); continuing on the {OnnxNativeSelector.Describe(build)} build with CUDA/CPU.");
            }
            else
            {
                Log.Warning($"[StreamDiffusion] {name}: {OnnxNativeSelector.Describe(wantedBuild)} needs its own ONNX Runtime build, but this session already loaded " +
                            $"the {OnnxNativeSelector.Describe(build)} one - restart TiXL to switch.");
            }
        }

        foreach (var candidate in chain)
        {
            if (!IsProviderAvailableOnThisPlatform(candidate, build))
                continue;

            var cudaMissing = string.Empty;
            if (candidate is ExecutionProvider.Cuda or ExecutionProvider.TensorRt
                && ((OperatingSystem.IsLinux() && !LinuxCudaRuntime.TryPrepare(out cudaMissing))
                    || (OperatingSystem.IsWindows() && !WindowsCudaRuntime.TryPrepare(out cudaMissing))))
            {
                Log.Info($"[StreamDiffusion] {name}: {EpLabel(candidate)} unavailable ({cudaMissing}), trying next provider");
                continue;
            }

            // TensorRT's provider DLL imports nvinfer_10.dll by name; load the runtime before asking ORT for the provider
            if (candidate == ExecutionProvider.TensorRt)
            {
                var tensorRtReady = OperatingSystem.IsLinux()
                                        ? LinuxCudaRuntime.TryPrepareTensorRt(out var trtMissing, out var trtDirectory)
                                        : WindowsCudaRuntime.TryPrepareTensorRt(out trtMissing, out trtDirectory);
                if (!tensorRtReady)
                {
                    Log.Info($"[StreamDiffusion] {name}: TensorRT unavailable ({trtMissing}), trying next provider");
                    continue;
                }

                Log.Info($"[StreamDiffusion] {name}: TensorRT runtime at {trtDirectory}");
            }

            SessionOptions? options = null;
            try
            {
                options = CreateSessionOptions(candidate, deviceId, modelPath, tensorRtProfile);

                // TensorRT writes engine/timing cache files while the session is created. Two sessions built at the
                // same time can target the same cache, so creation is serialized for that provider only.
                InferenceSession session;
                if (candidate == ExecutionProvider.TensorRt)
                {
                    lock (TensorRtCreationLock)
                        session = new InferenceSession(modelPath, options);
                }
                else
                {
                    session = new InferenceSession(modelPath, options);
                }

                Log.Info($"[StreamDiffusion] {name}: ready on {EpLabel(candidate)}");
                return new OnnxModelSession(session, name, EpLabel(candidate));
            }
            catch (Exception ex) when (candidate != ExecutionProvider.Cpu)
            {
                Log.Info($"[StreamDiffusion] {name}: {EpLabel(candidate)} unavailable ({FirstLine(ex.Message)}), trying next provider");
                options?.Dispose();
            }
            catch
            {
                options?.Dispose();
                throw;
            }
        }

        Log.Error($"[StreamDiffusion] {name}: failed to create session");
        return null;
    }

    /// <summary>
    /// GPU sessions keep a small CPU pool (only pre/post-processing nodes run there),
    /// while CPU-fallback sessions get the full core count — the previous fixed 2 threads
    /// made a CPU-only UNet 4-8x slower than necessary.
    /// </summary>
    private static SessionOptions CreateSessionOptions(ExecutionProvider provider, int deviceId, string modelPath,
                                                       TensorRtProfile? tensorRtProfile)
    {
        var options = new SessionOptions
        {
            // ORT 1.24 fails to initialize fp16 transformer graphs (SimplifiedLayerNormFusion) on the CPU at ENABLE_ALL; EXTENDED costs nothing measurable
            GraphOptimizationLevel = provider == ExecutionProvider.Cpu ? GraphOptimizationLevel.ORT_ENABLE_EXTENDED : GraphOptimizationLevel.ORT_ENABLE_ALL,
            // CPU inference runs on a worker, but leave one core for the render thread instead of saturating the machine
            IntraOpNumThreads = provider == ExecutionProvider.Cpu ? Math.Max(2, Environment.ProcessorCount - 1) : 2,
        };

        switch (provider)
        {
            case ExecutionProvider.DirectMl:
                // Required by the DirectML provider: it neither plans memory ahead nor runs nodes in parallel
                options.EnableMemoryPattern = false;
                options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                options.AppendExecutionProvider_DML(deviceId);
                break;

            case ExecutionProvider.Cuda:
                // HEURISTIC conv-algorithm search avoids cuDNN's exhaustive autotune, which stalls
                // session creation and re-runs on every new shape (the KV-cache batch changes 1-4).
                // A large conv workspace lets cuDNN choose the fastest algorithms it finds.
                using (var cudaOptions = new OrtCUDAProviderOptions())
                {
                    cudaOptions.UpdateOptions(new Dictionary<string, string>
                    {
                        ["device_id"] = deviceId.ToString(),
                        ["cudnn_conv_algo_search"] = "HEURISTIC",
                        ["cudnn_conv_use_max_workspace"] = "1",
                    });
                    options.AppendExecutionProvider_CUDA(cudaOptions);
                }
                break;

            case ExecutionProvider.CoreMl:
                options.AppendExecutionProvider_CoreML(CoreMLFlags.COREML_FLAG_USE_NONE);
                break;

            case ExecutionProvider.TensorRt:
                // Engine building is slow (minutes per input shape) - persist engines
                // next to the models so only the first use of a resolution stalls
                var trtCache = Path.Combine(Path.GetDirectoryName(modelPath) ?? ".", "trt_cache");
                Directory.CreateDirectory(trtCache);
                using (var trtOptions = new OrtTensorRTProviderOptions())
                {
                    // The builder needs transient workspace on top of the resident sessions. The old 2 GiB cap
                    // made the VAE decoder's engine build fail on a 16 GB card even with 15 GB free (the builder
                    // reports the cap as OutOfMemory), which then wedged the stream. 4 GiB builds it; override
                    // with TIXL_SD_TRT_WORKSPACE_MB on smaller cards.
                    var workspaceMb = int.TryParse(Environment.GetEnvironmentVariable("TIXL_SD_TRT_WORKSPACE_MB"), out var configured) && configured > 0
                                          ? configured
                                          : 4096;
                    var trtSettings = new Dictionary<string, string>
                    {
                        ["device_id"] = deviceId.ToString(),
                        ["trt_max_workspace_size"] = (workspaceMb * 1024L * 1024L).ToString(),
                        // fp16 engines: roughly doubles throughput on fp32 exports and halves engine
                        // memory; fp16 exports rebind to fp16 anyway, so this never hurts
                        ["trt_fp16_enable"] = "1",
                        ["trt_engine_cache_enable"] = "1",
                        ["trt_engine_cache_path"] = trtCache,
                        ["trt_timing_cache_enable"] = "1",
                        ["trt_timing_cache_path"] = trtCache
                    };

                    if (tensorRtProfile != null)
                    {
                        trtSettings["trt_profile_min_shapes"] = tensorRtProfile.MinShapes;
                        trtSettings["trt_profile_opt_shapes"] = tensorRtProfile.OptShapes;
                        trtSettings["trt_profile_max_shapes"] = tensorRtProfile.MaxShapes;
                    }

                    trtOptions.UpdateOptions(trtSettings);
                    options.AppendExecutionProvider_Tensorrt(trtOptions);
                }
                break;
        }

        return options;
    }

    /** DirectML exists only in the Windows DirectML build, CUDA and TensorRT in the Windows GPU build and on Linux. */
    private static bool IsProviderAvailableOnThisPlatform(ExecutionProvider provider, OnnxNativeSelector.Build build)
    {
        return provider switch
        {
            ExecutionProvider.DirectMl => OperatingSystem.IsWindows() && build == OnnxNativeSelector.Build.DirectMl,
            ExecutionProvider.CoreMl => OperatingSystem.IsMacOS(),
            ExecutionProvider.Cuda or ExecutionProvider.TensorRt => (OperatingSystem.IsWindows() && build == OnnxNativeSelector.Build.Gpu) || OperatingSystem.IsLinux(),
            _ => true
        };
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetDllDirectory(string? lpPathName);

    private static void PreloadCudaRuntimes(string modelPath)
    {
        // The explicit preload works around the Windows loader; Linux resolves the CUDA libraries
        // through ld.so (LD_LIBRARY_PATH / ldconfig) when the provider library loads.
        if (!OperatingSystem.IsWindows())
            return;

        lock (PreloadLock)
        {
            if (_preloadAttempted)
                return; // already loaded (or attempted) - also covers concurrent sessions initializing

            _preloadAttempted = true;

            var candidateDirs = new List<string>();

            void AddDir(string? d)
            {
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d) && !candidateDirs.Contains(d))
                    candidateDirs.Add(d);
            }

            AddDir(Path.GetDirectoryName(Environment.ProcessPath));
            AddDir(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName));
            AddDir(AppContext.BaseDirectory);
            AddDir(Path.Combine(AppContext.BaseDirectory, "CudaRuntime"));
            AddDir(Path.GetDirectoryName(modelPath));
            if (!string.IsNullOrEmpty(modelPath))
                AddDir(Path.Combine(Path.GetDirectoryName(modelPath)!, "CudaRuntime"));

            var asmLocation = typeof(OnnxModelSession).Assembly.Location;
            if (!string.IsNullOrEmpty(asmLocation))
            {
                var asmDir = Path.GetDirectoryName(asmLocation);
                AddDir(asmDir);
                if (!string.IsNullOrEmpty(asmDir))
                    AddDir(Path.Combine(asmDir, "CudaRuntime"));
            }

            // Walk up directory tree to search for repo root CudaRuntime or Editor bin folders
            var dirInfo = new DirectoryInfo(AppContext.BaseDirectory);
            while (dirInfo != null)
            {
                var dirPath = dirInfo.FullName;
                AddDir(Path.Combine(dirPath, "Operators", "DepthAnything", "CudaRuntime"));
                AddDir(Path.Combine(dirPath, "Operators", "StreamDiffusion", "CudaRuntime"));
                AddDir(Path.Combine(dirPath, "Editor", "bin", "Debug", "net10.0-windows"));
                AddDir(Path.Combine(dirPath, "Editor", "bin", "Release", "net10.0-windows"));
                AddDir(Path.Combine(dirPath, "CudaRuntime"));
                dirInfo = dirInfo.Parent;
            }

            var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
            if (!string.IsNullOrEmpty(cudaPath))
                AddDir(Path.Combine(cudaPath, "bin"));

            string? primaryCudaDir = null;
            foreach (var dir in candidateDirs)
            {
                if (File.Exists(Path.Combine(dir, "cublas64_13.dll")) || File.Exists(Path.Combine(dir, "cublas64_12.dll")))
                {
                    primaryCudaDir = dir;
                    break;
                }
            }

            if (primaryCudaDir != null)
            {
                try
                {
                    SetDllDirectory(primaryCudaDir);
                }
                catch { }
            }

            foreach (var dll in CudaRuntimeDlls)
            {
                foreach (var dir in candidateDirs)
                {
                    var candidate = Path.Combine(dir, dll);
                    if (!File.Exists(candidate))
                        continue;

                    if (NativeLibrary.TryLoad(candidate, out var handle))
                    {
                        PinnedCudaModules.Add(handle);
                    }
                    break;
                }
            }

            if (PinnedCudaModules.Count > 0)
                Log.Info($"[StreamDiffusion] Preloaded {PinnedCudaModules.Count} CUDA/cuDNN runtime DLLs from {primaryCudaDir ?? "system"}");
        }
    }

    private static string FirstLine(string message)
    {
        return message.Split('\n')[0].Trim();
    }

    private static string EpLabel(ExecutionProvider provider)
    {
        return provider switch
        {
            ExecutionProvider.DirectMl => "DirectML",
            ExecutionProvider.Cuda => "CUDA",
            ExecutionProvider.TensorRt => "TensorRT",
            ExecutionProvider.CoreMl => "CoreML",
            _ => "CPU"
        };
    }

    public string PrimaryInputName => FirstKey(Session.InputMetadata);
    public string PrimaryOutputName => FirstKey(Session.OutputMetadata);

    public IReadOnlyDictionary<string, NodeMetadata> Inputs => Session.InputMetadata;
    public IReadOnlyDictionary<string, NodeMetadata> Outputs => Session.OutputMetadata;

    /// <summary>
    /// True when the named input expects Float16 tensors. fp16 exports (e.g. the official
    /// onnxruntime/sd-turbo files) keep fp16 all the way through the graph, unlike optimum exports
    /// that keep fp16 weights but cast the IO back to fp32.
    /// </summary>
    public bool InputIsFloat16(string inputName)
        => Session.InputMetadata.TryGetValue(inputName, out var metadata)
           && metadata.ElementDataType == TensorElementType.Float16;

    /// <summary>True when the named output produces Float16 tensors.</summary>
    public bool OutputIsFloat16(string outputName)
        => Session.OutputMetadata.TryGetValue(outputName, out var metadata)
           && metadata.ElementDataType == TensorElementType.Float16;

    /// <summary>
    /// Finds a float input by tensor rank, optionally preferring a name fragment.
    /// Used to locate the UNet sample/timestep/embeddings across export variants.
    /// </summary>
    public string? FindInput(int rank, string? preferredNameFragment = null)
    {
        if (preferredNameFragment != null)
        {
            foreach (var (name, metadata) in Session.InputMetadata)
            {
                if (name.Contains(preferredNameFragment, StringComparison.OrdinalIgnoreCase)
                    && metadata.Dimensions is { Length: var inputRank } && inputRank == rank)
                {
                    return name;
                }
            }
        }

        foreach (var (name, metadata) in Session.InputMetadata)
        {
            if (metadata.Dimensions is { Length: var fallbackRank } && fallbackRank == rank)
            {
                return name;
            }
        }

        return null;
    }

    public DenseTensor<float> RunFloat(DenseTensor<float> input, string inputName, string outputName)
    {
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, input) };
        using var outputs = Session.Run(inputs, new[] { outputName });

        foreach (var output in outputs)
        {
            if (output.AsTensor<float>() is DenseTensor<float> dense)
            {
                var copy = new DenseTensor<float>(dense.Dimensions);
                dense.Buffer.Span.CopyTo(copy.Buffer.Span);
                return copy;
            }

            if (output.AsTensor<Float16>() is DenseTensor<Float16> half)
            {
                var copy = new DenseTensor<float>(half.Dimensions);
                ConvertHalfToFloat(half.Buffer.Span, copy.Buffer.Span);
                return copy;
            }
        }

        throw new InvalidOperationException($"{Name}: output '{outputName}' is not a float tensor");
    }

    /// <summary>Creates an IO binding for zero-copy/persistent-buffer inference.</summary>
    public OrtIoBinding CreateIoBinder()
    {
        return Session.CreateIoBinding();
    }

    /// <summary>Runs the session with a pre-bound IO binding (inputs/outputs must be bound).</summary>
    public void RunBound(OrtIoBinding binding)
    {
        Session.RunWithBinding(_runOptions, binding);
    }

    private static string FirstKey(IReadOnlyDictionary<string, NodeMetadata> metadata)
    {
        foreach (var key in metadata.Keys)
        {
            return key;
        }

        return string.Empty;
    }

    /** Converts a Float16 buffer to float32 element by element. */
    internal static void ConvertHalfToFloat(ReadOnlySpan<Float16> source, Span<float> target)
    {
        for (var i = 0; i < source.Length; i++)
            target[i] = source[i].ToFloat();
    }

    /** Converts a float buffer to Float16 element by element. */
    internal static void ConvertFloatToHalf(ReadOnlySpan<float> source, Span<Float16> target)
    {
        for (var i = 0; i < source.Length; i++)
            target[i] = (Float16)source[i];
    }

    public void Dispose()
    {
        _runOptions.Dispose();
        Session.Dispose();
    }
}
