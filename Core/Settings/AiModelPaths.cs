#nullable enable
using System;
using System.IO;
using T3.Core.Utils;

namespace T3.Core.Settings;

/// <summary>
/// Where the AI model store lives and which engine runtime folders to prefer, persisted in
/// <c>aiModels.json</c> next to the other settings files.
/// <para>The store holds the models downloaded through the settings window's model browser
/// (DepthAnything ONNX, MediaPipe task/tflite files, StreamDiffusion export folders), so they can
/// live outside the TiXL installation and the git repository. Operator packages resolve their model
/// files here before falling back to their packaged assets, and the CUDA/TensorRT runtime probes
/// check the configured engine folders first.</para>
/// </summary>
public sealed class AiModelSettings : T3.Core.IO.Settings<AiModelSettings.ConfigData>
{
    public static readonly AiModelSettings Instance = new();

    private AiModelSettings() : base("aiModels.json", saveOnQuit: true) { }

    public sealed class ConfigData
    {
        /// <summary>Root of the model store. Empty uses the platform default in %LOCALAPPDATA% (or the XDG data folder).</summary>
        public string ModelsDirectory = "";

        /// <summary>Folder holding the CUDA 12 runtime and cuDNN 9 DLLs. Probed before every standard location.</summary>
        public string CudaRuntimeDirectory = "";

        /// <summary>Folder holding the TensorRT 10 runtime (nvinfer_10.dll). Probed before TENSORRT_ROOT and the standard installs.</summary>
        public string TensorRtDirectory = "";

        /// <summary>Folder holding DirectML.dll. Preferred over the copy shipped with the DirectML build, once selected.</summary>
        public string DirectMlDirectory = "";

        /**
         * Legacy DirectML preference of the pre-V2 packages. The current StreamDiffusion V2 graphs and
         * DepthAnything cannot run through the DirectML provider (the DiT's 5D reshapes fail on it), so
         * nothing reads this anymore; it is kept so existing aiModels.json files keep loading.
         */
        public bool PreferDirectMlForOnnx = false;

        /// <summary>
        /// Set once the AI model setup wizard opened on its own (first start without models). Afterwards it is
        /// only reached through Help › AI Model Setup… and Settings › AI Models.
        /// </summary>
        public bool SetupWizardOffered = false;
    }

    /// <summary>Subfolders of the model store, one per operator package.</summary>
    public const string DepthAnythingFamilyFolder = "DepthAnything";
    public const string StreamDiffusionFamilyFolder = "StreamDiffusion";
    public const string MediapipeFamilyFolder = "Mediapipe";

    private static readonly string DefaultModelsRoot = ComputeDefaultModelsRoot();

    /// <summary>The configured model store root, or the platform default when nothing is configured.</summary>
    public static string ModelsRoot
    {
        get
        {
            var configured = Config.ModelsDirectory?.Trim();
            return string.IsNullOrEmpty(configured) ? DefaultModelsRoot : configured!;
        }
    }

    public static string FamilyDirectory(string familyFolder) => Path.Combine(ModelsRoot, familyFolder);

    /// <summary>True when <paramref name="fileName"/> exists in the family's store folder.</summary>
    public static bool TryGetStoredFile(string familyFolder, string fileName, out string fullPath)
    {
        fullPath = Path.Combine(FamilyDirectory(familyFolder), fileName);
        return File.Exists(fullPath);
    }

    /// <summary>Creates the store root and the given family folder if needed, returning false when the path is not usable.</summary>
    public static bool EnsureFamilyDirectory(string familyFolder, out string familyDirectory, out string? error)
    {
        familyDirectory = FamilyDirectory(familyFolder);
        try
        {
            Directory.CreateDirectory(familyDirectory);
            error = null;
            return true;
        }
        catch (Exception e)
        {
            error = $"Could not create the model folder {familyDirectory}: {e.Message}";
            return false;
        }
    }

    /// <summary>Wipes the cached CUDA/TensorRT probe results, so the next use re-detects with the current settings.</summary>
    public static void ResetRuntimeProbes()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsCudaRuntime.ResetProbes();
        }
        else if (OperatingSystem.IsLinux())
        {
            LinuxCudaRuntime.ResetProbes();
        }
    }

    /** True when the DirectML ONNX Runtime build is shipped with this installation or a DirectML.dll folder is configured. */
    public static bool IsDirectMlBuildShipped()
    {
        var configured = Config.DirectMlDirectory;
        if (!string.IsNullOrEmpty(configured) && File.Exists(Path.Combine(configured, "DirectML.dll")))
            return true;

        var baseDir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(baseDir, "runtimes", "win-x64-directml", "native", "onnxruntime.dll"));
    }

    /**
     * The store must survive version upgrades and is far too large for a cache directory,
     * so it sits next to the per-version cache folder under one unversioned TiXL folder.
     */
    private static string ComputeDefaultModelsRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                                                         Environment.SpecialFolderOption.DoNotVerify);
            if (Path.IsPathRooted(localAppData))
                return Path.Combine(localAppData, "TiXL", "Models");
        }
        else if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
                                                         Environment.SpecialFolderOption.DoNotVerify),
                                "Library", "Application Support", "TiXL", "Models");
        }
        else
        {
            // The XDG base directory spec: $XDG_DATA_HOME, or ~/.local/share when it is unset or not absolute.
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome) || !Path.IsPathRooted(dataHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
                                                     Environment.SpecialFolderOption.DoNotVerify);
                dataHome = Path.Combine(home, ".local", "share");
            }

            return Path.Combine(dataHome, "TiXL", "Models");
        }

        // Windows without a resolvable profile: fall back next to the per-version cache folder
        var cacheParent = Path.GetDirectoryName(FileLocations.CacheDirectory) ?? FileLocations.CacheDirectory;
        return Path.Combine(cacheParent, "Models");
    }
}
