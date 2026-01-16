#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using T3.Core.Audio;
using T3.Core.Resource;

namespace T3.Editor.Gui.Audio;

internal static class AudioImageFactory
{
    internal static bool TryGetOrCreateImagePathForClip(AudioClipResourceHandle handle, [NotNullWhen(true)] out string? imagePath)
    {
        var audioClip = handle.Clip;
        
        imagePath = null;
        ArgumentNullException.ThrowIfNull(audioClip);

        if (string.IsNullOrEmpty(audioClip.FilePath) || handle.LoadingAttemptFailed)
            return false;
            
        if (_loadingClips.ContainsKey(audioClip))
        {
            imagePath = null;
            return false;
        }
           
        // Return from cache
        if (_imageForAudioFiles.TryGetValue(audioClip, out imagePath))
        {
            return true;
        }
        
        // Generate image, if file exists.
        if (!ResourceManager.TryResolveRelativePath(handle.Clip.FilePath, handle.Owner, out _, out _))
        {
            return false;
        }
        
            
        _loadingClips.TryAdd(audioClip, true);

        Task.Run(() =>
                 {
                     Log.Debug($"Creating sound image for {audioClip.FilePath}");
                     if (AudioImageGenerator.TryGenerateSoundSpectrumAndVolume(audioClip, handle.Owner, out var imagePath))
                     {
                         _imageForAudioFiles[audioClip] = imagePath;
                     }
                     else
                     {
                         Log.Error($"Failed to create sound image for {audioClip.FilePath}", handle.Owner);
                         _imageForAudioFiles.TryRemove(audioClip, out _);
                     }

                     _loadingClips.TryRemove(audioClip, out _);
                 });
            
        return false;
    }
    
    public static void ResetImageCache()
    {
        _imageForAudioFiles.Clear();
    }

    
    // TODO: should be a hashset, but there is no ConcurrentHashset -_-
    private static readonly ConcurrentDictionary<AudioClipDefinition, bool> _loadingClips = new();
    private static readonly ConcurrentDictionary<AudioClipDefinition, string> _imageForAudioFiles = new();
}