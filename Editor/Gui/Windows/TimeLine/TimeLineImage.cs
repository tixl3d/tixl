#nullable enable
using ImGuiNET;
using SharpDX.Direct3D11;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.Resource;
using T3.Editor.Gui.Audio;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Editor.Gui.Windows.TimeLine;

internal sealed class TimeLineImage
{
    internal static void Draw(ImDrawListPtr drawList, AudioClipResourceHandle? soundTrackHandle)
    {
        if (soundTrackHandle == null)
            return;
        
        UpdateSoundTexture(soundTrackHandle);
        if (_loadedImagePath == null || TimeLineCanvas.Current == null)
            return;
            
        var clip = soundTrackHandle.Clip;

        var size = ImGui.GetWindowSize();
        var yMin = ImGui.GetWindowPos().Y;
        
            
        // BPM lives on Playback now (post Phase B). Same math as before, just sourced from there.
        var bpm = (float)TimeLineCanvas.Current.Playback.Bpm;
        var songDurationInBars = (float)(clip.LengthInSeconds * bpm / 240);
        var xMin = TimeLineCanvas.Current.TransformX(clip.TimeRange.Start);
        var xMax = TimeLineCanvas.Current.TransformX(songDurationInBars + clip.TimeRange.Start);
            
        if (_srv is { IsDisposed: false })
        {
            drawList.AddImage((IntPtr)_srv, 
                              new Vector2(xMin, yMin), 
                              new Vector2(xMax, yMin + size.Y));
        }
    }

    private static void UpdateSoundTexture(AudioClipResourceHandle soundtrackHandle)
    {
        // A different Style yields a different cache path, so style switches reload the texture.
        if (!AudioImageFactory.TryGetOrCreateImagePathForClip(soundtrackHandle, soundtrackHandle.Clip.Style, out var imagePath))
        {
            _loadedImagePath = null;
            return;
        }
                
        if (imagePath == _loadedImagePath)
            return;

        _textureResource?.Dispose();
        var resource = ResourceManager.CreateTextureResource(imagePath, soundtrackHandle.Owner);
        _textureResource = resource;
            
        if (resource.Value != null)
        {
            _loadedImagePath = imagePath;

            _textureResource.Value?.CreateShaderResourceView(ref _srv, imagePath);

        }
            
        _loadedImagePath = imagePath;
    }

    private static string? _loadedImagePath;
    private static ShaderResourceView? _srv;
    private static Resource<Texture2D>? _textureResource;
}