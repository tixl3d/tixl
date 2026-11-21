#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using T3.Core.Logging;
using T3.Core.Settings;
using T3.Core.Utils;

namespace T3.Editor.Gui.AiModels;

internal enum AiCheckResult { Ok, Fixable, Missing, Info }

/** One row of the setup check shown under Settings > AI Models. */
internal sealed class AiSetupCheckItem
{
    /** Which part of the setup a row describes; the settings page groups them (engines live on the "Your computer" card). */
    public enum CheckKind { Engine, Model, Store }

    public required string Title;
    public AiCheckResult Result;
    public string Detail = "";

    /** Which part of the setup this row describes. */
    public CheckKind Kind = CheckKind.Model;

    /** Set when the one-click setup can resolve the item by downloading a model. */
    public ModelCatalogEntry? EntryToInstall;

    public string ResultText => Result switch
        {
            AiCheckResult.Ok => "OK",
            AiCheckResult.Fixable => "Setup available",
            AiCheckResult.Missing => "Missing",
            AiCheckResult.Info => "Optional",
            _ => "",
        };
}

/// <summary>
/// Checks everything the AI operators need to run: the engine runtimes (CUDA/TensorRT/DirectML),
/// a writable model store, and the models of each family. <see cref="FixAllAsync"/> then downloads
/// the fastest default models of every family that has nothing usable, so the operators work
/// without the user having to hunt for files.
/// </summary>
internal static class AiSetupChecker
{
    /**
     * The latest result, swapped as a whole: the one-click setup re-checks on a worker thread while the
     * settings page iterates the list every frame, so the published list is never modified.
     */
    public static IReadOnlyList<AiSetupCheckItem> LastResults => _lastResults;

    private static volatile IReadOnlyList<AiSetupCheckItem> _lastResults = [];

    public static DateTime LastCheckAt = DateTime.MinValue;

    /** While true a fix-up download is in progress; the settings page polls until it ends. */
    public static volatile bool IsFixing;

    public static string FixProgressText = "";

    private static readonly string[] V2RequiredFiles =
    [
        "dit_step.onnx", "dit_manifest.json", "text_encoder.onnx", "vae_decoder.onnx",
    ];

    public static List<AiSetupCheckItem> Run()
    {
        var items = new List<AiSetupCheckItem>();

        CheckEngineRuntimes(items);
        CheckModelStore(items);
        foreach (var family in AiModelCatalog.VisibleFamilies)
            CheckFamily(family, items);

        _lastResults = items;
        LastCheckAt = DateTime.Now;
        return items;
    }

    private static void CheckEngineRuntimes(List<AiSetupCheckItem> items)
    {
        if (OperatingSystem.IsMacOS())
        {
            items.Add(new AiSetupCheckItem
                          {
                              Title = "ML acceleration (CoreML)",
                              Kind = AiSetupCheckItem.CheckKind.Engine,
                              Result = AiCheckResult.Info,
                              Detail = "macOS uses CoreML through the CPU ONNX Runtime build; no CUDA setup is needed.",
                          });
            return;
        }

        // CUDA: the primary GPU path of the ONNX operators. Probing loads the runtime, exactly like the operators do.
        string cudaReason;
        var cudaOk = OperatingSystem.IsWindows()
                         ? WindowsCudaRuntime.TryPrepare(out cudaReason)
                         : LinuxCudaRuntime.TryPrepare(out cudaReason);
        items.Add(new AiSetupCheckItem
                      {
                          Title = "CUDA 12 runtime (cuDNN 9)",
                          Kind = AiSetupCheckItem.CheckKind.Engine,
                          Result = cudaOk ? AiCheckResult.Ok : AiCheckResult.Info,
                          Detail = cudaOk
                                       ? "GPU acceleration for ONNX models is available."
                                       : $"CUDA runtime not found. {cudaReason}",
                      });

        // TensorRT: optional but the fastest provider for diffusion
        string trtReason;
        string trtDirectory;
        var trtOk = OperatingSystem.IsWindows()
                        ? WindowsCudaRuntime.TryPrepareTensorRt(out trtReason, out trtDirectory)
                        : LinuxCudaRuntime.TryPrepareTensorRt(out trtReason, out trtDirectory);
        items.Add(new AiSetupCheckItem
                      {
                          Title = "TensorRT 10 runtime",
                          Kind = AiSetupCheckItem.CheckKind.Engine,
                          Result = trtOk ? AiCheckResult.Ok : AiCheckResult.Info,
                          Detail = trtOk
                                       ? $"Found in {trtDirectory}. TensorRT engines are compiled on first use and cached."
                                       : $"TensorRT not found ({trtReason}). Optional: speeds up diffusion several-fold once installed.",
                      });

        if (OperatingSystem.IsWindows())
        {
            var directMlAvailable = IsDirectMlShipped();
            items.Add(new AiSetupCheckItem
                          {
                              Title = "DirectML runtime",
                              Kind = AiSetupCheckItem.CheckKind.Engine,
                              Result = directMlAvailable ? AiCheckResult.Ok : AiCheckResult.Info,
                              Detail = directMlAvailable
                                           ? "DirectML build is shipped; an operator selecting DirectML needs an editor restart to switch builds."
                                           : "DirectML build is not shipped in this installation; CUDA is the GPU path.",
                          });
        }
    }

    private static bool IsDirectMlShipped()
    {
        var configured = AiModelSettings.Config.DirectMlDirectory;
        if (!string.IsNullOrEmpty(configured) && File.Exists(Path.Combine(configured, "DirectML.dll")))
            return true;

        var baseDir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(baseDir, "runtimes", "win-x64-directml", "native", "onnxruntime.dll"));
    }

    private static void CheckModelStore(List<AiSetupCheckItem> items)
    {
        var root = AiModelSettings.ModelsRoot;
        items.Add(new AiSetupCheckItem
                      {
                          Title = "Model store",
                          Kind = AiSetupCheckItem.CheckKind.Store,
                          Result = AiCheckResult.Ok,
                          Detail = root,
                      });
    }

    private static void CheckFamily(AiModelFamily family, List<AiSetupCheckItem> items)
    {
        var entries = AiModelCatalog.ForFamily(family).ToList();
        var downloadable = entries.Where(e => !e.RequiresLocalExport).ToList();
        var installed = downloadable.Where(e => e.IsInstalled()).ToList();
        var available = downloadable.Where(e => e.IsAvailable()).ToList();
        var defaultEntry = downloadable.FirstOrDefault(e => e.IsDefault) ?? downloadable.FirstOrDefault();

        switch (family)
        {
            case AiModelFamily.DepthAnything:
            {
                if (installed.Count > 0)
                {
                    items.Add(new AiSetupCheckItem
                                  {
                                      Title = "Depth models",
                                      Result = AiCheckResult.Ok,
                                      Detail = string.Join(", ", installed.Select(e => e.DisplayName)),
                                  });
                }
                else if (available.Count > 0 || HasAnyDepthModelInStore() || HasShippedDepthModel())
                {
                    items.Add(new AiSetupCheckItem
                                  {
                                      Title = "Depth models",
                                      Result = AiCheckResult.Fixable,
                                      Detail = "A depth model is already present, but not a store-managed one. " +
                                               "The default DA3 Small (about 96 MB) is installed by Download defaults.",
                                      EntryToInstall = defaultEntry,
                                  });
                }
                else
                {
                    items.Add(new AiSetupCheckItem
                                  {
                                      Title = "Depth models",
                                      Result = AiCheckResult.Fixable,
                                      Detail = "No depth model installed. [DepthAnything] shows an error until one is downloaded.",
                                      EntryToInstall = defaultEntry,
                                  });
                }

                break;
            }

            case AiModelFamily.MediaPipe:
            {
                var missing = downloadable.Where(e => !e.IsAvailable()).ToList();
                if (missing.Count == 0)
                {
                    items.Add(new AiSetupCheckItem
                                  {
                                      Title = "MediaPipe models",
                                      Result = AiCheckResult.Ok,
                                      Detail = "All tracking models are available (bundled or in the model store).",
                                  });
                }
                else
                {
                    items.Add(new AiSetupCheckItem
                                  {
                                      Title = "MediaPipe models",
                                      Result = AiCheckResult.Fixable,
                                      Detail = $"{missing.Count} model(s) missing: {string.Join(", ", missing.Select(e => e.DisplayName))}. " +
                                               "The affected operators report \"Model not found\" until they are installed.",
                                      EntryToInstall = missing.First(),
                                  });
                }

                break;
            }

            case AiModelFamily.StreamDiffusion:
            {
                var usableExport = FindUsableExport();
                if (usableExport != null)
                {
                    items.Add(new AiSetupCheckItem
                                  {
                                      Title = "Diffusion models",
                                      Result = AiCheckResult.Ok,
                                      Detail = $"Usable export: {Path.GetFileName(usableExport!.TrimEnd(Path.DirectorySeparatorChar))}",
                                  });
                }
                else
                {
                    items.Add(new AiSetupCheckItem
                                  {
                                      Title = "Diffusion models",
                                      Result = AiCheckResult.Fixable,
                                      Detail = "No usable ONNX export found. [StreamDiffusion] cannot start without one. " +
                                               "Download \"StreamDiffusion V2 - 256x256 (ready-to-run)\" in the model browser - no Python needed. " +
                                               "Advanced: download the checkpoint entry instead and run the export scripts in " +
                                               "Operators/StreamDiffusion/tools/streamdiffusionv2 against it.",
                                  });
                }

                break;
            }
        }
    }

    /** The one-click fix: download every fixable family's default model, then re-check. */
    public static async Task FixAllAsync()
    {
        if (IsFixing)
            return;

        IsFixing = true;
        FixProgressText = "Checking…";
        try
        {
            var pending = LastResults.Where(i => i.EntryToInstall != null
                                                 && i.Result is AiCheckResult.Fixable
                                                                   or AiCheckResult.Missing)
                                     .Select(i => i.EntryToInstall!)
                                     .ToList();

            // MediaPipe ships bundled with TiXL and never auto-downloads

            foreach (var entry in pending)
            {
                FixProgressText = $"Downloading {entry.DisplayName}…";
                Log.Info($"[AI Models] One-click setup: installing '{entry.DisplayName}'");
                await AiModelDownloadService.GetOrStartTask(entry);
            }

            FixProgressText = "Re-checking…";
            Run();
            FixProgressText = "";
            Log.Info("[AI Models] One-click setup finished");
        }
        finally
        {
            IsFixing = false;
        }
    }

    private static bool HasAnyDepthModelInStore()
    {
        var folder = AiModelSettings.FamilyDirectory(AiModelSettings.DepthAnythingFamilyFolder);
        if (!Directory.Exists(folder))
            return false;

        return Directory.EnumerateFiles(folder, "*.onnx").Any();
    }

    /** The DepthAnything package can ship a small DA3 model; the operators fall back to it. */
    private static bool HasShippedDepthModel()
    {
        foreach (var shippedName in new[] { "da3-small-fp16.onnx", "da3-small.onnx" })
        {
            if (ModelCatalogEntry.TryResolveShippedFile(AiModelFamily.DepthAnything, shippedName, out _))
                return true;
        }

        return false;
    }

    /** Any folder in the store that satisfies the V2 export file layout. */
    private static string? FindUsableExport()
    {
        var familyDirectory = AiModelSettings.FamilyDirectory(AiModelSettings.StreamDiffusionFamilyFolder);
        if (!Directory.Exists(familyDirectory))
            return null;

        foreach (var directory in Directory.GetDirectories(familyDirectory))
        {
            if (V2RequiredFiles.All(f => File.Exists(Path.Combine(directory, f))))
                return directory;
        }

        return null;
    }
}
