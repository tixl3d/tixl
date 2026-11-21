#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using T3.Core.Logging;
using T3.Core.Settings;
using T3.Core.Utils;

namespace T3.Editor.Gui.AiModels;

/**
 * Detects the machine's GPU situation and recommends the best engine for the ONNX operators, so
 * the settings page can answer "which acceleration do I get?" in one plain-language line instead
 * of a runtime matrix. GPU names are detected once per session; engine probes reuse the cached
 * CUDA/TensorRT probes the operators share.
 */
internal static class AiEngineAdvisor
{
    public enum Engine { TensorRt, Cuda, DirectMl, CoreMl, Cpu }

    public sealed class Recommendation
    {
        public required Engine Engine;
        public required string Title;
        public required string Summary;

        /** Optional next step shown on the settings page, e.g. the TensorRT download hint. */
        public string Hint = "";

        /** Optional link target for a "Show me…" button next to the hint. */
        public string? ActionUrl;
    }

    private const string TensorRtDownloadUrl = "https://developer.nvidia.com/tensorrt-download";
    private const string NvidiaDriverUrl = "https://www.nvidia.com/download/index.aspx";

    /** GPU display names; queried once per session because WMI and process spawning are not free. */
    private static string[]? _gpuNames;
    private static bool _gpuNamesQueried;

    public static string[] GetGpuNames()
    {
        if (_gpuNamesQueried)
            return _gpuNames ?? [];

        _gpuNamesQueried = true;
        _gpuNames = QueryGpuNames();
        Log.Info($"[AI Models] Detected GPU(s): {(_gpuNames.Length > 0 ? string.Join(", ", _gpuNames) : "none")}");
        return _gpuNames;
    }

    private static string[] QueryGpuNames()
    {
        if (OperatingSystem.IsWindows())
            return QueryGpuNamesWindows();

        if (OperatingSystem.IsLinux())
            return QueryGpuNamesLinux();

        if (OperatingSystem.IsMacOS())
            return ["Apple GPU (CoreML)"];

        return [];
    }

    private static string[] QueryGpuNamesWindows()
    {
        var names = new List<string>();

        // WMI gives the friendly names of every adapter; the same source the About dialog uses.
        // "Microsoft Basic Display Adapter" is excluded: it is the fallback driver, not a GPU.
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name) && name != "Microsoft Basic Display Adapter")
                    names.Add(name);
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[AI Models] WMI GPU query failed: {e.Message}");
        }

        // nvidia-smi as a backstop: its presence identifies an NVIDIA machine even when WMI returned
        // nothing usable, and the query names the exact model.
        if (!names.Any(n => n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)))
        {
            var nvidia = QueryNvidiaSmi();
            if (nvidia != null)
                names.Add(nvidia);
        }

        return names.Distinct().ToArray();
    }

    private static string[] QueryGpuNamesLinux()
    {
        var nvidia = QueryNvidiaSmi();
        return nvidia != null ? [nvidia] : [];
    }

    /** The first GPU name from nvidia-smi, or null when the tool is absent (no NVIDIA driver installed). */
    private static string? QueryNvidiaSmi()
    {
        try
        {
            var psi = new ProcessStartInfo
                          {
                              FileName = OperatingSystem.IsWindows()
                                             ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe")
                             : "nvidia-smi",
                              Arguments = "--query-gpu=name --format=csv,noheader",
                              UseShellExecute = false,
                              RedirectStandardOutput = true,
                              RedirectStandardError = true,
                              CreateNoWindow = true,
                          };
            using var process = Process.Start(psi);
            if (process == null)
                return null;

            // Timeout via the async read: a wedged driver must not block the UI thread for long
            var readTask = process.StandardOutput.ReadToEndAsync();
            if (!readTask.Wait(3000))
                return null;

            process.WaitForExit(3000);
            var name = readTask.Result.Split('\n').FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(name) ? null : $"NVIDIA {name}";
        }
        catch
        {
            return null;
        }
    }

    public static bool HasNvidiaGpu => GetGpuNames().Any(n => n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));

    /** True when an AMD or Intel GPU is listed, used to word the hints on the settings page. */
    public static bool HasAmdOrIntelGpu => GetGpuNames().Any(n =>
                                                                 n.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                                                                 || n.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                                                                 || n.Contains("Intel", StringComparison.OrdinalIgnoreCase)
                                                                 || n.Contains("Arc", StringComparison.OrdinalIgnoreCase));

    /**
     * The best engine for this machine: TensorRT when its runtime is present, CUDA when the stack is
     * usable, DirectML on Windows without CUDA, CoreML on macOS, CPU as the last resort. Every probe
     * is the cached one the operators share, so this is cheap enough to call every frame.
     */
    public static Recommendation GetRecommendation()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new Recommendation
                       {
                           Engine = Engine.CoreMl,
                           Title = "CoreML",
                           Summary = "Acceleration runs through CoreML - no setup is needed on macOS.",
                       };
        }

        string reason;
        var trtOk = OperatingSystem.IsWindows()
                        ? WindowsCudaRuntime.TryPrepareTensorRt(out reason, out _)
                        : LinuxCudaRuntime.TryPrepareTensorRt(out reason, out _);
        if (trtOk)
        {
            return new Recommendation
                       {
                           Engine = Engine.TensorRt,
                           Title = "TensorRT",
                           Summary = "TensorRT is the fastest engine for diffusion and depth - models run several times faster than on CUDA.",
                           Hint = "TensorRT engines are compiled per model on first use and cached, so the very first run of a model is slower.",
                       };
        }

        var cudaOk = OperatingSystem.IsWindows()
                         ? WindowsCudaRuntime.TryPrepare(out reason)
                         : LinuxCudaRuntime.TryPrepare(out reason);
        if (cudaOk)
        {
            return new Recommendation
                       {
                           Engine = Engine.Cuda,
                           Title = "CUDA",
                           Summary = "Models run GPU-accelerated through CUDA.",
                           Hint = HasNvidiaGpu
                                      ? "Install TensorRT 10 to make diffusion several times faster."
                                      : "A CUDA runtime was found, but no NVIDIA GPU is listed for this machine.",
                           ActionUrl = HasNvidiaGpu ? TensorRtDownloadUrl : null,
                       };
        }

        // DirectML is shipped but cannot run the current model graphs (their 5D reshapes fail on the
        // DML provider), so it is never recommended; without CUDA the honest engine is the CPU.
        return new Recommendation
                   {
                       Engine = Engine.Cpu,
                       Title = "CPU",
                       Summary = "No usable GPU acceleration was found - the models run on the CPU. "
                               + "The shipped DirectML runtime cannot run these model graphs.",
                       Hint = HasNvidiaGpu
                                  ? "The CUDA runtimes were not found. Installing the latest NVIDIA driver enables the much faster CUDA/TensorRT engines."
                                  : "GPU acceleration for the AI models needs an NVIDIA GPU with current drivers.",
                       ActionUrl = HasNvidiaGpu ? NvidiaDriverUrl : null,
                   };
    }
}

