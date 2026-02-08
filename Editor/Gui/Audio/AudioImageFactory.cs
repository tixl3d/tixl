#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using T3.Core.Audio;
using T3.Core.Resource;
using T3.Core.Resource.Assets;

namespace T3.Editor.Gui.Audio;

internal static class AudioImageFactory
{
    internal static bool TryGetOrCreateImagePathForClip(AudioClipResourceHandle handle, [NotNullWhen(true)] out string? imagePath)
    {
        var audioClip = handle.Clip;
        
        imagePath = null;
        ArgumentNullException.ThrowIfNull(audioClip);

        if (string.IsNullOrEmpty(audioClip.Address) || handle.LoadingAttemptFailed)
            return false;
            
        if (_loadingClips.ContainsKey(audioClip.Address))
        {
            imagePath = null;
            return false;
        }
           
        // Return from cache
        if (_imageForAudioFiles.TryGetValue(audioClip.Address, out imagePath))
        {
            return true;
        }
        
        // Generate image, if file exists.
        if (!AssetRegistry.TryResolveAddress(handle.Clip.Address, handle.Owner, out _, out _))
        {
            return false;
        }
        
            
        _loadingClips.TryAdd(audioClip.Address, true);

        Task.Run(() =>
                 {
                     Log.Debug($"Creating sound image for {audioClip.Address}");
                     if (AudioImageGenerator.TryGenerateSoundSpectrumAndVolume(audioClip, handle.Owner, out var imagePath))
                     {
                         _imageForAudioFiles[audioClip.Address] = imagePath;
                     }
                     else
                     {
                         Log.Error($"Failed to create sound image for {audioClip.Address}", handle.Owner);
                         _imageForAudioFiles.TryRemove(audioClip.Address, out _);
                     }

                     _loadingClips.TryRemove(audioClip.Address, out _);
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