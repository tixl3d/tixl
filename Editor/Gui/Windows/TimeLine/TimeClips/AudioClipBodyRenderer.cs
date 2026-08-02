#nullable enable

using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using SharpDX.Direct3D11;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Resource;
using T3.Editor.Gui.Audio;
using T3.Editor.Gui.Styling;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Draws the waveform image inside an <c>[AudioClip]</c> op-clip body. Mirrors
/// <see cref="DataClipBodyRenderer"/>: <see cref="TimeClipItem"/> calls <see cref="TryDraw"/> for
/// every op-clip, and this no-ops unless the op is an <see cref="IAudioClipProvider"/> with a file set.
/// </summary>
internal static class AudioClipBodyRenderer
{
    public static void TryDraw(Instance instance,
                               TimeClip timeClip,
                               Vector2 bodyMin,
                               Vector2 bodyMax,
                               ImDrawListPtr drawList)
    {
        // Op-type gate: only [AudioClip] (and only when it exposes a file path).
        if (instance is not IAudioClipProvider provider)
            return;
        if (instance is not IDescriptiveFilename descriptive)
            return;

        var path = descriptive.SourcePathSlot.TypedInputValue.Value;
        if (string.IsNullOrEmpty(path))
            return;

        var bodyWidth = bodyMax.X - bodyMin.X;
        var bodyHeight = bodyMax.Y - bodyMin.Y;
        if (bodyWidth < 3 || bodyHeight < 4)
            return;

        var clipData = provider.GetResourceHandle().Clip;
        var style = clipData.Style;
        if (!TryGetWaveformSrv(path, instance, style, out var srv))
            return;

        // Muted clips fade their waveform too, matching the faded clip body.
        var contentFade = clipData.IsMuted ? 0.3f : 1f;

        drawList.PushClipRect(bodyMin, bodyMax, true);

        // The body maps linearly to the clip's source window [SourceRange.Start, SourceRange.End]
        // (converted to seconds via BPM); the waveform image spans the whole file [0, length]. Draw
        // the image only across the body sub-rect where file content actually exists — so a clip
        // longer than its source isn't stretched, and a trimmed clip shows just its used slice.
        // File length comes from the played clip when available, else an async probe (so the crop
        // works immediately, not only after the clip plays once). Full-stretch fallback until known.
        var lengthSecs = provider.SourceLengthInSeconds;
        if (lengthSecs <= 0.0001)
            AudioClipDurationCache.TryGetDurationSecs(path, instance, out lengthSecs);

        var playback = Playback.Current;
        var drawn = false;
        if (lengthSecs > 0.0001 && playback != null)
        {
            var sourceStartSecs = playback.SecondsFromBars(timeClip.SourceRange.Start);
            var sourceEndSecs = playback.SecondsFromBars(timeClip.SourceRange.End);
            var sourceSpanSecs = sourceEndSecs - sourceStartSecs;
            var visibleStartSecs = Math.Max(sourceStartSecs, 0.0);
            var visibleEndSecs = Math.Min(sourceEndSecs, lengthSecs);

            // Looping clip: tile the *valid* source window (clamped to the file's content — end-trims
            // extend SourceRange past it, and the engine loops only the real content) across the body,
            // marking each repeat with a border line.
            var timeSpanSecs = playback.SecondsFromBars(timeClip.TimeRange.End) - playback.SecondsFromBars(timeClip.TimeRange.Start);
            var loopSpanSecs = visibleEndSecs - visibleStartSecs;
            if (clipData.IsLooping && loopSpanSecs > 0.0001 && timeSpanSecs > 0.0001)
            {
                var u0 = (float)(visibleStartSecs / lengthSecs);
                var u1 = (float)(visibleEndSecs / lengthSecs);
                var segmentWidth = (float)(loopSpanSecs / timeSpanSecs) * bodyWidth;
                if (segmentWidth > 2)
                {
                    var borderColor = UiColors.BackgroundFull.Fade(0.6f);
                    var segmentStartX = bodyMin.X;
                    while (segmentStartX < bodyMax.X)
                    {
                        var segmentEndX = Math.Min(segmentStartX + segmentWidth, bodyMax.X);
                        var visibleFraction = (segmentEndX - segmentStartX) / segmentWidth;
                        drawList.AddImage((IntPtr)srv,
                                          new Vector2(segmentStartX, bodyMin.Y),
                                          new Vector2(segmentEndX, bodyMax.Y),
                                          new Vector2(u0, 0),
                                          new Vector2(u0 + (u1 - u0) * visibleFraction, 1),
                                          UiColors.ForegroundFull.Fade(contentFade));

                        if (segmentStartX > bodyMin.X)
                        {
                            drawList.AddLine(new Vector2(segmentStartX, bodyMin.Y),
                                             new Vector2(segmentStartX, bodyMax.Y),
                                             borderColor, 1);
                        }

                        segmentStartX += segmentWidth;
                    }

                    drawn = true;
                }
            }
            else if (sourceSpanSecs > 0.0001 && visibleEndSecs > visibleStartSecs)
            {
                var x0 = (float)((visibleStartSecs - sourceStartSecs) / sourceSpanSecs);
                var x1 = (float)((visibleEndSecs - sourceStartSecs) / sourceSpanSecs);
                var u0 = (float)(visibleStartSecs / lengthSecs);
                var u1 = (float)(visibleEndSecs / lengthSecs);

                drawList.AddImage((IntPtr)srv,
                                  new Vector2(bodyMin.X + x0 * bodyWidth + 1, bodyMin.Y + 1),
                                  new Vector2(bodyMin.X + x1 * bodyWidth - 1, bodyMax.Y - 1),
                                  new Vector2(u0, 0), new Vector2(u1, 1),
                                  UiColors.ForegroundFull.Fade(contentFade));
                drawn = true;
            }
        }

        if (!drawn)
        {
            drawList.AddImage((IntPtr)srv,
                              bodyMin + new Vector2(1, 1),
                              bodyMax - new Vector2(1, 1),
                              Vector2.Zero, Vector2.One,
                              UiColors.ForegroundFull.Fade(0.5f * contentFade));
        }

        drawList.PopClipRect();
    }

    /// <summary>
    /// (Cached) shader-resource view of the file's waveform image. First call per asset kicks off a
    /// background generation via <see cref="AudioImageFactory"/>; later calls return the loaded SRV.
    /// </summary>
    private static bool TryGetWaveformSrv(string assetPath, Instance owner, AudioClipStyle style, [NotNullWhen(true)] out ShaderResourceView? srv)
    {
        srv = null;
        var handle = new AudioClipResourceHandle(new TimelineAudioClip { AssetPath = assetPath }, owner);
        if (!AudioImageFactory.TryGetOrCreateImagePathForClip(handle, style, out var imagePath))
            return false;

        if (_imageCache.TryGetValue(imagePath, out var entry))
        {
            srv = entry.Srv;
            return srv is { IsDisposed: false };
        }

        var resource = ResourceManager.CreateTextureResource(imagePath, owner);
        ShaderResourceView? newSrv = null;
        resource.Value?.CreateShaderResourceView(ref newSrv, imagePath);

        _imageCache[imagePath] = new CachedImage(resource, newSrv);
        srv = newSrv;
        return srv is { IsDisposed: false };
    }

    private readonly record struct CachedImage(Resource<Texture2D> Resource, ShaderResourceView? Srv);

    // Lifetime: entries live for the editor session (same as the waveform cache in the old item renderer).
    private static readonly Dictionary<string, CachedImage> _imageCache = new();
}
