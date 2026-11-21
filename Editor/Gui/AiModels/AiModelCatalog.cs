#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace T3.Editor.Gui.AiModels;

/** The operator families the browser manages models for. */
internal enum AiModelFamily
{
    DepthAnything,
    StreamDiffusion,
    MediaPipe,
}

internal static class AiModelFamilyExtension
{
    public static string StoreFolder(this AiModelFamily family) => family switch
        {
            AiModelFamily.DepthAnything => T3.Core.Settings.AiModelSettings.DepthAnythingFamilyFolder,
            AiModelFamily.StreamDiffusion => T3.Core.Settings.AiModelSettings.StreamDiffusionFamilyFolder,
            AiModelFamily.MediaPipe => T3.Core.Settings.AiModelSettings.MediapipeFamilyFolder,
            _ => family.ToString(),
        };

    public static string Title(this AiModelFamily family) => family switch
        {
            AiModelFamily.DepthAnything => "Depth Anything",
            AiModelFamily.StreamDiffusion => "Stream Diffusion",
            AiModelFamily.MediaPipe => "MediaPipe",
            _ => family.ToString(),
        };

    public static string Description(this AiModelFamily family) => family switch
        {
            AiModelFamily.DepthAnything => "Monocular depth estimation ([DepthAnything] operators)",
            AiModelFamily.StreamDiffusion => "Text/image to image diffusion ([StreamDiffusion] operator)",
            AiModelFamily.MediaPipe => "Face, hand, pose tracking and segmentation ([Face…], [Hand…], [Pose…] operators)",
            _ => string.Empty,
        };

    /** The longer text the setup wizard shows when the family row is hovered. */
    public static string Details(this AiModelFamily family) => family switch
        {
            AiModelFamily.DepthAnything =>
                "Turns any image or live camera feed into a depth map, so you can displace geometry, build point clouds, " +
                "fake depth of field or key by distance. Usually one model is enough; pick a bigger one only if you need finer edges.",
            AiModelFamily.StreamDiffusion =>
                "Temporally stable realtime video-to-video diffusion (the StreamDiffusion V2 Wan2.1 model). The operator runs " +
                "an ONNX export you produce once with the bundled scripts; a checkpoint download in the browser feeds them.",
            AiModelFamily.MediaPipe =>
                "Google's lightweight trackers for faces, hands, bodies, gestures, objects and person segmentation. " +
                "Small files that run on the CPU, most of them already ship with TiXL.",
            _ => string.Empty,
        };
}

/// <summary>One file of a catalog entry, downloaded to a fixed name inside the family's store folder.</summary>
/// <param name="ExpectedSizeBytes">
/// Size of the file at the source. Used for progress and the disk-space estimate; the download itself is
/// verified against the server's Content-Length, so a slightly outdated value here never fails an install.
/// </param>
internal sealed record CatalogFile(string Url, string TargetFileName, long ExpectedSizeBytes);

/// <summary>
/// A model the model browser can install (or an export that has to be produced locally). Downloaded
/// files land in the model store, keeping multi-gigabyte weights out of the git repository.
/// </summary>
internal sealed class ModelCatalogEntry
{
    public required string Id { get; init; }
    public required AiModelFamily Family { get; init; }
    public required string DisplayName { get; init; }

    /** One line, shown under the name in the model browser. */
    public string Description { get; init; } = "";

    /** A few sentences for the setup wizard's description pane: what it is good for and when to pick it. */
    public string Details { get; init; } = "";

    /** The operators that use this model, e.g. "[DepthAnything]". */
    public string UsedBy { get; init; } = "";

    /** Relative ratings 1..5 within the family, shown as bars in the setup wizard. 0 hides the rating. */
    public int Speed { get; init; }
    public int Quality { get; init; }

    /** Rough hardware hint, e.g. "Any GPU" or "NVIDIA, 8 GB VRAM". */
    public string Hardware { get; init; } = "";

    /** Empty for entries that are only listed (installed with local export tooling). */
    public IReadOnlyList<CatalogFile> Files { get; init; } = [];

    /// <summary>0 for entries that are only listed (installed with the export tooling, not downloadable).</summary>
    public long TotalSizeBytes { get; init; }

    /** Installed as the family's default by the one-click setup. */
    public bool IsDefault { get; init; }

    /** Part of the "Recommended" setup type of the wizard. */
    public bool IsRecommended { get; init; }

    /** Part of the "Minimal" setup type of the wizard: the smallest set that makes each light family work. */
    public bool IsMinimal { get; init; }

    /** Not a download: the row shows how the export is produced locally. */
    public bool RequiresLocalExport { get; init; }

    /** When true, presence is checked against the packaged assets too (MediaPipe models ship with TiXL). */
    public bool ShippedWithTiXl { get; init; }

    public bool IsDownloadable => !RequiresLocalExport && Files.Count > 0;

    /**
     * Subfolder of the family directory the files install into. Families of single model files
     * (DepthAnything, MediaPipe) use none; StreamDiffusion installs one folder per export, which is
     * what the operator points its EnginePath at.
     */
    public string InstallSubFolder => Family == AiModelFamily.StreamDiffusion ? Id : string.Empty;

    public string TargetPath(string fileName)
    {
        var subFolder = InstallSubFolder;
        return string.IsNullOrEmpty(subFolder)
                   ? Path.Combine(T3.Core.Settings.AiModelSettings.FamilyDirectory(Family.StoreFolder()), fileName)
                   : Path.Combine(T3.Core.Settings.AiModelSettings.FamilyDirectory(Family.StoreFolder()), subFolder, fileName);
    }

    /** A local-export entry lives in its own folder under the family directory. */
    public string ExportFolder => Path.Combine(T3.Core.Settings.AiModelSettings.FamilyDirectory(Family.StoreFolder()), Id);

    /** The folder the entry's files live in (family folder, or one subfolder per export). */
    public string StoreDirectory
        => RequiresLocalExport || Files.Count == 0 ? ExportFolder : Path.GetDirectoryName(TargetPath(Files[0].TargetFileName)) ?? ExportFolder;

    public bool IsInstalled()
    {
        if (RequiresLocalExport)
            return Directory.Exists(ExportFolder);

        foreach (var file in Files)
        {
            if (!File.Exists(TargetPath(file.TargetFileName)))
                return false;
        }

        return true;
    }

    /** True when the files are found anywhere (store, package assets). Only used for shipped models. */
    public bool IsAvailable()
    {
        if (IsInstalled())
            return true;

        if (!ShippedWithTiXl || Files.Count == 0)
            return false;

        return TryResolveShippedFile(Family, Files[0].TargetFileName, out _);
    }

    /** Bytes still to download: files already in the store don't count. */
    public long RemainingDownloadBytes()
    {
        var remaining = 0L;
        foreach (var file in Files)
        {
            if (!File.Exists(TargetPath(file.TargetFileName)))
                remaining += file.ExpectedSizeBytes;
        }

        return remaining;
    }

    /** Checks the packaged assets through the asset registry, as the operators do. */
    public static bool TryResolveShippedFile(AiModelFamily family, string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        try
        {
            if (T3.Core.Resource.Assets.AssetRegistry.TryResolveAddress($"{family.StoreFolder()}:{fileName}", null, out var resolved, out _, logWarnings: false))
            {
                fullPath = resolved;
                return true;
            }
        }
        catch
        {
            // The registry may not be populated yet; absence then simply reads as "not available"
        }

        return false;
    }
}

/// <summary>
/// The models known to the settings window's browser and the setup wizard. URLs and sizes are checked
/// against the HuggingFace file listings and Google's MediaPipe model bucket; every entry must be
/// reachable without authentication.
/// </summary>
internal static class AiModelCatalog
{
    private const string HuggingFace = "https://huggingface.co";
    private const string MediaPipeModels = "https://storage.googleapis.com/mediapipe-models";
    private const string MediaPipeAssets = "https://storage.googleapis.com/mediapipe-assets";

    private const long KB = 1024;
    private const long MB = 1024 * 1024;

    public static readonly IReadOnlyList<ModelCatalogEntry> Entries =
    [
        // ---------------------------------------------------------------- DepthAnything
        new()
        {
            Id = "da3-small",
            Family = AiModelFamily.DepthAnything,
            DisplayName = "Depth Anything 3 – Small",
            Description = "Fastest DA3 model. Recommended default for interactive depth.",
            Details = "The best starting point. Runs at full frame rate on almost any GPU and gives clean, stable depth " +
                      "for live camera input, displacement and point clouds.",
            UsedBy = "[DepthAnything]",
            Speed = 5, Quality = 3,
            Hardware = "Any GPU, about 1 GB VRAM",
            TotalSizeBytes = 100_704_575,
            IsDefault = true,
            IsRecommended = true,
            Files =
            [
                new($"{HuggingFace}/zerochocobo/DepthAnything3_ONNX/resolve/main/da3_small.onnx", "da3-small.onnx", 100_704_575),
            ],
        },
        new()
        {
            Id = "da3-small-int8",
            Family = AiModelFamily.DepthAnything,
            DisplayName = "Depth Anything 3 – Small (int8)",
            Description = "Quantized small model; lower quality, a third of the download.",
            Details = "Compressed version of the Small model. Choose it for laptops, slow connections or CPU-only machines; " +
                      "edges are a little softer and flatter areas can shimmer.",
            UsedBy = "[DepthAnything]",
            Speed = 5, Quality = 2,
            Hardware = "Any GPU or CPU",
            TotalSizeBytes = 37_027_601,
            IsMinimal = true,
            Files =
            [
                new($"{HuggingFace}/ohaiibuzzle/DA3-ONNX/resolve/main/da3_s_504x378_int8.onnx", "da3-small-int8.onnx", 37_027_601),
            ],
        },
        new()
        {
            Id = "da3-base-fp16",
            Family = AiModelFamily.DepthAnything,
            DisplayName = "Depth Anything 3 – Base (fp16)",
            Description = "More detail than Small, still realtime on most GPUs.",
            Details = "Noticeably sharper object boundaries and better separation of background layers than Small, at " +
                      "roughly half its frame rate. A good upgrade for recorded footage or high-resolution output.",
            UsedBy = "[DepthAnything]",
            Speed = 4, Quality = 4,
            Hardware = "Mid-range GPU, about 2 GB VRAM",
            TotalSizeBytes = 207_235_996,
            Files =
            [
                new($"{HuggingFace}/Heliosoph/da3-base-onnx/resolve/main/model_fp16.onnx", "da3-base-fp16.onnx", 207_235_996),
            ],
        },
        new()
        {
            Id = "da3-mono-large-int8",
            Family = AiModelFamily.DepthAnything,
            DisplayName = "Depth Anything 3 – Mono Large (int8)",
            Description = "Large single-image model, quantized.",
            Details = "The large model tuned for single images, compressed to int8. Very fine detail on hair, foliage and " +
                      "thin structures; better suited to stills and offline renders than live input.",
            UsedBy = "[DepthAnything]",
            Speed = 2, Quality = 4,
            Hardware = "Fast GPU, about 3 GB VRAM",
            TotalSizeBytes = 431_206_981,
            Files =
            [
                new($"{HuggingFace}/ohaiibuzzle/DA3-ONNX/resolve/main/da3mono_l_504x378_int8.onnx", "da3-mono-large-int8.onnx", 431_206_981),
            ],
        },
        new()
        {
            Id = "da3-metric-large-fp16",
            Family = AiModelFamily.DepthAnything,
            DisplayName = "Depth Anything 3 – Metric Large (fp16)",
            Description = "Metric depth in meters; largest and slowest DA3 model.",
            Details = "Outputs real distances in meters instead of relative depth, so values stay comparable between frames " +
                      "and scenes. Pick it for measuring, room-scale projection mapping or matching a 3D scene's scale.",
            UsedBy = "[DepthAnything]",
            Speed = 1, Quality = 5,
            Hardware = "High-end GPU, about 4 GB VRAM",
            TotalSizeBytes = 669_065_321,
            Files =
            [
                new($"{HuggingFace}/Heliosoph/da3metric-large-onnx/resolve/main/model_fp16.onnx", "da3-metric-large-fp16.onnx", 669_065_321),
            ],
        },
        new()
        {
            Id = "da3-large",
            Family = AiModelFamily.DepthAnything,
            DisplayName = "Depth Anything 3 – Large",
            Description = "The 0.35B any-view model at 1036×1036; sharpest relative depth.",
            Details = "The full-size DA3 Large encoder. Fine detail on hair, foliage and thin structures; better suited to " +
                      "stills and offline renders than live input because of the large fixed input size.",
            UsedBy = "[DepthAnything]",
            Speed = 1, Quality = 5,
            Hardware = "Fast GPU, about 4 GB VRAM",
            TotalSizeBytes = 1_349_415_225,
            Files =
            [
                new($"{HuggingFace}/zerochocobo/DepthAnything3_ONNX/resolve/main/da3_large_1036.onnx", "da3-large.onnx", 1_349_415_225),
            ],
        },
        new()
        {
            Id = "da3-giant-1.1",
            Family = AiModelFamily.DepthAnything,
            DisplayName = "Depth Anything 3 – Giant 1.1 (own export)",
            Description = "The 1.15B any-view model; export locally with the bundled script.",
            Details = "The largest Apache-quality any-view checkpoint line. No public ONNX export exists; run " +
                      "Operators/DepthAnything/Scripts/export_da3_onnx.py once against the official weights " +
                      "(depth-anything/DA3-GIANT-1.1) and the operator finds da3-giant-fp16.onnx in the model store.",
            UsedBy = "[DepthAnything]",
            Speed = 1, Quality = 5,
            Hardware = "High-end GPU, 8 GB VRAM or more",
            RequiresLocalExport = true,
        },
        new()
        {
            Id = "da3-nested-giant-1.1",
            Family = AiModelFamily.DepthAnything,
            DisplayName = "Depth Anything 3 – Nested Giant 1.1 (own export)",
            Description = "The 1.40B nested model with metric depth and sky segmentation; export locally.",
            Details = "The full nested checkpoint (relative depth, metric depth, sky segmentation). No public ONNX export exists; run " +
                      "Operators/DepthAnything/Scripts/export_da3_onnx.py once against the official weights " +
                      "(depth-anything/DA3NESTED-GIANT-LARGE-1.1) and the operator finds da3-nested-giant-fp16.onnx in the model store. " +
                      "The nested model's metric-depth and sky outputs are exported when the script runs in metric mode.",
            UsedBy = "[DepthAnything]",
            Speed = 1, Quality = 5,
            Hardware = "High-end GPU, 8 GB VRAM or more",
            RequiresLocalExport = true,
        },
        new()
        {
            Id = "da3-mono-large-fp32",
            Family = AiModelFamily.DepthAnything,
            DisplayName = "Depth Anything 3 – Mono Large (fp32)",
            Description = "Large single-image model, full precision.",
            Details = "The large model tuned for single images at full precision - the int8 variant trades a little quality " +
                      "for three quarters less download and faster inference.",
            UsedBy = "[DepthAnything]",
            Speed = 1, Quality = 5,
            Hardware = "Fast GPU, about 4 GB VRAM",
            TotalSizeBytes = 1_336_957_133,
            Files =
            [
                new($"{HuggingFace}/ohaiibuzzle/DA3-ONNX/resolve/main/da3mono_l_504x378.onnx", "da3-mono-large-fp32.onnx", 1_336_957_133),
            ],
        },

        // ---------------------------------------------------------------- StreamDiffusion
        new()
        {
            Id = "streamdiffusion-v2-256",
            Family = AiModelFamily.StreamDiffusion,
            DisplayName = "StreamDiffusion V2 – 256×256 (ready-to-run)",
            Description = "Pre-exported ONNX of the causal Wan2.1 video model at 256×256 fp16 - download and stream, no Python needed.",
            Details = "The complete export: DiT, text encoder, TAEHV VAE and tokenizer, produced once with the bundled " +
                      "scripts so you don't have to. Download it and leave the operator's EnginePath empty. The realtime " +
                      "size: about 8 fps on TensorRT, 4 fps on CUDA for a 16 GB GPU. For other resolutions use the " +
                      "checkpoint download below with the export scripts.",
            UsedBy = "[StreamDiffusion]",
            Speed = 4, Quality = 4,
            Hardware = "NVIDIA, 8 GB VRAM or more",
            IsRecommended = true,
            TotalSizeBytes = 617_503 + 300_744_704 + 186 + 4_133_427 + 2_537_750_528 + 117 + 1_550_935 + 11_364_335_616 + 16_837_417 + 39_562_590 + 6_057_351,
            Files =
            [
                new($"{HuggingFace}/banidu-512/streamdiffusion-v2-onnx/resolve/main/cross_kv.onnx", "cross_kv.onnx", 617_503),
                new($"{HuggingFace}/banidu-512/streamdiffusion-v2-onnx/resolve/main/cross_kv.onnx.data", "cross_kv.onnx.data", 300_744_704),
                new($"{HuggingFace}/banidu-512/streamdiffusion-v2-onnx/resolve/main/dit_manifest.json", "dit_manifest.json", 186),
                new($"{HuggingFace}/banidu-512/streamdiffusion-v2-onnx/resolve/main/dit_step.onnx", "dit_step.onnx", 4_133_427),
                new($"{HuggingFace}/banidu-512/streamdiffusion-v2-onnx/resolve/main/dit_step.onnx.data", "dit_step.onnx.data", 2_537_750_528),
                new($"{HuggingFace}/banidu-512/streamdiffusion-v2-onnx/resolve/main/kv_zeros.onnx", "kv_zeros.onnx", 117),
                new($"{HuggingFace}/banidu-512/streamdiffusion-v2-onnx/resolve/main/text_encoder.onnx", "text_encoder.onnx", 1_550_935),
                new($"{HuggingFace}/banidu-512/streamdiffusion-v2-onnx/resolve/main/text_encoder.onnx.data", "text_encoder.onnx.data", 11_364_335_616),
                new($"{HuggingFace}/banidu-512/streamdiffusion-v2-onnx/resolve/main/tokenizer.json", "tokenizer.json", 16_837_417),
                new($"{HuggingFace}/banidu-512/streamdiffusion-v2-onnx/resolve/main/vae_decoder.onnx", "vae_decoder.onnx", 39_562_590),
                new($"{HuggingFace}/banidu-512/streamdiffusion-v2-onnx/resolve/main/vae_encoder.onnx", "vae_encoder.onnx", 6_057_351),
            ],
        },
        new()
        {
            Id = "streamdiffusion-v2",
            Family = AiModelFamily.StreamDiffusion,
            DisplayName = "StreamDiffusion V2 (own export)",
            Description = "Causal Wan2.1 1.3B video model - the latest and best variant. Produces its export with the scripts in Operators/StreamDiffusion/tools/streamdiffusionv2.",
            Details = "Temporally stable video-to-video diffusion: far less flicker than the image models. It can't be downloaded " +
                      "ready-made; run the export scripts in Operators/StreamDiffusion/tools/streamdiffusionv2 once, and TiXL finds the result in the model store.",
            UsedBy = "[StreamDiffusion]",
            Speed = 2, Quality = 5,
            Hardware = "NVIDIA, 12 GB VRAM or more",
            RequiresLocalExport = true,
        },
        new()
        {
            Id = "streamdiffusion-v2-checkpoint",
            Family = AiModelFamily.StreamDiffusion,
            DisplayName = "StreamDiffusion V2 checkpoint (for export)",
            Description = "The 1.3B causal DMD checkpoint plus the T5 text encoder and tokenizer the export scripts consume.",
            Details = "Everything the export scripts in Operators/StreamDiffusion/tools/streamdiffusionv2 need except the TAEHV " +
                      "autoencoder (taew2_1.pth, from the taehv GitHub project). Download this, then run: export-dit.py --ckpt model.pt, " +
                      "export-text-encoder.py --t5 models_t5_umt5-xxl-enc-bf16.pth, export-vae.py --taehv taew2_1.pth. " +
                      "A 14B checkpoint also exists upstream, but the export tooling covers the 1.3B model.",
            UsedBy = "[StreamDiffusion]",
            Speed = 2, Quality = 5,
            Hardware = "NVIDIA, 12 GB VRAM or more",
            TotalSizeBytes = 5_676_282_858L + 11_361_920_418L + 16_837_417,
            Files =
            [
                new($"{HuggingFace}/jerryfeng/StreamDiffusionV2/resolve/main/wan_causal_dmd_v2v/model.pt", "model.pt", 5_676_282_858),
                new($"{HuggingFace}/Wan-AI/Wan2.1-T2V-1.3B/resolve/main/models_t5_umt5-xxl-enc-bf16.pth", "models_t5_umt5-xxl-enc-bf16.pth", 11_361_920_418),
                new($"{HuggingFace}/Wan-AI/Wan2.1-T2V-1.3B/resolve/main/google/umt5-xxl/tokenizer.json", "tokenizer.json", 16_837_417),
            ],
        },

        // ---------------------------------------------------------------- MediaPipe
        new()
        {
            Id = "face-landmarker",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Face Landmarker",
            Description = "Used by [FaceLandmarkDetection].",
            Details = "Tracks 478 points on each face plus blendshapes (smile, blink, jaw open…). Use it to drive masks, " +
                      "face filters or animation from facial expressions.",
            UsedBy = "[FaceLandmarkDetection]",
            Speed = 4, Quality = 4,
            Hardware = "CPU",
            TotalSizeBytes = 4 * MB,
            ShippedWithTiXl = true,
            IsRecommended = true,
            IsMinimal = true,
            Files =
            [
                new($"{MediaPipeModels}/face_landmarker/face_landmarker/float16/1/face_landmarker.task", "face_landmarker.task", 4 * MB),
            ],
        },
        new()
        {
            Id = "hand-landmarker",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Hand Landmarker",
            Description = "Used by [HandLandmarkDetection] and [GestureRecognition].",
            Details = "Finds up to several hands and tracks 21 joints on each, in 2D and 3D. Good for pinch, point and " +
                      "air-drawing interactions.",
            UsedBy = "[HandLandmarkDetection], [GestureRecognition]",
            Speed = 4, Quality = 4,
            Hardware = "CPU",
            TotalSizeBytes = 8 * MB,
            ShippedWithTiXl = true,
            IsRecommended = true,
            IsMinimal = true,
            Files =
            [
                new($"{MediaPipeModels}/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task", "hand_landmarker.task", 8 * MB),
            ],
        },
        new()
        {
            Id = "pose-landmarker",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Pose Landmarker",
            Description = "Used by [PoseLandmarkDetection].",
            Details = "Full-body tracking with 33 joints. Drive skeletons, particles or effects from a dancer's or " +
                      "performer's movement.",
            UsedBy = "[PoseLandmarkDetection]",
            Speed = 3, Quality = 4,
            Hardware = "CPU",
            TotalSizeBytes = 9 * MB,
            ShippedWithTiXl = true,
            IsRecommended = true,
            IsMinimal = true,
            Files =
            [
                new($"{MediaPipeModels}/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task", "pose_landmarker.task", 9 * MB),
            ],
        },
        new()
        {
            Id = "gesture-recognizer",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Gesture Recognizer",
            Description = "Used by [GestureRecognition].",
            Details = "Recognizes common hand gestures (open palm, fist, thumbs up, victory, pointing…) so you can trigger " +
                      "cues without touching a controller.",
            UsedBy = "[GestureRecognition]",
            Speed = 4, Quality = 3,
            Hardware = "CPU",
            TotalSizeBytes = 8 * MB,
            ShippedWithTiXl = true,
            IsRecommended = true,
            Files =
            [
                new($"{MediaPipeModels}/gesture_recognizer/gesture_recognizer/float16/1/gesture_recognizer.task", "gesture_recognizer.task", 8 * MB),
            ],
        },
        new()
        {
            Id = "face-detection-short",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Face Detector (short range)",
            Description = "Used by [FaceDetection].",
            Details = "Very fast face boxes for faces close to the camera (within about 2 m), e.g. a webcam at a desk or an installation kiosk.",
            UsedBy = "[FaceDetection]",
            Speed = 5, Quality = 3,
            Hardware = "CPU",
            TotalSizeBytes = 230 * KB,
            ShippedWithTiXl = true,
            IsRecommended = true,
            Files =
            [
                new($"{MediaPipeAssets}/face_detection_short_range.tflite", "blaze_face_short_range.tflite", 230 * KB),
            ],
        },
        new()
        {
            Id = "face-detection-full",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Face Detector (full range)",
            Description = "Full-range face detection; works at larger distances than the short-range model.",
            Details = "Finds smaller faces further from the camera (up to about 5 m), for stages and rooms.",
            UsedBy = "[FaceDetection]",
            Speed = 4, Quality = 4,
            Hardware = "CPU",
            TotalSizeBytes = 1084 * KB,
            ShippedWithTiXl = true,
            Files =
            [
                new($"{MediaPipeAssets}/face_detection_full_range.tflite", "face_detection_full_range.tflite", 1084 * KB),
            ],
        },
        new()
        {
            Id = "selfie-segmenter",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Selfie Segmenter",
            Description = "Used by [ImageSegmentation].",
            Details = "Cuts people out of the background in realtime: a green screen without the green screen.",
            UsedBy = "[ImageSegmentation]",
            Speed = 5, Quality = 3,
            Hardware = "CPU",
            TotalSizeBytes = 256 * KB,
            ShippedWithTiXl = true,
            IsRecommended = true,
            IsMinimal = true,
            Files =
            [
                new($"{MediaPipeModels}/image_segmenter/selfie_segmenter/float16/latest/selfie_segmenter.tflite", "selfie_segmenter.tflite", 256 * KB),
            ],
        },
        new()
        {
            Id = "selfie-segmenter-landscape",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Selfie Segmenter (landscape)",
            Description = "Used by [ImageSegmentation] in landscape mode.",
            Details = "The same person cut-out, tuned for 16:9 camera input. Slightly cleaner edges on wide shots.",
            UsedBy = "[ImageSegmentation]",
            Speed = 5, Quality = 3,
            Hardware = "CPU",
            TotalSizeBytes = 256 * KB,
            ShippedWithTiXl = true,
            Files =
            [
                new($"{MediaPipeModels}/image_segmenter/selfie_segmenter_landscape/float16/latest/selfie_segmenter_landscape.tflite", "selfie_segmenter_landscape.tflite", 256 * KB),
            ],
        },
        new()
        {
            Id = "selfie-multiclass",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Selfie Multiclass Segmenter",
            Description = "Used by [ImageSegmentation]; segments hair, skin, clothes and background.",
            Details = "Separates hair, face skin, body skin, clothes and accessories into their own masks, so each can be " +
                      "recolored or affected separately.",
            UsedBy = "[ImageSegmentation]",
            Speed = 3, Quality = 4,
            Hardware = "CPU",
            TotalSizeBytes = 16 * MB,
            ShippedWithTiXl = true,
            Files =
            [
                new($"{MediaPipeModels}/image_segmenter/selfie_multiclass_256x256/float32/latest/selfie_multiclass_256x256.tflite", "selfie_multiclass_256x256.tflite", 16 * MB),
            ],
        },
        new()
        {
            Id = "efficientdet-lite0",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Object Detector (EfficientDet Lite0)",
            Description = "Used by [ObjectDetection].",
            Details = "Detects 80 everyday object classes (people, cars, cups, phones…) with bounding boxes. The fast default.",
            UsedBy = "[ObjectDetection]",
            Speed = 4, Quality = 3,
            Hardware = "CPU",
            TotalSizeBytes = 8 * MB,
            ShippedWithTiXl = true,
            IsRecommended = true,
            Files =
            [
                new($"{MediaPipeModels}/object_detector/efficientdet_lite0/float32/latest/efficientdet_lite0.tflite", "efficientdet_lite0.tflite", 8 * MB),
            ],
        },
        new()
        {
            Id = "efficientdet-lite2",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Object Detector (EfficientDet Lite2)",
            Description = "Used by [ObjectDetection]; more accuracy than Lite0, slower.",
            Details = "Finds smaller and partly hidden objects more reliably than Lite0, at about half its speed.",
            UsedBy = "[ObjectDetection]",
            Speed = 2, Quality = 4,
            Hardware = "CPU",
            TotalSizeBytes = 8 * MB,
            ShippedWithTiXl = true,
            Files =
            [
                new($"{MediaPipeModels}/object_detector/efficientdet_lite2/float32/latest/efficientdet_lite2.tflite", "efficientdet_lite2.tflite", 8 * MB),
            ],
        },
        new()
        {
            Id = "ssd-mobilenet-v2",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "Object Detector (SSD MobileNet v2)",
            Description = "Alternative object detection model for [ObjectDetection].",
            Details = "An older, very light detector. Useful on slow machines when Lite0 is still too heavy.",
            UsedBy = "[ObjectDetection]",
            Speed = 5, Quality = 2,
            Hardware = "CPU",
            TotalSizeBytes = 7 * MB,
            ShippedWithTiXl = true,
            Files =
            [
                new($"{MediaPipeModels}/object_detector/ssd_mobilenet_v2/float32/latest/ssd_mobilenet_v2.tflite", "ssd_mobilenet_v2.tflite", 7 * MB),
            ],
        },
        new()
        {
            Id = "deeplabv3",
            Family = AiModelFamily.MediaPipe,
            DisplayName = "DeepLab v3 Segmenter",
            Description = "Used by [ImageSegmentation]; general-purpose segmentation.",
            Details = "Segments 21 scene classes (person, car, animal, furniture…) instead of only people.",
            UsedBy = "[ImageSegmentation]",
            Speed = 3, Quality = 3,
            Hardware = "CPU",
            TotalSizeBytes = 3 * MB,
            ShippedWithTiXl = true,
            Files =
            [
                new($"{MediaPipeAssets}/deeplabv3.tflite", "deeplabv3.tflite", 3 * MB),
            ],
        },
    ];

    public static readonly AiModelFamily[] Families = Enum.GetValues<AiModelFamily>();

    /**
     * The families the settings page and the setup wizard manage. The MediaPipe models ship bundled
     * with TiXL and need no setup, so they stay out of the model browser, the setup check and the wizard.
     */
    public static readonly AiModelFamily[] VisibleFamilies = [AiModelFamily.DepthAnything, AiModelFamily.StreamDiffusion];

    /** Per-family index built once: the settings panel draws it every frame, so it must not iterate. */
    private static readonly Dictionary<AiModelFamily, IReadOnlyList<ModelCatalogEntry>> _entriesByFamily = BuildFamilyIndex();

    private static Dictionary<AiModelFamily, IReadOnlyList<ModelCatalogEntry>> BuildFamilyIndex()
    {
        var index = new Dictionary<AiModelFamily, IReadOnlyList<ModelCatalogEntry>>();
        foreach (var family in Families)
        {
            var list = new List<ModelCatalogEntry>();
            foreach (var entry in Entries)
            {
                if (entry.Family == family)
                    list.Add(entry);
            }
            index[family] = list;
        }
        return index;
    }

    public static IReadOnlyList<ModelCatalogEntry> ForFamily(AiModelFamily family)
        => _entriesByFamily.TryGetValue(family, out var list) ? list : [];

    /** The entries the one-click setup downloads when missing: the fastest working model per family. */
    public static IEnumerable<ModelCatalogEntry> Defaults
    {
        get
        {
            foreach (var entry in Entries)
            {
                if (entry.IsDefault)
                    yield return entry;
            }
        }
    }

    public static string FormatSize(long bytes) => bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (double)(1024 * 1024 * 1024):0.#} GB",
            >= 1024 * 1024 => $"{Math.Round(bytes / (double)(1024 * 1024))} MB",
            >= 1024 => $"{Math.Round(bytes / 1024.0)} KB",
            _ => $"{bytes} B",
        };
}
