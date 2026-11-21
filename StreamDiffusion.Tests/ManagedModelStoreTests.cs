using t3.streamdiffusion.V2;
using T3.Core.Settings;
using Xunit;

namespace StreamDiffusion.Tests;

/// <summary>
/// Pins the contract between the model store (Settings > AI Models) and the operator: exports install
/// into one subfolder of <c>StreamDiffusion/</c> per model, and the operator's empty-EnginePath scan
/// finds the first of them that passes V2 validation.
/// </summary>
public class ManagedModelStoreTests : IDisposable
{
    private readonly string _tempStore = Path.Combine(Path.GetTempPath(), "sd-store-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousModelsDirectory;

    public ManagedModelStoreTests()
    {
        _previousModelsDirectory = AiModelSettings.Config.ModelsDirectory;
        AiModelSettings.Config.ModelsDirectory = _tempStore;
    }

    public void Dispose()
    {
        AiModelSettings.Config.ModelsDirectory = _previousModelsDirectory ?? "";
        try { Directory.Delete(_tempStore, true); } catch { }
    }

    [Fact]
    public void FamilyDirectory_RespectsConfiguredStoreRoot()
    {
        Assert.Equal(Path.Combine(_tempStore, "StreamDiffusion"), AiModelSettings.FamilyDirectory(AiModelSettings.StreamDiffusionFamilyFolder));
    }

    [Fact]
    public void StoredFile_IsFoundInTheFamilyFolder()
    {
        var familyDirectory = AiModelSettings.FamilyDirectory(AiModelSettings.StreamDiffusionFamilyFolder);
        Directory.CreateDirectory(familyDirectory);
        File.WriteAllText(Path.Combine(familyDirectory, "probe.onnx"), "placeholder");

        Assert.True(AiModelSettings.TryGetStoredFile(AiModelSettings.StreamDiffusionFamilyFolder, "probe.onnx", out var path));
        Assert.Equal(Path.Combine(familyDirectory, "probe.onnx"), path);
    }

    [Fact]
    public void ExportSubfolder_LayoutIsFoundByTheOperatorScan()
    {
        // The layout the export tools produce: one folder with the V2 file set, inside the family folder
        var exportFolder = Path.Combine(AiModelSettings.FamilyDirectory(AiModelSettings.StreamDiffusionFamilyFolder), "streamdiffusion-v2");
        Directory.CreateDirectory(exportFolder);
        foreach (var file in new[] { "dit_step.onnx", "dit_manifest.json", "cross_kv.onnx", "kv_zeros.onnx", "text_encoder.onnx", "vae_encoder.onnx", "vae_decoder.onnx", "tokenizer.json" })
            File.WriteAllText(Path.Combine(exportFolder, file), "placeholder");

        Assert.Equal(exportFolder, ScanForValidExport());
    }

    [Fact]
    public void SiblingFolderWithMissingFiles_IsSkippedByTheOperatorScan()
    {
        var familyDirectory = AiModelSettings.FamilyDirectory(AiModelSettings.StreamDiffusionFamilyFolder);
        Directory.CreateDirectory(Path.Combine(familyDirectory, "broken-export"));
        File.WriteAllText(Path.Combine(familyDirectory, "broken-export", "dit_step.onnx"), "placeholder");

        Directory.CreateDirectory(Path.Combine(familyDirectory, "streamdiffusion-v2"));
        foreach (var file in new[] { "dit_step.onnx", "dit_manifest.json", "cross_kv.onnx", "kv_zeros.onnx", "text_encoder.onnx", "vae_encoder.onnx", "vae_decoder.onnx", "tokenizer.json" })
            File.WriteAllText(Path.Combine(familyDirectory, "streamdiffusion-v2", file), "placeholder");

        Assert.Equal(Path.Combine(familyDirectory, "streamdiffusion-v2"), ScanForValidExport());
    }

    [Fact]
    public void EmptyStore_ScanFindsNothing()
    {
        Assert.Null(ScanForValidExport());
    }

    /** Mirrors the operator's empty-EnginePath fallback (StreamDiffusion.FindManagedModelFolder). */
    private static string? ScanForValidExport()
    {
        var familyDirectory = AiModelSettings.FamilyDirectory(AiModelSettings.StreamDiffusionFamilyFolder);
        if (!Directory.Exists(familyDirectory))
            return null;

        foreach (var directory in Directory.GetDirectories(familyDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (StreamDiffusionV2Pipeline.ValidateModelDirectory(directory).Length == 0)
                return directory;
        }

        return null;
    }
}
