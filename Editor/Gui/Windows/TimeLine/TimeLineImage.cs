#nullable enable
using ImGuiNET;
using SharpDX.Direct3D11;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.Resource;
using T3.Editor.Gui.Audio;
using T3.Editor.Gui.Windows.TimeLine.TimeClips;
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

        // BPM lives on Playback now (post Phase B).
        var bpm = (float)TimeLineCanvas.Current.Playback.Bpm;
        var barsPerSecond = bpm / 240f;

        // An op-provided clip only gets LengthInSeconds once its stream loads (first playback) —
        // fall back to the probed duration so the background shows without playing the clip first.
        var lengthInSeconds = clip.LengthInSeconds;
        if (lengthInSeconds <= 0 && soundTrackHandle.Owner != null && !string.IsNullOrEmpty(clip.AssetPath))
        {
            AudioClipDurationCache.TryGetDurationSecs(clip.AssetPath, soundTrackHandle.Owner, out lengthInSeconds);
        }

        var songDurationInBars = (float)(lengthInSeconds * barsPerSecond);
        if (songDurationInBars <= 0)
            return;

        // Show exactly the audible window: start at the clip's source offset and clamp to its trimmed
        // end (End <= Start means "play until the content runs out"). The image UVs map the same window,
        // so a trimmed or offset clip's background stays aligned with what is actually heard.
        var sourceOffsetBars = (float)(clip.SourceOffsetSecs * barsPerSecond);
        var availableBars = songDurationInBars - sourceOffsetBars;

        // An op clip in background mode always shows its full source content — its TimeRange may
        // still hold a stale trim until the op's auto-extend has run (needs the stream loaded once).
        var hasClipEnd = clip.Display != AudioClipDisplay.BackgroundImage
                         && clip.TimeRange.End > clip.TimeRange.Start;
        var visibleBars = hasClipEnd
                              ? Math.Min(clip.TimeRange.End - clip.TimeRange.Start, availableBars)
                              : availableBars;
        if (visibleBars <= 0)
            return;

        var xMin = TimeLineCanvas.Current.TransformX(clip.TimeRange.Start);
        var xMax = TimeLineCanvas.Current.TransformX(clip.TimeRange.Start + visibleBars);

        var u0 = sourceOffsetBars / songDurationInBars;
        var u1 = (sourceOffsetBars + visibleBars) / songDurationInBars;

        if (_srv is { IsDisposed: false })
        {
            drawList.AddImage((IntPtr)_srv,
                              new Vector2(xMin, yMin),
                              new Vector2(xMax, yMin + size.Y),
                              new Vector2(u0, 0),
                              new Vector2(u1, 1));
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