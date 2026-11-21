#nullable enable
using System.IO;
using T3.Core.Resource.Assets;
using T3.Core.Resource;
using T3.Core.Settings;

namespace Lib.io.video.mediapipe;

/// <summary>
/// Resolves the .tflite / .task model files the MediaPipe operators need: first from the model store
/// that the settings window's AI model browser downloads into, then from the package's Assets folder
/// through the asset registry, which is how models bundled with TiXL are found.
/// </summary>
public static class MediaPipeModelResolver
{
    /// <summary>Looks a model file up in the model store, then in the Mediapipe package assets.</summary>
    public static bool TryResolve(string fileName, IResourceConsumer consumer, out string fullPath)
    {
        if (AiModelSettings.TryGetStoredFile(AiModelSettings.MediapipeFamilyFolder, fileName, out var storedPath))
        {
            fullPath = Path.GetFullPath(storedPath);
            return true;
        }

        if (AssetRegistry.TryResolveAddress($"Mediapipe:{fileName}", consumer, out var resolvedPath, out _, logWarnings: false))
        {
            fullPath = Path.GetFullPath(resolvedPath);
            return true;
        }

        fullPath = Path.Combine(AiModelSettings.FamilyDirectory(AiModelSettings.MediapipeFamilyFolder), fileName);
        return false;
    }
}
