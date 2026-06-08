#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using T3.Core.Audio;
using T3.Core.Resource.Assets;

namespace T3.Editor.Gui.Audio;

internal static class AudioImageFactory
{
    internal static bool TryGetOrCreateImagePathForClip(AudioClipResourceHandle handle, [NotNullWhen(true)] out string? imagePath)
    {
        var audioClip = handle.Clip;
        
        imagePath = null;
        ArgumentNullException.ThrowIfNull(audioClip);

        if (string.IsNullOrEmpty(audioClip.AssetPath) || handle.LoadingAttemptFailed)
            return false;
            
        if (_loadingClips.ContainsKey(audioClip.AssetPath))
        {
            imagePath = null;
            return false;
        }
           
        // Return from cache
        if (_imageForAudioFiles.TryGetValue(audioClip.AssetPath, out imagePath))
        {
            return true;
        }
        
        // Generate image, if file exists.
        if (!AssetRegistry.TryResolveAddress(handle.Clip.AssetPath, handle.Owner, out _, out _))
        {
            return false;
        }
        
        _loadingClips.TryAdd(audioClip.AssetPath, true);

        Task.Run(() =>
                 {
                     try
                     {
                         Log.Debug($"Creating sound image for {audioClip.AssetPath}");
                         if (AudioImageGenerator.TryGenerateSoundSpectrumAndVolume(audioClip, handle.Owner, out var imagePath))
                         {
                             _imageForAudioFiles[audioClip.AssetPath] = imagePath;
                         }
                         else
                         {
                             Log.Error($"Failed to create sound image for {audioClip.AssetPath}", handle.Owner);
                             _imageForAudioFiles.TryRemove(audioClip.AssetPath, out _);
                         }
                     }
                     catch (Exception e)
                     {
                         // Without this, a throw faults the task silently and the AssetPath stays in
                         // _loadingClips forever — permanently blocking regeneration with no log line.
                         Log.Error($"Sound image generation threw for {audioClip.AssetPath}: {e}", handle.Owner);
                         _imageForAudioFiles.TryRemove(audioClip.AssetPath, out _);
                     }
                     finally
                     {
                         _loadingClips.TryRemove(audioClip.AssetPath, out _);
                     }
                 });
            
        return false;
    }
    
    public static void ResetImageCache()
    {
        _imageForAudioFiles.Clear();
    }

    
    // TODO: should be a hashset, but there is no ConcurrentHashset -_-
    private static readonly ConcurrentDictionary<string, bool> _loadingClips = new();
    private static readonly ConcurrentDictionary<string, string> _imageForAudioFiles = new();
}